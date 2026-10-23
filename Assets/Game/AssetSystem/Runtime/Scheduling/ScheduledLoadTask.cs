using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Game.AssetSystem.Core;
using UnityEngine;

namespace Game.AssetSystem.Scheduling
{
    public abstract class ScheduledLoadTaskBase
    {
        private readonly CancellationTokenSource _cancellation =
            new CancellationTokenSource();

        private readonly LoadScope _scope;

        protected ScheduledLoadTaskBase(
            string key,
            LoadScope scope,
            AssetLoadPriority priority)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "key must not be empty.",
                    nameof(key));
            }

            Key = key;
            _scope = scope;
            Priority = priority;
            Status = ScheduledLoadStatus.Queued;
        }

        public string Key { get; }

        public AssetLoadPriority Priority { get; }

        public ScheduledLoadStatus Status { get; protected set; }

        public bool IsDone
        {
            get
            {
                return Status == ScheduledLoadStatus.Succeeded ||
                       Status == ScheduledLoadStatus.Failed ||
                       Status == ScheduledLoadStatus.Canceled;
            }
        }

        public bool CancelRequested { get; private set; }

        internal ScheduledLoadErrorCode CancelCode { get; private set; }

        internal string CancelMessage { get; private set; }

        internal CancellationToken CancellationToken
        {
            get { return _cancellation.Token; }
        }

        internal LoadScope Scope
        {
            get { return _scope; }
        }

        public void Cancel()
        {
            if (!MainThreadPump.IsMainThread)
            {
                MainThreadPump.PostTerminal(
                    () => RequestCancel(
                        ScheduledLoadErrorCode.Canceled,
                        "Canceled by caller"));

                return;
            }

            RequestCancel(
                ScheduledLoadErrorCode.Canceled,
                "Canceled by caller");
        }

        internal void CancelFromScope()
        {
            if (!MainThreadPump.IsMainThread)
            {
                MainThreadPump.PostTerminal(
                    () => RequestCancel(
                        ScheduledLoadErrorCode.ScopeDisposed,
                        "Scope disposed"));

                return;
            }

            RequestCancel(
                ScheduledLoadErrorCode.ScopeDisposed,
                "Scope disposed");
        }

        private void RequestCancel(
            ScheduledLoadErrorCode code,
            string message)
        {
            if (IsDone)
            {
                return;
            }

            if (!CancelRequested)
            {
                CancelRequested = true;
                CancelCode = code;
                CancelMessage = message;

                try
                {
                    _cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The task is already finalizing. Ignore.
                }
            }

            if (Status == ScheduledLoadStatus.Queued)
            {
                CompleteCanceledOnMain(code, message);
            }
        }

        internal void MarkRunning()
        {
            if (Status != ScheduledLoadStatus.Queued)
            {
                throw new InvalidOperationException(
                    "Task can only start from Queued. key=" + Key +
                    " status=" + Status);
            }

            Status = ScheduledLoadStatus.Running;
        }

        internal abstract Task<object> ExecuteAsync(
            CancellationToken cancellationToken);

        internal abstract void CompleteSuccessOnMain(object value);

        internal abstract void CompleteFailureOnMain(string message);

        internal abstract void CompleteCanceledOnMain(
            ScheduledLoadErrorCode code,
            string message);

        /// <summary>
        /// Called when a worker produced a value that nobody will receive,
        /// because the task was canceled after the worker had already finished.
        /// The default implementation does nothing; typed tasks override it so
        /// the abandoned payload can be released instead of leaking.
        /// </summary>
        internal abstract void ReleaseAbandonedResult(object value);

        protected void UntrackFromScope()
        {
            _scope?.Untrack(this);
        }
    }

    public sealed class ScheduledLoadTask<T> : ScheduledLoadTaskBase
    {
        private readonly Func<CancellationToken, Task<T>> _worker;

        private readonly Action<T> _releaseAbandoned;

        private readonly TaskCompletionSource<ScheduledLoadResult<T>> _completion =
            new TaskCompletionSource<ScheduledLoadResult<T>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        internal ScheduledLoadTask(
            string key,
            LoadScope scope,
            AssetLoadPriority priority,
            Func<CancellationToken, Task<T>> worker,
            Action<T> releaseAbandoned = null)
            : base(key, scope, priority)
        {
            _worker = worker ??
                      throw new ArgumentNullException(nameof(worker));

            _releaseAbandoned = releaseAbandoned;
        }

        public Task<ScheduledLoadResult<T>> CompletionTask
        {
            get { return _completion.Task; }
        }

        public TaskAwaiter<ScheduledLoadResult<T>> GetAwaiter()
        {
            return _completion.Task.GetAwaiter();
        }

        internal override async Task<object> ExecuteAsync(
            CancellationToken cancellationToken)
        {
            var value = await _worker(cancellationToken).ConfigureAwait(false);
            return value;
        }

        internal override void CompleteSuccessOnMain(object value)
        {
            if (IsDone)
            {
                return;
            }

            Status = ScheduledLoadStatus.Succeeded;

            var result = new ScheduledLoadResult<T>(
                ScheduledLoadStatus.Succeeded,
                (T)value,
                default(ScheduledLoadError),
                Key,
                Priority);

            UntrackFromScope();
            _completion.TrySetResult(result);
        }

        internal override void CompleteFailureOnMain(string message)
        {
            if (IsDone)
            {
                return;
            }

            Status = ScheduledLoadStatus.Failed;

            var result = new ScheduledLoadResult<T>(
                ScheduledLoadStatus.Failed,
                default(T),
                new ScheduledLoadError(
                    ScheduledLoadErrorCode.WorkerFailed,
                    message),
                Key,
                Priority);

            UntrackFromScope();
            _completion.TrySetResult(result);
        }

        internal override void CompleteCanceledOnMain(
            ScheduledLoadErrorCode code,
            string message)
        {
            if (IsDone)
            {
                return;
            }

            Status = ScheduledLoadStatus.Canceled;

            var result = new ScheduledLoadResult<T>(
                ScheduledLoadStatus.Canceled,
                default(T),
                new ScheduledLoadError(code, message),
                Key,
                Priority);

            UntrackFromScope();
            _completion.TrySetResult(result);
        }

        internal override void ReleaseAbandonedResult(object value)
        {
            if (value == null)
            {
                return;
            }

            try
            {
                if (_releaseAbandoned != null)
                {
                    _releaseAbandoned((T)value);
                    return;
                }

                if (value is IReleasableAssetPayload releasable)
                {
                    releasable.ReleasePayload();
                    return;
                }

                if (value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
