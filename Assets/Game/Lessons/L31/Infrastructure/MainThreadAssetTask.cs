using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Game.AssetSystem.Infrastructure.L31
{
    public enum MainThreadTaskState
    {
        Pending,
        Succeeded,
        Failed,
        Canceled
    }

    public readonly struct MainThreadAssetResult<T>
    {
        public MainThreadTaskState State { get; }
        public T Value { get; }
        public string Error { get; }
        public string Source { get; }

        public MainThreadAssetResult(
            MainThreadTaskState state,
            T value,
            string error,
            string source)
        {
            State = state;
            Value = value;
            Error = error;
            Source = source;
        }

        public bool IsSuccess
        {
            get { return State == MainThreadTaskState.Succeeded; }
        }
    }

    public sealed class MainThreadAssetTask<T>
    {
        private readonly TaskCompletionSource<MainThreadAssetResult<T>> _completion =
            new TaskCompletionSource<MainThreadAssetResult<T>>();

        private MainThreadTaskState _state = MainThreadTaskState.Pending;
        private MainThreadAssetResult<T> _result;
        private int _completionThreadId;

        public MainThreadTaskState State
        {
            get { return _state; }
        }

        public int CompletionThreadId
        {
            get { return _completionThreadId; }
        }

        public bool IsDone
        {
            get { return _completion.Task.IsCompleted; }
        }

        public MainThreadAssetResult<T> Result
        {
            get { return _result; }
        }

        public TaskAwaiter<MainThreadAssetResult<T>> GetAwaiter()
        {
            return _completion.Task.GetAwaiter();
        }

        public void CompleteSuccess(T value, string source)
        {
            Complete(new MainThreadAssetResult<T>(
                MainThreadTaskState.Succeeded,
                value,
                null,
                source));
        }

        public void CompleteFailure(string error, string source)
        {
            Complete(new MainThreadAssetResult<T>(
                MainThreadTaskState.Failed,
                default(T),
                error,
                source));
        }

        public void Cancel(string reason, string source)
        {
            Complete(new MainThreadAssetResult<T>(
                MainThreadTaskState.Canceled,
                default(T),
                reason,
                source));
        }

        private void Complete(MainThreadAssetResult<T> result)
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

            _state = result.State;
            _result = result;
            _completionThreadId = Thread.CurrentThread.ManagedThreadId;
            _completion.TrySetResult(result);
        }
    }
}
