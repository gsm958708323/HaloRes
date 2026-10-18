using System;
using System.Collections.Generic;
using Game.AssetSystem.Infrastructure.L33;
using UnityEngine;

namespace Game.AssetSystem.Loading.L33
{
    public sealed class SharedLoadRegistry<T>
    {
        private readonly Dictionary<string, SharedAssetLoadOperation<T>> _operations =
            new Dictionary<string, SharedAssetLoadOperation<T>>();

        private readonly Func<string, IAssetLoadBackend<T>> _backendFactory;
        private readonly Action<T> _releaseAbandonedValue;

        private int _nextTraceId = 1;
        private int _createdOperationCount;
        private int _reusedOperationCount;
        private int _detachedOperationCount;
        private int _abandonedOperationCount;
        private int _staleOperationRemovedCount;

        public SharedLoadRegistry(
            Func<string, IAssetLoadBackend<T>> backendFactory,
            Action<T> releaseAbandonedValue = null)
        {
            _backendFactory = backendFactory ??
                              throw new ArgumentNullException(nameof(backendFactory));
            _releaseAbandonedValue = releaseAbandonedValue;
        }

        public int ActiveOperationCount
        {
            get
            {
                EnsureMainThread();
                return _operations.Count;
            }
        }

        public int CreatedOperationCount
        {
            get { return _createdOperationCount; }
        }

        public int ReusedOperationCount
        {
            get { return _reusedOperationCount; }
        }

        public int DetachedOperationCount
        {
            get { return _detachedOperationCount; }
        }

        public int AbandonedOperationCount
        {
            get { return _abandonedOperationCount; }
        }

        public int StaleOperationRemovedCount
        {
            get { return _staleOperationRemovedCount; }
        }

        public AssetLoadTask<T> Load(string key, string ownerName)
        {
            EnsureMainThread();

            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key must not be empty.", nameof(key));
            }

            if (_operations.TryGetValue(key, out var existing))
            {
                if (existing.TryAcquireWaiter(ownerName, out var reusedWaiter))
                {
                    _reusedOperationCount++;

                    Debug.Log(
                        "[Registry] reuse key=" + key +
                        " owner=" + SafeOwner(ownerName) +
                        " activeOps=" + _operations.Count +
                        " reused=" + _reusedOperationCount);

                    return reusedWaiter;
                }

                // Defensive path.
                // With the current state machine this should not normally happen,
                // because operations detach before they become unacquirable.
                _operations.Remove(key);
                _staleOperationRemovedCount++;

                Debug.LogWarning(
                    "[Registry] stale operation removed key=" + key +
                    " activeOps=" + _operations.Count +
                    " staleRemoved=" + _staleOperationRemovedCount);
            }

            var backend = _backendFactory(key);

            if (backend == null)
            {
                throw new InvalidOperationException(
                    "Backend factory returned null for key: " + key);
            }

            var traceId = _nextTraceId++;
            var operation = new SharedAssetLoadOperation<T>(
                key,
                traceId,
                backend,
                _releaseAbandonedValue,
                OnOperationDetached);

            _operations.Add(key, operation);
            _createdOperationCount++;

            if (!operation.TryAcquireWaiter(ownerName, out var waiter))
            {
                _operations.Remove(key);

                throw new InvalidOperationException(
                    "Newly created operation rejected its first waiter. key=" + key);
            }

            Debug.Log(
                "[Registry] create key=" + key +
                " trace=" + traceId +
                " owner=" + SafeOwner(ownerName) +
                " activeOps=" + _operations.Count +
                " created=" + _createdOperationCount);

            return waiter;
        }

        private void OnOperationDetached(
            SharedAssetLoadOperation<T> operation,
            SharedOperationDetachReason reason)
        {
            EnsureMainThread();

            if (_operations.TryGetValue(operation.Key, out var current) &&
                ReferenceEquals(current, operation))
            {
                _operations.Remove(operation.Key);
            }

            _detachedOperationCount++;

            if (reason == SharedOperationDetachReason.AllWaitersCanceled)
            {
                _abandonedOperationCount++;
            }

            Debug.Log(
                "[Registry] detached key=" + operation.Key +
                " trace=" + operation.TraceId +
                " reason=" + reason +
                " activeOps=" + _operations.Count +
                " detached=" + _detachedOperationCount +
                " abandonedOps=" + _abandonedOperationCount);
        }

        private static string SafeOwner(string ownerName)
        {
            return string.IsNullOrEmpty(ownerName) ? "Unnamed" : ownerName;
        }

        private static void EnsureMainThread()
        {
            if (!UnityMainThreadRunner.IsMainThread)
            {
                throw new InvalidOperationException(
                    "SharedLoadRegistry must be used on the Unity main thread.");
            }
        }
    }
}
