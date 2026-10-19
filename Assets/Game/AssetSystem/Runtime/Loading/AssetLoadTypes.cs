using System.Threading;

namespace Game.AssetSystem.Loading
{
    public enum AssetLoadStatus
    {
        Pending,
        Succeeded,
        Failed,
        Canceled
    }

    public enum AssetLoadErrorCode
    {
        None,
        Canceled,
        BackendFailed,
        InternalError
    }

    public readonly struct AssetLoadError
    {
        public AssetLoadErrorCode Code { get; }
        public string Message { get; }

        public AssetLoadError(AssetLoadErrorCode code, string message)
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
    {
        public AssetLoadStatus Status { get; }
        public T Value { get; }
        public AssetLoadError Error { get; }
        public string OwnerName { get; }
        public int TraceId { get; }
        public int CompletionThreadId { get; }

        public AssetLoadResult(
            AssetLoadStatus status,
            T value,
            AssetLoadError error,
            string ownerName,
            int traceId,
            int completionThreadId)
        {
            Status = status;
            Value = value;
            Error = error;
            OwnerName = ownerName;
            TraceId = traceId;
            CompletionThreadId = completionThreadId;
        }

        public bool IsSuccess
        {
            get { return Status == AssetLoadStatus.Succeeded; }
        }
    }

    public readonly struct AssetLoadBackendResult<T>
    {
        public bool Succeeded { get; }
        public T Value { get; }
        public string Error { get; }
        public bool WasCanceled { get; }

        private AssetLoadBackendResult(
            bool succeeded,
            T value,
            string error,
            bool wasCanceled)
        {
            Succeeded = succeeded;
            Value = value;
            Error = error;
            WasCanceled = wasCanceled;
        }

        public static AssetLoadBackendResult<T> Success(T value)
        {
            return new AssetLoadBackendResult<T>(true, value, null, false);
        }

        public static AssetLoadBackendResult<T> Failure(string error)
        {
            return new AssetLoadBackendResult<T>(false, default(T), error, false);
        }

        public static AssetLoadBackendResult<T> Canceled()
        {
            return new AssetLoadBackendResult<T>(false, default(T), null, true);
        }
    }

    public interface IAssetLoadBackend<T>
    {
        void BeginLoad(
            string key,
            CancellationToken cancellationToken,
            System.Action<AssetLoadBackendResult<T>> onCompleted);
    }
}
