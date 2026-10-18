using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Game.AssetSystem.Integration.L51
{
    public abstract class AssetHandleBase : IDisposable
    {
        private readonly AssetCache _cache;
        private bool _disposed;

        public AssetKey Key { get; }
        public AssetScope Scope { get; }

        internal AssetHandleBase(
            AssetCache cache,
            AssetKey key,
            AssetScope scope)
        {
            _cache = cache;
            Key = key;
            Scope = scope;
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

            if (!IntegrationMainThreadPump.IsMainThread)
            {
                IntegrationMainThreadPump.Post(Dispose);
                return;
            }

            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Scope?.UntrackHandle(this);
            _cache.OnHandleDisposed(this);
        }

        internal void DisposeFromScope()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cache.OnHandleDisposed(this);
        }
    }

    public sealed class AssetHandle<T> : AssetHandleBase
        where T : class
    {
        public T Value { get; }

        internal AssetHandle(
            AssetCache cache,
            AssetKey key,
            AssetScope scope,
            T value)
            : base(cache, key, scope)
        {
            Value = value;
        }
    }

    public abstract class AssetLoadTaskBase
    {
        protected readonly AssetCache Cache;

        public AssetKey Key { get; }
        public string Owner { get; }
        public AssetScope Scope { get; }
        public bool IsDone { get; private set; }

        protected AssetLoadTaskBase(
            AssetCache cache,
            AssetKey key,
            AssetScope scope,
            string owner)
        {
            Cache = cache;
            Key = key;
            Scope = scope;
            Owner = string.IsNullOrEmpty(owner) ? "Unnamed" : owner;
        }

        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            if (!IntegrationMainThreadPump.IsMainThread)
            {
                IntegrationMainThreadPump.Post(Cancel);
                return;
            }

            if (IsDone)
            {
                return;
            }

            Cache.CancelWaiter(this);
        }

        internal void CancelFromScope()
        {
            if (IsDone)
            {
                return;
            }

            if (!IntegrationMainThreadPump.IsMainThread)
            {
                IntegrationMainThreadPump.Post(CancelFromScope);
                return;
            }

            if (IsDone)
            {
                return;
            }

            Cache.CancelWaiterFromScope(this);
        }

        internal abstract AssetHandleBase CreateHandle(
            object payload,
            AssetCache cache,
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
            AssetScope scope,
            string owner)
            : base(cache, key, scope, owner)
        {
        }

        public Task<AssetLoadResult<T>> Completion
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
            string owner)
        {
            if (payload is T typed)
            {
                return new AssetHandle<T>(cache, Key, Scope, typed);
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

            var typed = (AssetHandle<T>)handle;

            _completion.TrySetResult(new AssetLoadResult<T>(
                AssetLoadStatus.Succeeded,
                typed,
                typed.Value,
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
                null,
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
                null,
                new AssetLoadError(code, message),
                Key,
                Owner));
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
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            if (!IntegrationMainThreadPump.IsMainThread)
            {
                IntegrationMainThreadPump.Post(Dispose);
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
                handle.DisposeFromScope();
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
        private enum State
        {
            NotLoaded = 0,
            Loading = 1,
            Ready = 2,
            Idle = 3,
            Failed = 4
        }

        private sealed class CacheEntry
        {
            public RuntimeAsset RuntimeAsset;
            public State State;
            public object Payload;
            public BundleLease Lease;
            public int UserRefs;
            public readonly List<AssetLoadTaskBase> Waiters =
                new List<AssetLoadTaskBase>();
            public string LastError;
        }

        private readonly ActiveContentContext _context;
        private readonly BundleRuntime _bundleRuntime;

        private readonly Dictionary<AssetKey, CacheEntry> _entries =
            new Dictionary<AssetKey, CacheEntry>();

        public int TotalLiveHandles { get; private set; }

        public int IdleAssetCount
        {
            get
            {
                var count = 0;

                foreach (var entry in _entries.Values)
                {
                    if (entry.State == State.Idle)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int ReadyAssetCount
        {
            get
            {
                var count = 0;

                foreach (var entry in _entries.Values)
                {
                    if (entry.State == State.Ready)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int LoadingAssetCount
        {
            get
            {
                var count = 0;

                foreach (var entry in _entries.Values)
                {
                    if (entry.State == State.Loading)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public AssetCache(
            ActiveContentContext context,
            BundleRuntime bundleRuntime)
        {
            _context = context ??
                       throw new ArgumentNullException(nameof(context));
            _bundleRuntime = bundleRuntime ??
                             throw new ArgumentNullException(nameof(bundleRuntime));
        }

        public AssetScope CreateScope(string name)
        {
            IntegrationMainThreadPump.EnsureMainThread(nameof(CreateScope));
            return new AssetScope(name);
        }

        public AssetLoadTask<T> AcquireAsync<T>(
            AssetKey key,
            AssetScope scope,
            string owner = "Unnamed")
            where T : class
        {
            IntegrationMainThreadPump.EnsureMainThread(nameof(AcquireAsync));

            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            var task = new AssetLoadTask<T>(this, key, scope, owner);

            if (scope.IsDisposed)
            {
                task.CompleteCanceled(
                    AssetLoadErrorCode.ScopeDisposed,
                    "Scope is already disposed.");
                return task;
            }

            scope.TrackTask(task);

            if (!_context.TryGetAsset(key, out var runtimeAsset))
            {
                task.CompleteFailure(
                    AssetLoadErrorCode.KeyNotFound,
                    "AssetKey not found: " + key.Value);
                return task;
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new CacheEntry
                {
                    RuntimeAsset = runtimeAsset,
                    State = State.NotLoaded
                };

                _entries.Add(key, entry);
            }

            if (entry.State == State.Ready || entry.State == State.Idle)
            {
                var handle = task.CreateHandle(entry.Payload, this, owner);

                if (handle == null)
                {
                    task.CompleteFailure(
                        AssetLoadErrorCode.TypeMismatch,
                        "Loaded payload type does not match requested type.");
                    return task;
                }

                entry.UserRefs++;

                if (entry.State == State.Idle)
                {
                    entry.State = State.Ready;
                }

                scope.TrackHandle(handle);
                TotalLiveHandles++;
                task.CompleteSuccess(handle);
                return task;
            }

            if (entry.State == State.Loading)
            {
                entry.Waiters.Add(task);
                return task;
            }

            if (entry.State == State.Failed)
            {
                entry.State = State.NotLoaded;
                entry.LastError = null;
            }

            entry.State = State.Loading;
            entry.Waiters.Add(task);
            BeginLoad(entry);
            return task;
        }

        public bool TryUnloadIdle(AssetKey key)
        {
            IntegrationMainThreadPump.EnsureMainThread(nameof(TryUnloadIdle));

            if (!_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            if (entry.State != State.Idle)
            {
                return false;
            }

            if (entry.UserRefs != 0)
            {
                return false;
            }

            if (entry.Payload == null && entry.Lease == null)
            {
                return false;
            }

            var payload = entry.Payload;
            var lease = entry.Lease;

            entry.Payload = null;
            entry.Lease = null;
            entry.State = State.NotLoaded;

            if (payload is IReleasableAssetPayload releasable)
            {
                try
                {
                    releasable.ReleasePayload();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            if (lease != null)
            {
                lease.Dispose();
            }

            return true;
        }

        public List<AssetKey> SnapshotIdleAssetKeys()
        {
            IntegrationMainThreadPump.EnsureMainThread(
                nameof(SnapshotIdleAssetKeys));

            var list = new List<AssetKey>();

            foreach (var pair in _entries)
            {
                if (pair.Value.State == State.Idle)
                {
                    list.Add(pair.Key);
                }
            }

            return list;
        }

        internal void OnHandleDisposed(AssetHandleBase handle)
        {
            IntegrationMainThreadPump.EnsureMainThread(
                nameof(OnHandleDisposed));

            if (TotalLiveHandles > 0)
            {
                TotalLiveHandles--;
            }

            if (!_entries.TryGetValue(handle.Key, out var entry))
            {
                return;
            }

            if (entry.UserRefs > 0)
            {
                entry.UserRefs--;
            }

            if (entry.UserRefs == 0 && entry.State == State.Ready)
            {
                entry.State = State.Idle;
            }
        }

        internal void CancelWaiter(AssetLoadTaskBase task)
        {
            IntegrationMainThreadPump.EnsureMainThread(
                nameof(CancelWaiter));

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
            }
        }

        internal void CancelWaiterFromScope(AssetLoadTaskBase task)
        {
            IntegrationMainThreadPump.EnsureMainThread(
                nameof(CancelWaiterFromScope));

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
            }
        }

        private async void BeginLoad(CacheEntry entry)
        {
            BundleAcquireResult bundleResult;

            try
            {
                bundleResult = await _bundleRuntime.AcquireAsync(
                    entry.RuntimeAsset.BundleId,
                    "Asset:" + entry.RuntimeAsset.Key.Value);
            }
            catch (Exception exception)
            {
                CompleteLoadFailure(
                    entry,
                    AssetLoadErrorCode.BundleLoadFailed,
                    exception.GetType().Name + ": " + exception.Message);
                return;
            }

            if (!bundleResult.Success)
            {
                CompleteLoadFailure(
                    entry,
                    AssetLoadErrorCode.BundleLoadFailed,
                    bundleResult.Error);
                return;
            }

            object payload;

            try
            {
                payload = bundleResult.Lease.LoadAsset(
                    entry.RuntimeAsset.InternalPath);
            }
            catch (Exception exception)
            {
                bundleResult.Lease.Dispose();

                CompleteLoadFailure(
                    entry,
                    AssetLoadErrorCode.AssetPayloadFailed,
                    exception.GetType().Name + ": " + exception.Message);

                return;
            }

            if (payload == null)
            {
                bundleResult.Lease.Dispose();

                CompleteLoadFailure(
                    entry,
                    AssetLoadErrorCode.AssetPayloadFailed,
                    "Payload is null.");
                return;
            }

            CompleteLoadSuccess(entry, payload, bundleResult.Lease);
        }

        private void CompleteLoadSuccess(
            CacheEntry entry,
            object payload,
            BundleLease lease)
        {
            IntegrationMainThreadPump.EnsureMainThread(
                nameof(CompleteLoadSuccess));

            entry.Payload = payload;
            entry.Lease = lease;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            var success = 0;

            for (var i = 0; i < waiters.Length; i++)
            {
                var waiter = waiters[i];

                if (waiter.IsDone)
                {
                    continue;
                }

                var handle = waiter.CreateHandle(payload, this, waiter.Owner);

                if (handle == null)
                {
                    waiter.CompleteFailure(
                        AssetLoadErrorCode.TypeMismatch,
                        "Loaded payload type does not match requested type.");
                    continue;
                }

                entry.UserRefs++;
                success++;
                waiter.Scope.TrackHandle(handle);
                TotalLiveHandles++;
                waiter.CompleteSuccess(handle);
            }

            if (success == 0)
            {
                entry.State = State.Idle;
            }
            else
            {
                entry.State = State.Ready;
            }
        }

        private void CompleteLoadFailure(
            CacheEntry entry,
            AssetLoadErrorCode code,
            string message)
        {
            IntegrationMainThreadPump.EnsureMainThread(
                nameof(CompleteLoadFailure));

            entry.State = State.Failed;
            entry.LastError = message;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            for (var i = 0; i < waiters.Length; i++)
            {
                if (!waiters[i].IsDone)
                {
                    waiters[i].CompleteFailure(code, message);
                }
            }
        }
    }
}
