using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Diagnostics;
using Game.AssetSystem.Core;
using Game.AssetSystem.Memory;
using UnityEngine;

namespace Game.AssetSystem.Assets
{
    public readonly struct AssetKey : IEquatable<AssetKey>
    {
        public string Value { get; }

        public AssetKey(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool Equals(AssetKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AssetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(AssetKey left, AssetKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AssetKey left, AssetKey right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class AssetEntry
    {
        public AssetKey Key;
        public BundleId BundleId;
        public string InternalAssetPath;
    }

    public sealed class AssetCatalog
    {
        private readonly Dictionary<AssetKey, AssetEntry> _entries =
            new Dictionary<AssetKey, AssetEntry>();

        public AssetCatalog(IEnumerable<AssetEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    throw new ArgumentException("AssetEntry cannot be null.");
                }

                _entries[entry.Key] = entry;
            }
        }

        public bool TryGet(AssetKey key, out AssetEntry entry)
        {
            return _entries.TryGetValue(key, out entry);
        }
    }

    public interface IAssetPayload
    {
        string DebugName { get; }
        void Release();
    }

    public enum AssetLoadStatus
    {
        Pending = 0,
        Succeeded = 1,
        Failed = 2,
        Canceled = 3
    }

    public enum AssetLoadErrorCode
    {
        None = 0,
        KeyNotFound = 1,
        TypeMismatch = 2,
        BundleLoadFailed = 3,
        AssetLoadFailed = 4,
        ScopeDisposed = 5,
        Canceled = 6,
        InternalError = 7
    }

    public readonly struct AssetLoadError
    {
        public AssetLoadErrorCode Code { get; }
        public string Message { get; }

        public AssetLoadError(
            AssetLoadErrorCode code,
            string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }

    public readonly struct AssetLoadResult<T>
        where T : class
    {
        public AssetLoadStatus Status { get; }
        public AssetHandle<T> Handle { get; }
        public T Value { get; }
        public AssetLoadError Error { get; }
        public AssetKey Key { get; }
        public string Owner { get; }

        public AssetLoadResult(
            AssetLoadStatus status,
            AssetHandle<T> handle,
            T value,
            AssetLoadError error,
            AssetKey key,
            string owner)
        {
            Status = status;
            Handle = handle;
            Value = value;
            Error = error;
            Key = key;
            Owner = owner;
        }

        public bool IsSuccess
        {
            get { return Status == AssetLoadStatus.Succeeded; }
        }
    }

    public abstract class AssetLoadTaskBase
    {
        protected readonly AssetCache Cache;

        public AssetKey Key { get; }
        public string Owner { get; }
        public AssetScope Scope { get; }
        public bool IsDone { get; protected set; }
        public bool CancelRequested { get; private set; }

        protected AssetLoadTaskBase(
            AssetCache cache,
            AssetKey key,
            string owner,
            AssetScope scope)
        {
            Cache = cache;
            Key = key;
            Owner = string.IsNullOrEmpty(owner) ? "Unnamed" : owner;
            Scope = scope;
        }

        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            if (!MainThreadPump.IsMainThread)
            {
                MainThreadPump.Post(Cancel);
                return;
            }

            if (IsDone)
            {
                return;
            }

            CancelRequested = true;
            Cache.CancelWaiter(this);
        }

        internal void CancelFromScope()
        {
            if (IsDone)
            {
                return;
            }

            if (!MainThreadPump.IsMainThread)
            {
                MainThreadPump.Post(() =>
                {
                    if (!IsDone)
                    {
                        Cache.CancelWaiterFromScope(this);
                    }
                });
                return;
            }

            Cache.CancelWaiterFromScope(this);
        }

        internal abstract AssetHandleBase CreateHandle(
            object payload,
            AssetCache cache,
            AssetRuntimeDiagnostics diagnostics,
            string owner);

        internal abstract void CompleteSuccess(AssetHandleBase handle);

        internal abstract void CompleteFailure(
            AssetLoadErrorCode code,
            string message);

        internal abstract void CompleteCanceled(
            AssetLoadErrorCode code,
            string message);

        protected void MarkDone()
        {
            IsDone = true;
            Scope?.UntrackTask(this);
        }
    }

    public sealed class AssetLoadTask<T> : AssetLoadTaskBase
        where T : class
    {
        private readonly TaskCompletionSource<AssetLoadResult<T>> _completion =
            new TaskCompletionSource<AssetLoadResult<T>>();

        internal AssetLoadTask(
            AssetCache cache,
            AssetKey key,
            string owner,
            AssetScope scope)
            : base(cache, key, owner, scope)
        {
        }

        public Task<AssetLoadResult<T>> CompletionTask
        {
            get { return _completion.Task; }
        }

        public TaskAwaiter<AssetLoadResult<T>> GetAwaiter()
        {
            return _completion.Task.GetAwaiter();
        }

        internal override AssetHandleBase CreateHandle(
            object payload,
            AssetCache cache,
            AssetRuntimeDiagnostics diagnostics,
            string owner)
        {
            if (payload is T typed)
            {
                return new AssetHandle<T>(
                    Key,
                    typed,
                    Scope,
                    cache.ReleaseAsset,
                    diagnostics,
                    owner);
            }

            return null;
        }

        internal override void CompleteSuccess(AssetHandleBase handle)
        {
            if (IsDone)
            {
                return;
            }

            MarkDone();

            var typedHandle = (AssetHandle<T>)handle;

            _completion.TrySetResult(new AssetLoadResult<T>(
                AssetLoadStatus.Succeeded,
                typedHandle,
                typedHandle.Value,
                default(AssetLoadError),
                Key,
                Owner));
        }

        internal override void CompleteFailure(
            AssetLoadErrorCode code,
            string message)
        {
            if (IsDone)
            {
                return;
            }

            MarkDone();

            _completion.TrySetResult(new AssetLoadResult<T>(
                AssetLoadStatus.Failed,
                null,
                default(T),
                new AssetLoadError(code, message),
                Key,
                Owner));
        }

        internal override void CompleteCanceled(
            AssetLoadErrorCode code,
            string message)
        {
            if (IsDone)
            {
                return;
            }

            MarkDone();

            _completion.TrySetResult(new AssetLoadResult<T>(
                AssetLoadStatus.Canceled,
                null,
                default(T),
                new AssetLoadError(code, message),
                Key,
                Owner));
        }
    }

    public abstract class AssetHandleBase : IDisposable
    {
        private readonly Action<AssetKey> _releaseAction;
        private readonly AssetRuntimeDiagnostics _diagnostics;
        private readonly int _diagnosticId;

        private bool _disposed;
        private bool _disposePosted;

        public AssetKey Key { get; }
        public AssetScope Scope { get; }

        protected AssetHandleBase(
            AssetKey key,
            AssetScope scope,
            Action<AssetKey> releaseAction,
            AssetRuntimeDiagnostics diagnostics,
            string owner)
        {
            Key = key;
            Scope = scope;
            _releaseAction = releaseAction;
            _diagnostics = diagnostics;

            if (_diagnostics != null)
            {
                _diagnosticId = _diagnostics.RegisterHandle(this, owner);
            }
        }

        public bool IsValid
        {
            get { return !_disposed; }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!MainThreadPump.IsMainThread)
            {
                if (_disposePosted)
                {
                    return;
                }

                _disposePosted = true;
                MainThreadPump.Post(Dispose);
                return;
            }

            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Scope?.UntrackHandle(this);

            if (_diagnostics != null && _diagnosticId > 0)
            {
                _diagnostics.UnregisterHandle(_diagnosticId);
            }

            _releaseAction?.Invoke(Key);
        }
    }

    public sealed class AssetHandle<T> : AssetHandleBase
        where T : class
    {
        public T Value { get; }

        internal AssetHandle(
            AssetKey key,
            T value,
            AssetScope scope,
            Action<AssetKey> releaseAction,
            AssetRuntimeDiagnostics diagnostics,
            string owner)
            : base(
                key,
                scope,
                releaseAction,
                diagnostics,
                owner)
        {
            Value = value;
        }
    }

    public sealed class AssetScope : IDisposable
    {
        private readonly HashSet<AssetLoadTaskBase> _tasks =
            new HashSet<AssetLoadTaskBase>();

        private readonly HashSet<AssetHandleBase> _handles =
            new HashSet<AssetHandleBase>();

        public string Name { get; }
        public bool IsDisposed { get; private set; }

        public AssetScope(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "name must not be empty.",
                    nameof(name));
            }

            Name = name;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            if (!MainThreadPump.IsMainThread)
            {
                MainThreadPump.Post(Dispose);
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            var tasks = new List<AssetLoadTaskBase>(_tasks);
            _tasks.Clear();

            for (var i = 0; i < tasks.Count; i++)
            {
                tasks[i].CancelFromScope();
            }

            var handles = new List<AssetHandleBase>(_handles);
            _handles.Clear();

            for (var i = 0; i < handles.Count; i++)
            {
                handles[i].Dispose();
            }
        }

        internal void TrackTask(AssetLoadTaskBase task)
        {
            if (IsDisposed)
            {
                task.CancelFromScope();
                return;
            }

            _tasks.Add(task);
        }

        internal void UntrackTask(AssetLoadTaskBase task)
        {
            _tasks.Remove(task);
        }

        internal void TrackHandle(AssetHandleBase handle)
        {
            if (IsDisposed)
            {
                handle.Dispose();
                return;
            }

            _handles.Add(handle);
        }

        internal void UntrackHandle(AssetHandleBase handle)
        {
            _handles.Remove(handle);
        }
    }

    public sealed class AssetCache
    {
        private enum AssetRuntimeState
        {
            NotLoaded = 0,
            Loading = 1,
            Ready = 2,
            Idle = 3,
            Failed = 4
        }

        private sealed class CacheEntry
        {
            public AssetEntry Info;
            public AssetRuntimeState State;
            public object Payload;
            public int UserRefCount;
            public BundleLease BundleLease;
            public List<AssetLoadTaskBase> Waiters =
                new List<AssetLoadTaskBase>();
            public string LastError;
        }

        private readonly Dictionary<AssetKey, CacheEntry> _entries =
            new Dictionary<AssetKey, CacheEntry>();

        private readonly BundleRuntime _bundleRuntime;
        private readonly AssetMemoryPolicy _policy;
        private readonly AssetRuntimeDiagnostics _diagnostics;

        public AssetCache(
            BundleRuntime bundleRuntime,
            AssetMemoryPolicy policy,
            AssetRuntimeDiagnostics diagnostics)
        {
            _bundleRuntime = bundleRuntime ??
                             throw new ArgumentNullException(nameof(bundleRuntime));
            _policy = policy ??
                       throw new ArgumentNullException(nameof(policy));
            _diagnostics = diagnostics ??
                         throw new ArgumentNullException(nameof(diagnostics));
        }

        public AssetRuntimeDiagnostics Diagnostics
        {
            get { return _diagnostics; }
        }

        internal void ReleaseAsset(AssetKey key)
        {
            EnsureMainThread();

            if (!_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            if (entry.UserRefCount > 0)
            {
                entry.UserRefCount--;
            }

            if (entry.UserRefCount == 0 &&
                entry.State == AssetRuntimeState.Ready)
            {
                entry.State = AssetRuntimeState.Idle;
                _policy.MarkAssetIdle(key, Time.realtimeSinceStartup);

                Debug.Log(
                    "[AssetCache] " +
                    key.Value +
                    " became Idle; payload and bundle lease retained.");
            }

            UpdateDiagnostics(entry);
        }

        public AssetLoadTask<T> AcquireAsync<T>(
            AssetEntry info,
            AssetScope scope,
            string owner = "Unnamed")
            where T : class
        {
            EnsureMainThread();

            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            var task = new AssetLoadTask<T>(
                this,
                info.Key,
                owner,
                scope);

            if (scope.IsDisposed)
            {
                task.CompleteCanceled(
                    AssetLoadErrorCode.ScopeDisposed,
                    "Scope is already disposed.");
                return task;
            }

            scope.TrackTask(task);

            var entry = GetOrCreateEntry(info);

            if (entry.State == AssetRuntimeState.Ready ||
                entry.State == AssetRuntimeState.Idle)
            {
                return CompleteFromReadyEntry(
                    info,
                    entry,
                    task,
                    scope,
                    owner);
            }

            if (entry.State == AssetRuntimeState.Loading)
            {
                entry.Waiters.Add(task);
                UpdateDiagnostics(entry);
                return task;
            }

            if (entry.State == AssetRuntimeState.Failed)
            {
                entry.State = AssetRuntimeState.NotLoaded;
                entry.LastError = null;
            }

            if (entry.State == AssetRuntimeState.NotLoaded)
            {
                entry.Waiters.Add(task);
                StartLoadAsync(entry);
                return task;
            }

            task.CompleteFailure(
                AssetLoadErrorCode.InternalError,
                "Asset is in an unsupported state: " + entry.State);

            return task;
        }

        public AssetLoadTask<T> CreateFailedTask<T>(
            AssetKey key,
            AssetScope scope,
            string owner,
            AssetLoadErrorCode code,
            string message)
            where T : class
        {
            EnsureMainThread();

            var task = new AssetLoadTask<T>(
                this,
                key,
                owner,
                scope);

            if (scope != null && !scope.IsDisposed)
            {
                scope.TrackTask(task);
            }

            task.CompleteFailure(code, message);
            return task;
        }

        public bool TryUnloadIdle(AssetKey key)
        {
            EnsureMainThread();

            if (!_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            if (entry.State != AssetRuntimeState.Idle)
            {
                return false;
            }

            if (entry.UserRefCount != 0)
            {
                return false;
            }

            if (entry.Payload == null && entry.BundleLease == null)
            {
                return false;
            }

            var payload = entry.Payload;
            var bundleLease = entry.BundleLease;

            entry.Payload = null;
            entry.BundleLease = null;
            entry.State = AssetRuntimeState.NotLoaded;
            entry.LastError = null;

            if (payload is IAssetPayload assetPayload)
            {
                try
                {
                    assetPayload.Release();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            if (bundleLease != null)
            {
                bundleLease.Dispose();
            }

            _policy.MarkAssetRemoved(key);
            _diagnostics.OnAssetRemoved(key);

            Debug.Log("[AssetCache] unloaded idle asset " + key.Value);
            return true;
        }

        internal void CancelWaiter(AssetLoadTaskBase task)
        {
            EnsureMainThread();

            if (task == null || task.IsDone)
            {
                return;
            }

            if (!_entries.TryGetValue(task.Key, out var entry))
            {
                task.CompleteCanceled(
                    AssetLoadErrorCode.Canceled,
                    "Canceled by owner.");
                return;
            }

            if (entry.Waiters.Remove(task))
            {
                task.CompleteCanceled(
                    AssetLoadErrorCode.Canceled,
                    "Canceled by owner: " + task.Owner);
                UpdateDiagnostics(entry);
            }
        }

        internal void CancelWaiterFromScope(AssetLoadTaskBase task)
        {
            EnsureMainThread();

            if (task == null || task.IsDone)
            {
                return;
            }

            if (!_entries.TryGetValue(task.Key, out var entry))
            {
                task.CompleteCanceled(
                    AssetLoadErrorCode.ScopeDisposed,
                    "Scope disposed.");
                return;
            }

            if (entry.Waiters.Remove(task))
            {
                task.CompleteCanceled(
                    AssetLoadErrorCode.ScopeDisposed,
                    "Scope disposed: " + task.Owner);
                UpdateDiagnostics(entry);
            }
        }

        private AssetLoadTask<T> CompleteFromReadyEntry<T>(
            AssetEntry info,
            CacheEntry entry,
            AssetLoadTask<T> task,
            AssetScope scope,
            string owner)
            where T : class
        {
            var handle = task.CreateHandle(
                entry.Payload,
                this,
                _diagnostics,
                owner);

            if (handle == null)
            {
                task.CompleteFailure(
                    AssetLoadErrorCode.TypeMismatch,
                    "Loaded payload type does not match requested type.");
                return task;
            }

            entry.UserRefCount++;

            if (entry.State == AssetRuntimeState.Idle)
            {
                entry.State = AssetRuntimeState.Ready;
            }

            _policy.MarkAssetActive(info.Key);
            scope.TrackHandle(handle);
            task.CompleteSuccess(handle);
            UpdateDiagnostics(entry);

            return task;
        }

        private void StartLoadAsync(CacheEntry entry)
        {
            entry.State = AssetRuntimeState.Loading;
            entry.LastError = null;
            UpdateDiagnostics(entry);

            Debug.Log("[AssetCache] start load " + entry.Info.Key.Value);
            RunLoadAsync(entry);
        }

        private async void RunLoadAsync(CacheEntry entry)
        {
            BundleAcquireResult bundleResult;

            try
            {
                bundleResult = await _bundleRuntime.AcquireAsync(
                    entry.Info.BundleId,
                    "Asset:" + entry.Info.Key.Value);
            }
            catch (Exception exception)
            {
                FinalizeLoadFailure(
                    entry,
                    AssetLoadErrorCode.InternalError,
                    exception.GetType().Name + ": " + exception.Message);
                return;
            }

            if (!bundleResult.Success)
            {
                FinalizeLoadFailure(
                    entry,
                    AssetLoadErrorCode.BundleLoadFailed,
                    bundleResult.Error.ToString());
                return;
            }

            object payload;

            try
            {
                payload = bundleResult.Lease.LoadAsset(
                    entry.Info.InternalAssetPath);
            }
            catch (Exception exception)
            {
                bundleResult.Lease.Dispose();

                FinalizeLoadFailure(
                    entry,
                    AssetLoadErrorCode.AssetLoadFailed,
                    exception.GetType().Name + ": " + exception.Message);

                return;
            }

            if (payload == null)
            {
                bundleResult.Lease.Dispose();

                FinalizeLoadFailure(
                    entry,
                    AssetLoadErrorCode.AssetLoadFailed,
                    "Asset payload is null. key=" +
                    entry.Info.Key.Value);

                return;
            }

            FinalizeLoadSuccess(
                entry,
                payload,
                bundleResult.Lease);
        }

        private void FinalizeLoadSuccess(
            CacheEntry entry,
            object payload,
            BundleLease bundleLease)
        {
            EnsureMainThread();

            entry.Payload = payload;
            entry.BundleLease = bundleLease;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            var successCount = 0;

            for (var i = 0; i < waiters.Length; i++)
            {
                var waiter = waiters[i];

                if (waiter.IsDone)
                {
                    continue;
                }

                var handle = waiter.CreateHandle(
                    payload,
                    this,
                    _diagnostics,
                    waiter.Owner);

                if (handle == null)
                {
                    waiter.CompleteFailure(
                        AssetLoadErrorCode.TypeMismatch,
                        "Loaded payload type does not match requested type.");
                    continue;
                }

                entry.UserRefCount++;
                successCount++;
                waiter.Scope.TrackHandle(handle);
                waiter.CompleteSuccess(handle);
            }

            if (successCount == 0)
            {
                entry.State = AssetRuntimeState.Idle;
                _policy.MarkAssetIdle(
                    entry.Info.Key,
                    Time.realtimeSinceStartup);

                Debug.Log(
                    "[AssetCache] loaded " +
                    entry.Info.Key.Value +
                    " with no active waiter; cached Idle.");
            }
            else
            {
                entry.State = AssetRuntimeState.Ready;
                _policy.MarkAssetActive(entry.Info.Key);

                Debug.Log(
                    "[AssetCache] load success " +
                    entry.Info.Key.Value +
                    " handles=" +
                    successCount);
            }

            UpdateDiagnostics(entry);
        }

        private void FinalizeLoadFailure(
            CacheEntry entry,
            AssetLoadErrorCode code,
            string message)
        {
            EnsureMainThread();

            entry.Payload = null;
            entry.State = AssetRuntimeState.Failed;
            entry.LastError = message;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            Debug.LogError(
                "[AssetCache] load failed " +
                entry.Info.Key.Value +
                ": " +
                code +
                " " +
                message);

            for (var i = 0; i < waiters.Length; i++)
            {
                if (!waiters[i].IsDone)
                {
                    waiters[i].CompleteFailure(code, message);
                }
            }

            UpdateDiagnostics(entry);
        }

        private CacheEntry GetOrCreateEntry(AssetEntry info)
        {
            if (!_entries.TryGetValue(info.Key, out var entry))
            {
                entry = new CacheEntry
                {
                    Info = info,
                    State = AssetRuntimeState.NotLoaded
                };

                _entries.Add(info.Key, entry);
            }

            return entry;
        }

        private void UpdateDiagnostics(CacheEntry entry)
        {
            _diagnostics.OnAssetStateChanged(
                entry.Info.Key,
                entry.State.ToString(),
                entry.UserRefCount,
                entry.Waiters.Count,
                entry.BundleLease != null);
        }

        private static void EnsureMainThread()
        {
            if (!MainThreadPump.IsMainThread)
            {
                throw new InvalidOperationException(
                    "AssetCache must be used on the Unity main thread.");
            }
        }
    }

    public interface IAssetService
    {
        AssetScope CreateScope(string name);

        AssetLoadTask<T> LoadAsync<T>(
            AssetKey key,
            AssetScope scope,
            string owner = "Unnamed")
            where T : class;
    }

    public sealed class AssetService : IAssetService
    {
        private readonly AssetCatalog _catalog;
        private readonly AssetCache _cache;

        public AssetService(
            AssetCatalog catalog,
            AssetCache cache)
        {
            _catalog = catalog ??
                       throw new ArgumentNullException(nameof(catalog));
            _cache = cache ??
                     throw new ArgumentNullException(nameof(cache));
        }

        public AssetScope CreateScope(string name)
        {
            EnsureMainThread();
            return new AssetScope(name);
        }

        public AssetLoadTask<T> LoadAsync<T>(
            AssetKey key,
            AssetScope scope,
            string owner = "Unnamed")
            where T : class
        {
            EnsureMainThread();

            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            if (!_catalog.TryGet(key, out var entry))
            {
                return _cache.CreateFailedTask<T>(
                    key,
                    scope,
                    owner,
                    AssetLoadErrorCode.KeyNotFound,
                    "AssetKey not found: " + key.Value);
            }

            return _cache.AcquireAsync<T>(
                entry,
                scope,
                owner);
        }

        private static void EnsureMainThread()
        {
            if (!MainThreadPump.IsMainThread)
            {
                throw new InvalidOperationException(
                    "AssetService must be used on the Unity main thread.");
            }
        }
    }
}
