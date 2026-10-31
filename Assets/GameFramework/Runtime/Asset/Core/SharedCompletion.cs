using System.Threading;
using System.Threading.Tasks;

namespace GameFramework.Asset.Core
{
    internal sealed class SharedCompletion<T>
    {
        private readonly TaskCompletionSource<T> _source = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _source.Task;

        public void SetResult(T value)
        {
            _source.TrySetResult(value);
        }

        public void SetException(System.Exception error)
        {
            _source.TrySetException(error);
            // All waiters may have cancelled. Observe the shared fault even in that case.
            _ = _source.Task.Exception;
        }

        public Task<T> WaitAsync(CancellationToken cancellationToken)
        {
            return TaskWait.WithCancellation(Task, cancellationToken);
        }
    }

    internal static class TaskWait
    {
        public static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
            {
                return await task;
            }

            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancellation))
            {
                var completed = await Task.WhenAny(task, cancellation.Task);
                if (completed == cancellation.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return await task;
            }
        }

        public static async Task WithCancellation(Task task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
            {
                await task;
                return;
            }

            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancellation))
            {
                var completed = await Task.WhenAny(task, cancellation.Task);
                if (completed == cancellation.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await task;
            }
        }
    }
}
