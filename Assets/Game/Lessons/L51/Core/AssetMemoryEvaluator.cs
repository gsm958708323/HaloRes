using System;

namespace Game.AssetSystem.Integration.L51
{
    public sealed class ReclaimReport
    {
        public int AssetsUnloaded;
        public int BundlesUnloaded;
        public int Attempts;

        public override string ToString()
        {
            return "assetsUnloaded=" + AssetsUnloaded +
                   " bundlesUnloaded=" + BundlesUnloaded +
                   " attempts=" + Attempts;
        }
    }

    public sealed class AssetMemoryEvaluator
    {
        private readonly AssetCache _cache;
        private readonly BundleRuntime _bundleRuntime;

        public int MaxUnloadsPerCall = 10;

        public AssetMemoryEvaluator(
            AssetCache cache,
            BundleRuntime bundleRuntime)
        {
            _cache = cache ??
                     throw new ArgumentNullException(nameof(cache));
            _bundleRuntime = bundleRuntime ??
                             throw new ArgumentNullException(nameof(bundleRuntime));
        }

        public ReclaimReport Evaluate()
        {
            IntegrationMainThreadPump.EnsureMainThread(nameof(Evaluate));

            var budget = Math.Max(0, MaxUnloadsPerCall);
            var report = new ReclaimReport();

            if (budget == 0)
            {
                return report;
            }

            var idleAssets = _cache.SnapshotIdleAssetKeys();

            for (var i = 0; i < idleAssets.Count; i++)
            {
                if (report.AssetsUnloaded + report.BundlesUnloaded >= budget)
                {
                    break;
                }

                report.Attempts++;

                if (_cache.TryUnloadIdle(idleAssets[i]))
                {
                    report.AssetsUnloaded++;
                }
            }

            if (report.AssetsUnloaded + report.BundlesUnloaded >= budget)
            {
                return report;
            }

            var idleBundles = _bundleRuntime.SnapshotIdleBundleIds();

            for (var i = 0; i < idleBundles.Count; i++)
            {
                if (report.AssetsUnloaded + report.BundlesUnloaded >= budget)
                {
                    break;
                }

                report.Attempts++;

                if (_bundleRuntime.TryUnloadIdle(idleBundles[i]))
                {
                    report.BundlesUnloaded++;
                }
            }

            return report;
        }
    }
}
