using System;
using System.Collections.Generic;
using Game.AssetSystem.Assets.L46;
using Game.AssetSystem.Bundles.L46;
using Game.AssetSystem.Infrastructure.L46;
using UnityEngine;

namespace Game.AssetSystem.Diagnostics.L46
{
    public sealed class AssetDiagEntry
    {
        public AssetKey Key;
        public string State;
        public int UserRefCount;
        public int WaitingCount;
        public bool HasBundleLease;
    }

    public sealed class BundleDiagEntry
    {
        public BundleId Id;
        public string Name;
        public string State;
        public int UserRefCount;
        public bool HasPayload;
    }

    public sealed class HandleDiagEntry
    {
        public int Id;
        public AssetKey Key;
        public string Owner;
        public string ScopeName;
        public bool ScopeDisposed;
        public double CreatedAt;
    }

    public sealed class AssetRuntimeSnapshot
    {
        public int IdleAssetCount;
        public int IdleBundleCount;
        public int LiveHandleCount;
        public int SuspectLeakHandleCount;

        public List<AssetDiagEntry> Assets =
            new List<AssetDiagEntry>();

        public List<BundleDiagEntry> Bundles =
            new List<BundleDiagEntry>();

        public List<HandleDiagEntry> LiveHandles =
            new List<HandleDiagEntry>();
    }

    public sealed class AssetRuntimeDiagnostics
    {
        private sealed class HandleRecord
        {
            public int Id;
            public AssetHandleBase Handle;
            public string Owner;
            public double CreatedAt;
        }

        private readonly Dictionary<AssetKey, AssetDiagEntry> _assets =
            new Dictionary<AssetKey, AssetDiagEntry>();

        private readonly Dictionary<BundleId, BundleDiagEntry> _bundles =
            new Dictionary<BundleId, BundleDiagEntry>();

        private readonly Dictionary<int, HandleRecord> _handles =
            new Dictionary<int, HandleRecord>();

        private int _nextHandleId = 1;

        public int RegisterHandle(
            AssetHandleBase handle,
            string owner)
        {
            EnsureMainThread();

            var id = _nextHandleId++;

            _handles.Add(id, new HandleRecord
            {
                Id = id,
                Handle = handle,
                Owner = string.IsNullOrEmpty(owner) ? "Unnamed" : owner,
                CreatedAt = Time.realtimeSinceStartup
            });

            return id;
        }

        public void UnregisterHandle(int handleId)
        {
            EnsureMainThread();
            _handles.Remove(handleId);
        }

        public void OnAssetStateChanged(
            AssetKey key,
            string state,
            int userRefCount,
            int waitingCount,
            bool hasBundleLease)
        {
            EnsureMainThread();

            _assets[key] = new AssetDiagEntry
            {
                Key = key,
                State = state,
                UserRefCount = userRefCount,
                WaitingCount = waitingCount,
                HasBundleLease = hasBundleLease
            };
        }

        public void OnAssetRemoved(AssetKey key)
        {
            EnsureMainThread();
            _assets.Remove(key);
        }

        public void OnBundleStateChanged(
            BundleId id,
            string name,
            string state,
            int userRefCount,
            bool hasPayload)
        {
            EnsureMainThread();

            _bundles[id] = new BundleDiagEntry
            {
                Id = id,
                Name = name,
                State = state,
                UserRefCount = userRefCount,
                HasPayload = hasPayload
            };
        }

        public void OnBundleRemoved(BundleId id)
        {
            EnsureMainThread();
            _bundles.Remove(id);
        }

        public AssetRuntimeSnapshot CaptureSnapshot()
        {
            EnsureMainThread();

            var snapshot = new AssetRuntimeSnapshot();

            foreach (var pair in _assets)
            {
                var entry = pair.Value;

                snapshot.Assets.Add(new AssetDiagEntry
                {
                    Key = entry.Key,
                    State = entry.State,
                    UserRefCount = entry.UserRefCount,
                    WaitingCount = entry.WaitingCount,
                    HasBundleLease = entry.HasBundleLease
                });

                if (entry.State == "Idle")
                {
                    snapshot.IdleAssetCount++;
                }
            }

            foreach (var pair in _bundles)
            {
                var entry = pair.Value;

                snapshot.Bundles.Add(new BundleDiagEntry
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    State = entry.State,
                    UserRefCount = entry.UserRefCount,
                    HasPayload = entry.HasPayload
                });

                if (entry.State == "Idle")
                {
                    snapshot.IdleBundleCount++;
                }
            }

            foreach (var pair in _handles)
            {
                var record = pair.Value;

                if (record.Handle == null || !record.Handle.IsValid)
                {
                    continue;
                }

                var scope = record.Handle.Scope;
                var scopeDisposed = scope != null && scope.IsDisposed;

                snapshot.LiveHandles.Add(new HandleDiagEntry
                {
                    Id = record.Id,
                    Key = record.Handle.Key,
                    Owner = record.Owner,
                    ScopeName = scope == null ? "<null>" : scope.Name,
                    ScopeDisposed = scopeDisposed,
                    CreatedAt = record.CreatedAt
                });

                if (scopeDisposed)
                {
                    snapshot.SuspectLeakHandleCount++;
                }
            }

            snapshot.LiveHandleCount = snapshot.LiveHandles.Count;

            snapshot.Assets.Sort((a, b) => string.Compare(
                a.Key.Value,
                b.Key.Value,
                StringComparison.Ordinal));

            snapshot.Bundles.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));

            return snapshot;
        }

        private static void EnsureMainThread()
        {
            if (!UnityMainThreadPump.IsMainThread)
            {
                throw new InvalidOperationException(
                    "AssetRuntimeDiagnostics must be used on the Unity main thread.");
            }
        }
    }
}
