using System;
using System.Collections.Generic;
using System.Threading;
using Game.AssetSystem.Infrastructure.L32;
using UnityEngine;

namespace Game.AssetSystem.Loading.L32
{
    public sealed class SharedAssetLoadOperation<T>
    {
        private readonly string _key;
        private readonly int _traceId;
        private readonly IAssetLoadBackend<T> _backend;
        private readonly Action<T> _releaseAbandonedValue;
        private readonly List<AssetLoadTask<T>> _activeWaiters =
            new List<AssetLoadTask<T>>();

        private readonly CancellationTokenSource _backendCancellation =
            new CancellationTokenSource();

        private bool _backendStarted;
        private bool _completed;
        private bool _cancellationRequested;

        public SharedAssetLoadOperation(
            string key,
            int traceId,
            IAssetLoadBackend<T> backend,
            Action<T> releaseAbandonedValue = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key must not be empty.", nameof(key));
            }

            _key = key;
            _traceId = traceId;
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _releaseAbandonedValue = releaseAbandonedValue;
        }

        public string Key
        {
            get { return _key; }
        }

        public int TraceId
        {
            get { return _traceId; }
        }

        public int ActiveWaiterCount
        {
            get
            {
                EnsureMainThread();
                return _activeWaiters.Count;
            }
        }

        public bool IsCompleted
        {
            get
            {
                EnsureMainThread();
                return _completed;
            }
        }

        public AssetLoadTask<T> AcquireWaiter(string ownerName)
        {
            EnsureMainThread();

            if (_completed)
            {
                throw new InvalidOperationException(
                    "SharedAssetLoadOperation has already completed. Create a new operation from the scheduler.");
            }

            if (_cancellationRequested)
            {
                throw new InvalidOperationException(
                    "All previous waiters canceled. Create a new SharedAssetLoadOperation for a new request.");
            }

            var waiter = new AssetLoadTask<T>(this, ownerName, _traceId);
            _activeWaiters.Add(waiter);

            Debug.Log(
                "[SharedLoad] acquire key=" + _key +
                " trace=" + _traceId +
                " owner=" + SafeOwnerName(waiter.OwnerName) +
                " activeWaiters=" + _activeWaiters.Count);

            if (!_backendStarted)
            {
                _backendStarted = true;
                Debug.Log(
                    "[SharedLoad] start backend key=" + _key +
                    " trace=" + _traceId);

                _backend.BeginLoad(
                    _key,
                    _backendCancellation.Token,
                    OnBackendCompleted);
            }

            return waiter;
        }

        internal void CancelWaiter(AssetLoadTask<T> waiter)
        {
            EnsureMainThread();

            if (_completed || waiter == null || waiter.IsDone)
            {
                return;
            }

            if (!_activeWaiters.Remove(waiter))
            {
                return;
            }

            waiter.CompleteCanceled("Canceled by owner");

            Debug.Log(
                "[SharedLoad] cancel key=" + _key +
                " trace=" + _traceId +
                " owner=" + SafeOwnerName(waiter.OwnerName) +
                " remainingWaiters=" + _activeWaiters.Count);

            if (_activeWaiters.Count == 0 && !_completed && !_cancellationRequested)
            {
                _cancellationRequested = true;

                Debug.Log(
                    "[SharedLoad] all waiters canceled; request backend cancellation key=" + _key +
                    " trace=" + _traceId);

                try
                {
                    _backendCancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The operation is already finalizing. This lesson ignores the race.
                }
            }
        }

        private void OnBackendCompleted(AssetLoadBackendResult<T> result)
        {
            // This callback can arrive on a worker thread.
            UnityMainThreadRunner.Post(() => HandleBackendCompletedOnMain(result));
        }

        private void HandleBackendCompletedOnMain(AssetLoadBackendResult<T> backendResult)
        {
            EnsureMainThread();

            if (_completed)
            {
                Debug.Log(
                    "[SharedLoad] duplicate completion ignored key=" + _key +
                    " trace=" + _traceId);

                if (backendResult.Succeeded)
                {
                    ReleaseAbandoned(backendResult.Value, "duplicate completion");
                }

                return;
            }

            _completed = true;

            var waiters = _activeWaiters.ToArray();
            _activeWaiters.Clear();

            if (waiters.Length == 0)
            {
                if (backendResult.Succeeded)
                {
                    ReleaseAbandoned(
                        backendResult.Value,
                        "late completion with no waiter");
                }

                Debug.Log(
                    "[SharedLoad] completion ignored: no active waiter key=" + _key +
                    " trace=" + _traceId +
                    " backendSucceeded=" + backendResult.Succeeded);

                return;
            }

            if (backendResult.Succeeded)
            {
                Debug.Log(
                    "[SharedLoad] complete success key=" + _key +
                    " trace=" + _traceId +
                    " waiters=" + waiters.Length);

                for (var i = 0; i < waiters.Length; i++)
                {
                    waiters[i].CompleteSuccess(backendResult.Value);
                }

                return;
            }

            var message = backendResult.WasCanceled
                ? "Backend canceled unexpectedly"
                : (backendResult.Error ?? "Backend failed");

            Debug.Log(
                "[SharedLoad] complete failure key=" + _key +
                " trace=" + _traceId +
                " waiters=" + waiters.Length +
                " message=" + message);

            for (var i = 0; i < waiters.Length; i++)
            {
                waiters[i].CompleteFailure(message);
            }
        }

        private void ReleaseAbandoned(T value, string reason)
        {
            Debug.Log(
                "[SharedLoad] release abandoned key=" + _key +
                " trace=" + _traceId +
                " reason=" + reason);

            _releaseAbandonedValue?.Invoke(value);
        }

        private static string SafeOwnerName(string value)
        {
            return string.IsNullOrEmpty(value) ? "Unnamed" : value;
        }

        private static void EnsureMainThread()
        {
            if (!UnityMainThreadRunner.IsMainThread)
            {
                throw new InvalidOperationException(
                    "SharedAssetLoadOperation must be used on the Unity main thread.");
            }
        }
    }
}
