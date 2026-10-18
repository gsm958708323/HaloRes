using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Game.AssetSystem.Infrastructure.L45;
using UnityEngine;

namespace Game.AssetSystem.Bundles.L45
{
    public readonly struct BundleId : IEquatable<BundleId>
    {
        public int Value { get; }

        public BundleId(int value)
        {
            Value = value;
        }

        public bool Equals(BundleId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is BundleId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(BundleId left, BundleId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BundleId left, BundleId right)
        {
            return !left.Equals(right);
        }
    }

    public enum BundleRuntimeState
    {
        NotLoaded = 0,
        Loading = 1,
        Ready = 2,
        Idle = 3,
        Unloading = 4,
        Failed = 5
    }

    public enum BundleLoadErrorCode
    {
        None = 0,
        Canceled = 1,
        BundleNotFound = 2,
        DependencyMissing = 3,
        DependencyCycle = 4,
        LoadFailed = 5,
        InternalError = 6
    }

    public readonly struct BundleLoadError
    {
        public BundleLoadErrorCode Code { get; }
        public string Message { get; }

        public BundleLoadError(BundleLoadErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }

    public sealed class BundleInfo
    {
        public BundleId Id;
        public string Name;
        public BundleId[] DirectDependencies;
    }

    public sealed class BundleCatalog
    {
        private readonly Dictionary<BundleId, BundleInfo> _bundles =
            new Dictionary<BundleId, BundleInfo>();

        public BundleCatalog(IEnumerable<BundleInfo> bundles)
        {
            if (bundles == null)
            {
                throw new ArgumentNullException(nameof(bundles));
            }

            foreach (var bundle in bundles)
            {
                if (bundle == null)
                {
                    throw new ArgumentException(
                        "BundleInfo cannot be null.");
                }

                _bundles[bundle.Id] = bundle;
            }
        }

        public bool TryGet(BundleId id, out BundleInfo info)
        {
            return _bundles.TryGetValue(id, out info);
        }

        public IEnumerable<BundleInfo> All
        {
            get { return _bundles.Values; }
        }
    }

    public interface IBundlePayload
    {
        string DebugName { get; }
        object LoadAsset(string internalAssetPath);
        void Release();
    }

    public interface IBundleLoader
    {
        Task<IBundlePayload> LoadAsync(
            BundleInfo bundle,
            CancellationToken cancellationToken);
    }

    public sealed class BundleAcquireTask
    {
        private readonly TaskCompletionSource<BundleAcquireResult> _completion =
            new TaskCompletionSource<BundleAcquireResult>();

        private readonly BundleRuntime _runtime;

        public BundleId Id { get; }
        public string Owner { get; }

        internal BundleAcquireTask(
            BundleRuntime runtime,
            BundleId id,
            string owner)
        {
            _runtime = runtime;
            Id = id;
            Owner = string.IsNullOrEmpty(owner) ? "Unnamed" : owner;
        }

        public bool IsDone
        {
            get { return _completion.Task.IsCompleted; }
        }

        public Task<BundleAcquireResult> CompletionTask
        {
            get { return _completion.Task; }
        }

        public void Cancel()
        {
            _runtime.CancelWaiter(this);
        }

        public TaskAwaiter<BundleAcquireResult> GetAwaiter()
        {
            return _completion.Task.GetAwaiter();
        }

        internal void SetSuccess(BundleLease lease)
        {
            _completion.TrySetResult(
                BundleAcquireResult.Succeeded(lease));
        }

        internal void SetCanceled(string message)
        {
            _completion.TrySetResult(
                BundleAcquireResult.Failed(
                    BundleLoadErrorCode.Canceled,
                    message));
        }

        internal void SetError(
            BundleLoadErrorCode code,
            string message)
        {
            _completion.TrySetResult(
                BundleAcquireResult.Failed(code, message));
        }
    }

    public readonly struct BundleAcquireResult
    {
        public bool Success { get; }
        public BundleLease Lease { get; }
        public BundleLoadError Error { get; }

        private BundleAcquireResult(
            bool success,
            BundleLease lease,
            BundleLoadError error)
        {
            Success = success;
            Lease = lease;
            Error = error;
        }

        public static BundleAcquireResult Succeeded(BundleLease lease)
        {
            return new BundleAcquireResult(
                true,
                lease,
                default(BundleLoadError));
        }

        public static BundleAcquireResult Failed(
            BundleLoadErrorCode code,
            string message)
        {
            return new BundleAcquireResult(
                false,
                null,
                new BundleLoadError(code, message));
        }
    }

    public sealed class BundleLease : IDisposable
    {
        private readonly BundleRuntime _runtime;
        private readonly BundleId _id;
        private bool _disposed;

        internal BundleLease(BundleRuntime runtime, BundleId id)
        {
            _runtime = runtime;
            _id = id;
        }

        public BundleId Id
        {
            get { return _id; }
        }

        public object LoadAsset(string internalAssetPath)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    "BundleLease",
                    "BundleLease has been disposed.");
            }

            return _runtime.LoadAssetFromLease(
                _id,
                internalAssetPath);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runtime.ReleaseLease(_id);
        }
    }

    public sealed class BundleRuntime
    {
        private sealed class BundleEntry
        {
            public BundleInfo Info;
            public BundleRuntimeState State;
            public IBundlePayload Payload;
            public int UserRefCount;
            public List<BundleAcquireTask> Waiters =
                new List<BundleAcquireTask>();
            public List<BundleLease> DependencyLeases =
                new List<BundleLease>();
            public string LastError;
        }

        private readonly BundleCatalog _catalog;
        private readonly IBundleLoader _loader;

        private readonly Dictionary<BundleId, BundleEntry> _entries =
            new Dictionary<BundleId, BundleEntry>();

        public BundleRuntime(
            BundleCatalog catalog,
            IBundleLoader loader)
        {
            _catalog = catalog ??
                       throw new ArgumentNullException(nameof(catalog));
            _loader = loader ??
                      throw new ArgumentNullException(nameof(loader));
        }

        public BundleAcquireTask AcquireAsync(
            BundleId id,
            string owner = "Unnamed",
            CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureMainThread();

            var task = new BundleAcquireTask(this, id, owner);

            if (cancellationToken.IsCancellationRequested)
            {
                task.SetCanceled("Canceled before start.");
                return task;
            }

            if (!_catalog.TryGet(id, out var info))
            {
                task.SetError(
                    BundleLoadErrorCode.BundleNotFound,
                    "Bundle not found: " + id);
                return task;
            }

            var entry = GetOrCreateEntry(info);

            if (entry.State == BundleRuntimeState.Ready ||
                entry.State == BundleRuntimeState.Idle)
            {
                entry.UserRefCount++;
                entry.State = BundleRuntimeState.Ready;

                var lease = new BundleLease(this, id);
                task.SetSuccess(lease);
                return task;
            }

            if (entry.State == BundleRuntimeState.Loading)
            {
                entry.Waiters.Add(task);
                return task;
            }

            if (entry.State == BundleRuntimeState.Failed)
            {
                entry.State = BundleRuntimeState.NotLoaded;
                entry.LastError = null;
            }

            if (entry.State == BundleRuntimeState.NotLoaded)
            {
                entry.Waiters.Add(task);
                StartLoad(entry);
                return task;
            }

            task.SetError(
                BundleLoadErrorCode.InternalError,
                "Bundle is in an unsupported state: " + entry.State);

            return task;
        }

        public bool TryUnloadIdle(BundleId id)
        {
            EnsureMainThread();

            if (!_entries.TryGetValue(id, out var entry))
            {
                return false;
            }

            if (entry.State != BundleRuntimeState.Idle)
            {
                return false;
            }

            if (entry.UserRefCount != 0)
            {
                return false;
            }

            if (entry.Payload == null)
            {
                return false;
            }

            entry.State = BundleRuntimeState.Unloading;

            for (var i = 0; i < entry.DependencyLeases.Count; i++)
            {
                entry.DependencyLeases[i].Dispose();
            }

            entry.DependencyLeases.Clear();

            var payload = entry.Payload;
            entry.Payload = null;

            try
            {
                payload.Release();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            entry.State = BundleRuntimeState.NotLoaded;
            entry.LastError = null;

            Debug.Log("[BundleRuntime] unloaded idle bundle " + entry.Info.Name);
            return true;
        }

        internal void CancelWaiter(BundleAcquireTask task)
        {
            EnsureMainThread();

            if (task == null || task.IsDone)
            {
                return;
            }

            if (!_entries.TryGetValue(task.Id, out var entry))
            {
                return;
            }

            if (!entry.Waiters.Remove(task))
            {
                return;
            }

            task.SetCanceled("Canceled by owner: " + task.Owner);
        }

        internal void ReleaseLease(BundleId id)
        {
            EnsureMainThread();

            if (!_entries.TryGetValue(id, out var entry))
            {
                return;
            }

            if (entry.UserRefCount > 0)
            {
                entry.UserRefCount--;
            }

            if (entry.UserRefCount == 0 &&
                entry.State == BundleRuntimeState.Ready)
            {
                entry.State = BundleRuntimeState.Idle;

                Debug.Log(
                    "[BundleRuntime] " +
                    entry.Info.Name +
                    " became Idle; payload retained.");
            }
        }

        internal object LoadAssetFromLease(
            BundleId id,
            string internalAssetPath)
        {
            EnsureMainThread();

            if (!_entries.TryGetValue(id, out var entry))
            {
                throw new InvalidOperationException(
                    "Bundle entry does not exist: " + id);
            }

            if (entry.State != BundleRuntimeState.Ready &&
                entry.State != BundleRuntimeState.Idle)
            {
                throw new InvalidOperationException(
                    "Bundle is not loaded. id=" + id +
                    ", state=" + entry.State);
            }

            if (entry.Payload == null)
            {
                throw new InvalidOperationException(
                    "Bundle payload is null. id=" + id);
            }

            return entry.Payload.LoadAsset(internalAssetPath);
        }

        private BundleEntry GetOrCreateEntry(BundleInfo info)
        {
            if (!_entries.TryGetValue(info.Id, out var entry))
            {
                entry = new BundleEntry
                {
                    Info = info,
                    State = BundleRuntimeState.NotLoaded
                };

                _entries.Add(info.Id, entry);
            }

            return entry;
        }

        private void StartLoad(BundleEntry entry)
        {
            EnsureMainThread();

            if (TryFindCycle(
                entry.Info.Id,
                new HashSet<BundleId>(),
                new HashSet<BundleId>(),
                new List<BundleId>(),
                out var cycle))
            {
                FailEntry(
                    entry,
                    BundleLoadErrorCode.DependencyCycle,
                    "Circular bundle dependency detected: " +
                    FormatCycle(cycle));
                return;
            }

            entry.State = BundleRuntimeState.Loading;
            entry.LastError = null;

            Debug.Log("[BundleRuntime] start load " + entry.Info.Name);
            RunLoadAsync(entry);
        }

        private async void RunLoadAsync(BundleEntry entry)
        {
            var dependencyLeases = new List<BundleLease>();

            try
            {
                if (entry.Info.DirectDependencies != null)
                {
                    for (var i = 0;
                         i < entry.Info.DirectDependencies.Length;
                         i++)
                    {
                        var dependencyId = entry.Info.DirectDependencies[i];

                        if (!_catalog.TryGet(dependencyId, out var dependencyInfo))
                        {
                            throw new BundleLoadException(
                                BundleLoadErrorCode.DependencyMissing,
                                "Missing dependency bundle: " +
                                dependencyId +
                                " for " +
                                entry.Info.Name);
                        }

                        var dependencyTask = AcquireAsync(
                            dependencyId,
                            "Dependency:" + entry.Info.Name);

                        var dependencyResult = await dependencyTask;

                        if (!dependencyResult.Success)
                        {
                            throw new BundleLoadException(
                                BundleLoadErrorCode.DependencyMissing,
                                "Failed to load dependency " +
                                dependencyInfo.Name +
                                ": " +
                                dependencyResult.Error);
                        }

                        dependencyLeases.Add(dependencyResult.Lease);
                    }
                }

                var payload = await _loader.LoadAsync(
                    entry.Info,
                    CancellationToken.None);

                if (payload == null)
                {
                    throw new BundleLoadException(
                        BundleLoadErrorCode.LoadFailed,
                        "Loader returned null payload for " +
                        entry.Info.Name);
                }

                FinalizeLoadSuccess(entry, payload, dependencyLeases);
            }
            catch (BundleLoadException exception)
            {
                FinalizeLoadFailure(
                    entry,
                    exception.Code,
                    exception.Message,
                    dependencyLeases);
            }
            catch (Exception exception)
            {
                FinalizeLoadFailure(
                    entry,
                    BundleLoadErrorCode.LoadFailed,
                    exception.GetType().Name + ": " + exception.Message,
                    dependencyLeases);
            }
        }

        private void FinalizeLoadSuccess(
            BundleEntry entry,
            IBundlePayload payload,
            List<BundleLease> dependencyLeases)
        {
            EnsureMainThread();

            entry.Payload = payload;
            entry.DependencyLeases = dependencyLeases;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            if (waiters.Length == 0)
            {
                entry.State = BundleRuntimeState.Idle;

                Debug.Log(
                    "[BundleRuntime] loaded " +
                    entry.Info.Name +
                    " with no waiters; cached Idle.");
                return;
            }

            entry.State = BundleRuntimeState.Ready;
            entry.UserRefCount += waiters.Length;

            Debug.Log(
                "[BundleRuntime] load success " +
                entry.Info.Name +
                " waiters=" +
                waiters.Length);

            for (var i = 0; i < waiters.Length; i++)
            {
                var lease = new BundleLease(this, entry.Info.Id);
                waiters[i].SetSuccess(lease);
            }
        }

        private void FinalizeLoadFailure(
            BundleEntry entry,
            BundleLoadErrorCode code,
            string message,
            List<BundleLease> dependencyLeases)
        {
            EnsureMainThread();

            for (var i = 0; i < dependencyLeases.Count; i++)
            {
                dependencyLeases[i].Dispose();
            }

            dependencyLeases.Clear();

            entry.Payload = null;
            entry.State = BundleRuntimeState.Failed;
            entry.LastError = message;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            Debug.LogError(
                "[BundleRuntime] load failed " +
                entry.Info.Name +
                ": " +
                code +
                " " +
                message);

            for (var i = 0; i < waiters.Length; i++)
            {
                if (!waiters[i].IsDone)
                {
                    waiters[i].SetError(code, message);
                }
            }
        }

        private void FailEntry(
            BundleEntry entry,
            BundleLoadErrorCode code,
            string message)
        {
            entry.State = BundleRuntimeState.Failed;
            entry.LastError = message;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            for (var i = 0; i < waiters.Length; i++)
            {
                if (!waiters[i].IsDone)
                {
                    waiters[i].SetError(code, message);
                }
            }
        }

        private bool TryFindCycle(
            BundleId current,
            HashSet<BundleId> visiting,
            HashSet<BundleId> visited,
            List<BundleId> path,
            out List<BundleId> cycle)
        {
            if (visiting.Contains(current))
            {
                var startIndex = path.IndexOf(current);

                cycle = startIndex >= 0
                    ? path.GetRange(startIndex, path.Count - startIndex)
                    : new List<BundleId> { current };

                cycle.Add(current);
                return true;
            }

            if (visited.Contains(current))
            {
                cycle = null;
                return false;
            }

            visiting.Add(current);
            path.Add(current);

            if (_catalog.TryGet(current, out var info) &&
                info.DirectDependencies != null)
            {
                for (var i = 0; i < info.DirectDependencies.Length; i++)
                {
                    if (TryFindCycle(
                        info.DirectDependencies[i],
                        visiting,
                        visited,
                        path,
                        out cycle))
                    {
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            visiting.Remove(current);
            visited.Add(current);

            cycle = null;
            return false;
        }

        private static string FormatCycle(List<BundleId> cycle)
        {
            if (cycle == null || cycle.Count == 0)
            {
                return "<empty>";
            }

            var parts = new string[cycle.Count];

            for (var i = 0; i < cycle.Count; i++)
            {
                parts[i] = cycle[i].ToString();
            }

            return string.Join(" -> ", parts);
        }

        private sealed class BundleLoadException : Exception
        {
            public BundleLoadErrorCode Code { get; }

            public BundleLoadException(
                BundleLoadErrorCode code,
                string message)
                : base(message)
            {
                Code = code;
            }
        }

        private static void EnsureMainThread()
        {
            if (!UnityMainThreadPump.IsMainThread)
            {
                throw new InvalidOperationException(
                    "BundleRuntime must be used on the Unity main thread.");
            }
        }
    }
}
