using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Game.AssetSystem.Infrastructure.L32;

namespace Game.AssetSystem.Loading.L32
{
    public sealed class AssetLoadTask<T>
    {
        private readonly SharedAssetLoadOperation<T> _operation;
        private readonly string _ownerName;
        private readonly int _traceId;

        private readonly TaskCompletionSource<AssetLoadResult<T>> _completion =
            new TaskCompletionSource<AssetLoadResult<T>>();

        private AssetLoadStatus _status = AssetLoadStatus.Pending;
        private AssetLoadResult<T> _result;
        private int _completionThreadId;

        internal AssetLoadTask(
            SharedAssetLoadOperation<T> operation,
            string ownerName,
            int traceId)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _ownerName = string.IsNullOrEmpty(ownerName) ? "Unnamed" : ownerName;
            _traceId = traceId;
        }

        public string OwnerName
        {
            get { return _ownerName; }
        }

        public AssetLoadStatus Status
        {
            get { return _status; }
        }

        public bool IsDone
        {
            get { return _completion.Task.IsCompleted; }
        }

        public int CompletionThreadId
        {
            get { return _completionThreadId; }
        }

        public AssetLoadResult<T> Result
        {
            get { return _result; }
        }

        internal bool IsActive
        {
            get { return !IsDone && _status == AssetLoadStatus.Pending; }
        }

        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            _operation.CancelWaiter(this);
        }

        internal void CompleteSuccess(T value)
        {
            Complete(new AssetLoadResult<T>(
                AssetLoadStatus.Succeeded,
                value,
                default(AssetLoadError),
                _ownerName,
                _traceId,
                0));
        }

        internal void CompleteFailure(string message)
        {
            Complete(new AssetLoadResult<T>(
                AssetLoadStatus.Failed,
                default(T),
                new AssetLoadError(AssetLoadErrorCode.BackendFailed, message),
                _ownerName,
                _traceId,
                0));
        }

        internal void CompleteCanceled(string message)
        {
            Complete(new AssetLoadResult<T>(
                AssetLoadStatus.Canceled,
                default(T),
                new AssetLoadError(AssetLoadErrorCode.Canceled, message),
                _ownerName,
                _traceId,
                0));
        }

        private void Complete(AssetLoadResult<T> result)
        {
            if (!UnityMainThreadRunner.IsMainThread)
            {
                UnityMainThreadRunner.Post(() => Complete(result));
                return;
            }

            if (_completion.Task.IsCompleted)
            {
                return;
            }

            var finalResult = new AssetLoadResult<T>(
                result.Status,
                result.Value,
                result.Error,
                result.OwnerName,
                result.TraceId,
                Thread.CurrentThread.ManagedThreadId);

            _status = finalResult.Status;
            _result = finalResult;
            _completionThreadId = finalResult.CompletionThreadId;
            _completion.TrySetResult(finalResult);
        }

        public TaskAwaiter<AssetLoadResult<T>> GetAwaiter()
        {
            return _completion.Task.GetAwaiter();
        }
    }
}
