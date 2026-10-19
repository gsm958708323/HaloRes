using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using Game.AssetSystem.Core;

namespace Game.AssetSystem.Integration
{
    public interface IBundlePayload
    {
        string DebugName { get; }
        object LoadAsset(string internalAssetPath);
        void Release();
    }

    public interface IBundlePayloadFactory
    {
        Task<IBundlePayload> LoadAsync(
            RuntimeBundle bundle,
            CancellationToken cancellationToken);
    }

    public sealed class BundleLease : IDisposable
    {
        private readonly BundleRuntime _owner;
        private readonly BundleId _id;
        private bool _disposed;

        internal BundleLease(BundleRuntime owner, BundleId id)
        {
            _owner = owner;
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
                throw new ObjectDisposedException("BundleLease");
            }

            return _owner.LoadAssetFromLease(_id, internalAssetPath);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // ReleaseLease asserts main-thread affinity; defer a background
            // dispose instead of throwing from it.
            if (!MainThreadPump.IsMainThread)
            {
                MainThreadPump.Post(() => _owner.ReleaseLease(_id));
                return;
            }

            _owner.ReleaseLease(_id);
        }
    }

    public sealed class BundleAcquireResult
    {
        public bool Success;
        public BundleLease Lease;
        public string Error;

        internal static BundleAcquireResult Ok(BundleLease lease)
        {
            return new BundleAcquireResult
            {
                Success = true,
                Lease = lease
            };
        }

        internal static BundleAcquireResult Fail(string error)
        {
            return new BundleAcquireResult
            {
                Success = false,
                Error = error
            };
        }
    }

    public sealed class BundleRuntime
    {
        private enum State
        {
            NotLoaded = 0,
            Loading = 1,
            Ready = 2,
            Idle = 3,
            Failed = 4
        }

        private sealed class Entry
        {
            public RuntimeBundle Bundle;
            public State State;
            public IBundlePayload Payload;
            public int UserRefs;
            public readonly List<Waiter> Waiters = new List<Waiter>();
            public string LastError;
        }

        private sealed class Waiter
        {
            public readonly TaskCompletionSource<BundleAcquireResult> Completion =
                new TaskCompletionSource<BundleAcquireResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            public string Owner;
        }

        private readonly ActiveContentContext _context;
        private readonly IBundlePayloadFactory _factory;

        private readonly Dictionary<BundleId, Entry> _entries =
            new Dictionary<BundleId, Entry>();

        public int LoadedPayloadCount { get; private set; }

        public int IdleBundleCount
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

        public int ReadyBundleCount
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

        public BundleRuntime(
            ActiveContentContext context,
            IBundlePayloadFactory factory)
        {
            _context = context ??
                       throw new ArgumentNullException(nameof(context));
            _factory = factory ??
                       throw new ArgumentNullException(nameof(factory));
        }

        public Task<BundleAcquireResult> AcquireAsync(
            BundleId id,
            string owner,
            CancellationToken cancellationToken = default)
        {
            MainThreadPump.EnsureMainThread(nameof(AcquireAsync));

            if (cancellationToken.IsCancellationRequested)
            {
                var tcs = new TaskCompletionSource<BundleAcquireResult>();
                tcs.SetResult(BundleAcquireResult.Fail("Canceled before start."));
                return tcs.Task;
            }

            if (!_context.TryGetBundle(id, out var runtimeBundle))
            {
                var tcs = new TaskCompletionSource<BundleAcquireResult>();
                tcs.SetResult(BundleAcquireResult.Fail(
                    "Bundle not found in active context: " + id));
                return tcs.Task;
            }

            if (!_entries.TryGetValue(id, out var entry))
            {
                entry = new Entry
                {
                    Bundle = runtimeBundle,
                    State = State.NotLoaded
                };

                _entries.Add(id, entry);
            }

            if (entry.State == State.Ready || entry.State == State.Idle)
            {
                entry.UserRefs++;

                if (entry.State == State.Idle)
                {
                    entry.State = State.Ready;
                }

                var tcs = new TaskCompletionSource<BundleAcquireResult>();
                tcs.SetResult(BundleAcquireResult.Ok(
                    new BundleLease(this, id)));
                return tcs.Task;
            }

            var waiter = new Waiter
            {
                Owner = string.IsNullOrEmpty(owner) ? "Unnamed" : owner
            };

            entry.Waiters.Add(waiter);

            if (entry.State != State.Loading)
            {
                entry.State = State.Loading;
                entry.LastError = null;
                BeginLoad(entry);
            }

            return waiter.Completion.Task;
        }

        private async void BeginLoad(Entry entry)
        {
            IBundlePayload payload = null;

            try
            {
                payload = await _factory.LoadAsync(
                    entry.Bundle,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                CompleteLoadFailure(
                    entry,
                    exception.GetType().Name + ": " + exception.Message);
                return;
            }

            if (payload == null)
            {
                CompleteLoadFailure(entry, "Payload is null.");
                return;
            }

            CompleteLoadSuccess(entry, payload);
        }

        private void CompleteLoadSuccess(Entry entry, IBundlePayload payload)
        {
            MainThreadPump.EnsureMainThread(
                nameof(CompleteLoadSuccess));

            entry.Payload = payload;
            LoadedPayloadCount++;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            if (waiters.Length == 0)
            {
                entry.State = State.Idle;
                return;
            }

            entry.State = State.Ready;
            entry.UserRefs += waiters.Length;

            for (var i = 0; i < waiters.Length; i++)
            {
                waiters[i].Completion.TrySetResult(
                    BundleAcquireResult.Ok(
                        new BundleLease(this, entry.Bundle.Id)));
            }
        }

        private void CompleteLoadFailure(Entry entry, string error)
        {
            MainThreadPump.EnsureMainThread(
                nameof(CompleteLoadFailure));

            entry.State = State.Failed;
            entry.LastError = error;

            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();

            for (var i = 0; i < waiters.Length; i++)
            {
                waiters[i].Completion.TrySetResult(
                    BundleAcquireResult.Fail(error));
            }
        }

        internal object LoadAssetFromLease(
            BundleId id,
            string internalAssetPath)
        {
            MainThreadPump.EnsureMainThread(
                nameof(LoadAssetFromLease));

            if (!_entries.TryGetValue(id, out var entry))
            {
                throw new InvalidOperationException(
                    "Bundle not loaded: " + id);
            }

            if (entry.State != State.Ready && entry.State != State.Idle)
            {
                throw new InvalidOperationException(
                    "Bundle not ready, state=" + entry.State);
            }

            if (entry.Payload == null)
            {
                throw new InvalidOperationException(
                    "Bundle payload missing: " + id);
            }

            return entry.Payload.LoadAsset(internalAssetPath);
        }

        internal void ReleaseLease(BundleId id)
        {
            MainThreadPump.EnsureMainThread(nameof(ReleaseLease));

            if (!_entries.TryGetValue(id, out var entry))
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

        public bool TryUnloadIdle(BundleId id)
        {
            MainThreadPump.EnsureMainThread(nameof(TryUnloadIdle));

            if (!_entries.TryGetValue(id, out var entry))
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

            if (entry.Payload == null)
            {
                return false;
            }

            var payload = entry.Payload;
            entry.Payload = null;
            entry.State = State.NotLoaded;

            if (LoadedPayloadCount > 0)
            {
                LoadedPayloadCount--;
            }

            try
            {
                payload.Release();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            return true;
        }

        public List<BundleId> SnapshotIdleBundleIds()
        {
            MainThreadPump.EnsureMainThread(
                nameof(SnapshotIdleBundleIds));

            var list = new List<BundleId>();

            foreach (var pair in _entries)
            {
                if (pair.Value.State == State.Idle)
                {
                    list.Add(pair.Key);
                }
            }

            return list;
        }
    }
}
