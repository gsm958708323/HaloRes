using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Game.AssetSystem.Bundles.L44
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

        public BundleLoadError(
            BundleLoadErrorCode code,
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
            Owner = string.IsNullOrEmpty(owner)
                ? "Unnamed"
                : owner;
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

    public sealed class BundleSnapshotEntry
    {
        public BundleId Id;
        public string Name;
        public BundleRuntimeState State;
        public int UserRefCount;
        public int WaitingCount;
        public bool HasPayload;
        public string LastError;
    }

    public sealed class BundleRuntimeSnapshot
    {
        public int TotalUserRefs;
        public int LoadedPayloadCount;
        public int LoadingCount;
        public int IdleCount;

        public List<BundleSnapshotEntry> Entries =
            new List<BundleSnapshotEntry>();
    }
}
