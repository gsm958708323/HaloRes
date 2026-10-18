using System;
using System.Collections.Generic;
using Game.AssetSystem.Assets.L46;
using Game.AssetSystem.Bundles.L46;
using Game.AssetSystem.Infrastructure.L46;
using UnityEngine;

namespace Game.AssetSystem.Memory.L46
{
    public enum MemoryPressureLevel
    {
        Normal = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public sealed class MemoryReclaimReport
    {
        public int AssetsUnloaded;
        public int BundlesUnloaded;
        public int Attempts;
        public int RemainingAssets;
        public int RemainingBundles;

        public override string ToString()
        {
            return "assetsUnloaded=" + AssetsUnloaded +
                   " bundlesUnloaded=" + BundlesUnloaded +
                   " attempts=" + Attempts +
                   " remainingAssets=" + RemainingAssets +
                   " remainingBundles=" + RemainingBundles;
        }
    }

    public sealed class AssetMemoryPolicy
    {
        private readonly Dictionary<AssetKey, double> _idleAssets =
            new Dictionary<AssetKey, double>();

        private readonly Dictionary<BundleId, double> _idleBundles =
            new Dictionary<BundleId, double>();

        private Func<AssetKey, bool> _unloadAsset;
        private Func<BundleId, bool> _unloadBundle;

        public int MaxUnloadsPerFrame = 2;
        public double AssetIdleGraceSeconds = 0.5;
        public double BundleIdleGraceSeconds = 1.0;
        public bool AllowBundleUnload = true;

        public int IdleAssetCount
        {
            get { return _idleAssets.Count; }
        }

        public int IdleBundleCount
        {
            get { return _idleBundles.Count; }
        }

        public void Configure(
            Func<AssetKey, bool> unloadAsset,
            Func<BundleId, bool> unloadBundle)
        {
            _unloadAsset = unloadAsset;
            _unloadBundle = unloadBundle;
        }

        public void MarkAssetIdle(AssetKey key, double now)
        {
            EnsureMainThread();
            _idleAssets[key] = now;
        }

        public void MarkAssetActive(AssetKey key)
        {
            EnsureMainThread();
            _idleAssets.Remove(key);
        }

        public void MarkAssetRemoved(AssetKey key)
        {
            EnsureMainThread();
            _idleAssets.Remove(key);
        }

        public void MarkBundleIdle(BundleId id, double now)
        {
            EnsureMainThread();
            _idleBundles[id] = now;
        }

        public void MarkBundleActive(BundleId id)
        {
            EnsureMainThread();
            _idleBundles.Remove(id);
        }

        public void MarkBundleRemoved(BundleId id)
        {
            EnsureMainThread();
            _idleBundles.Remove(id);
        }

        public MemoryReclaimReport Evaluate(
            double now,
            MemoryPressureLevel level)
        {
            EnsureMainThread();

            var report = new MemoryReclaimReport
            {
                RemainingAssets = _idleAssets.Count,
                RemainingBundles = _idleBundles.Count
            };

            if (level == MemoryPressureLevel.Normal)
            {
                return report;
            }

            var budget = Math.Max(0, MaxUnloadsPerFrame);

            if (budget == 0)
            {
                return report;
            }

            var assetGrace = level >= MemoryPressureLevel.High
                ? 0.0
                : AssetIdleGraceSeconds;

            var bundleGrace = level >= MemoryPressureLevel.High
                ? 0.0
                : BundleIdleGraceSeconds;

            var assetCandidates = CollectSorted(
                _idleAssets,
                now,
                assetGrace);

            var assetsUnloaded = 0;

            for (var i = 0;
                 i < assetCandidates.Count && assetsUnloaded < budget;
                 i++)
            {
                var key = assetCandidates[i].Key;
                report.Attempts++;

                if (_unloadAsset == null)
                {
                    break;
                }

                if (_unloadAsset(key))
                {
                    assetsUnloaded++;
                    _idleAssets.Remove(key);
                }
            }

            report.AssetsUnloaded = assetsUnloaded;
            budget -= assetsUnloaded;

            if (AllowBundleUnload && budget > 0)
            {
                var bundleCandidates = CollectSorted(
                    _idleBundles,
                    now,
                    bundleGrace);

                var bundlesUnloaded = 0;

                for (var i = 0;
                     i < bundleCandidates.Count && bundlesUnloaded < budget;
                     i++)
                {
                    var id = bundleCandidates[i].Key;
                    report.Attempts++;

                    if (_unloadBundle == null)
                    {
                        break;
                    }

                    if (_unloadBundle(id))
                    {
                        bundlesUnloaded++;
                        _idleBundles.Remove(id);
                    }
                }

                report.BundlesUnloaded = bundlesUnloaded;
            }

            report.RemainingAssets = _idleAssets.Count;
            report.RemainingBundles = _idleBundles.Count;

            return report;
        }

        private static List<KeyValuePair<TKey, double>> CollectSorted<TKey>(
            Dictionary<TKey, double> dictionary,
            double now,
            double grace)
        {
            var result = new List<KeyValuePair<TKey, double>>();

            foreach (var pair in dictionary)
            {
                if (now - pair.Value >= grace)
                {
                    result.Add(pair);
                }
            }

            result.Sort((a, b) => a.Value.CompareTo(b.Value));
            return result;
        }

        private static void EnsureMainThread()
        {
            if (!UnityMainThreadPump.IsMainThread)
            {
                throw new InvalidOperationException(
                    "AssetMemoryPolicy must be used on the Unity main thread.");
            }
        }
    }
}
