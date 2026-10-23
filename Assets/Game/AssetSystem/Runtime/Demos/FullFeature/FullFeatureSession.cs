using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Hosting;
using Game.AssetSystem.Memory;
using Game.AssetSystem.Patch;
using Game.AssetSystem.Release;
using Game.AssetSystem.Simulation;
using UnityEngine;

namespace Game.AssetSystem.Demos.FullFeature
{
    /// <summary>
    /// The full-feature demo's client side, now a thin shell over
    /// <see cref="AssetSystemFacade"/>.
    ///
    /// Everything that is actually framework behaviour — install, activate,
    /// rebuild, load, release, reclaim — is in the facade, which is what a game
    /// would use. What is left here is what makes it a demo: the build-output
    /// folder acting as a CDN, the simulated transport with its interrupt
    /// budget, and a rolling log the dashboard can render.
    ///
    /// The dashboard and the PlayMode scenario both drive this same object, so
    /// what the test proves is what the UI does.
    /// </summary>
    public sealed class FullFeatureSession
    {
        private const int MaxLogLines = 40;

        private readonly string _serverRoot;
        private readonly AssetSystemFacade _facade;
        private readonly List<string> _logs = new List<string>();

        public FullFeatureSession(string clientRoot = null, string serverRoot = null)
        {
            var resolvedClientRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(clientRoot)
                    ? FullFeatureLayout.DefaultClientRoot
                    : clientRoot);

            _serverRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(serverRoot)
                    ? FullFeatureLayout.BuildRoot
                    : serverRoot);

            _facade = new AssetSystemFacade(new AssetSystemOptions
            {
                ClientRoot = resolvedClientRoot,
                BaseContentRoot = _serverRoot,
                BaseVersion = FullFeatureLayout.BaseVersion,
                ManifestFileName = FullFeatureLayout.ManifestFileName,
                PlatformKey = FullFeatureLayout.PlatformKey,
                MaxStartsPerFrame = 2,
                MaxConcurrentBundleLoads = 4,
                UseLoadGate = true
            });

            _facade.Log += AppendLog;
        }

        /// <summary>
        /// The framework entry point this demo is a window onto. Exposed so a
        /// reader can compare the demo calls with the game-facing API.
        /// </summary>
        public AssetSystemFacade Facade
        {
            get { return _facade; }
        }

        public string ClientRoot
        {
            get { return _facade.ClientRoot; }
        }

        public string ServerRoot
        {
            get { return _serverRoot; }
        }

        public string ContentRoot
        {
            get { return _facade.ContentRoot; }
        }

        public string StagingRoot
        {
            get { return _facade.StagingRoot; }
        }

        public string StateRoot
        {
            get { return _facade.StateRoot; }
        }

        public string ActiveStatePath
        {
            get { return _facade.ActiveStatePath; }
        }

        public bool Ready
        {
            get { return _facade.Ready; }
        }

        public string InitError
        {
            get { return _facade.InitError; }
        }

        public AssetCache Cache
        {
            get { return _facade.Cache; }
        }

        public BundleRuntime Bundles
        {
            get { return _facade.Bundles; }
        }

        public SafePointGate SafePoint
        {
            get { return _facade.SafePoint; }
        }

        public MemoryPressureLevel Pressure
        {
            get { return _facade.Pressure; }
        }

        public int LastReclaimedAssets
        {
            get { return _facade.LastReclaimedAssets; }
        }

        public int LastReclaimedBundles
        {
            get { return _facade.LastReclaimedBundles; }
        }

        public IReadOnlyList<string> Logs
        {
            get { return _logs; }
        }

        /// <summary>Live download progress: (transferred, total) in bytes.</summary>
        public event Action<long, long> DownloadProgress;

        public string ActiveVersion
        {
            get { return _facade.ActiveVersion; }
        }

        /// <summary>
        /// How many bundle payload loads the gate may start per frame. The
        /// dashboard exposes it so the budget is observable, not theoretical.
        /// </summary>
        public int StartsPerFrame
        {
            get { return _facade.StartsPerFrame; }
            set { _facade.StartsPerFrame = value; }
        }

        public int GatePendingCount
        {
            get { return _facade.GatePendingCount; }
        }

        public int ScopeCount
        {
            get { return _facade.ScopeCount; }
        }

        // ------------------------------------------------------------------
        // Boot
        // ------------------------------------------------------------------

        public Task<bool> InitializeAsync()
        {
            return _facade.InitializeAsync();
        }

        public void RebuildRuntime(string reason)
        {
            _facade.RebuildRuntime(reason);
        }

        // ------------------------------------------------------------------
        // Per-frame work
        // ------------------------------------------------------------------

        public void Tick()
        {
            _facade.Tick();
        }

        // ------------------------------------------------------------------
        // Assets
        // ------------------------------------------------------------------

        public AssetLoadTask<AssetInfo> Load(string key, string scopeName)
        {
            return _facade.LoadAsync<AssetInfo>(key, scopeName);
        }

        public int ReleaseAllScopes()
        {
            return _facade.ReleaseAllScopes();
        }

        // ------------------------------------------------------------------
        // Patch
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a simulated CDN over the build output folder and hands it to
        /// the framework's download-and-stage path.
        ///
        /// The interruption budget is demo-only: it exists so the resume path
        /// can be exercised without a flaky network.
        /// </summary>
        public async Task<PatchDownloadReport> DownloadAsync(
            string version,
            int interruptAtPercent,
            bool supportsRange,
            CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(
                _serverRoot,
                version,
                FullFeatureLayout.ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                return new PatchDownloadReport
                {
                    Version = version,
                    Status = PatchApplyStatus.Failed,
                    Message = "服务端缺少清单：" + manifestPath
                };
            }

            var manifest = FullFeatureLayout.LoadManifest(manifestPath);
            var server = new SimulatedCdnServer();
            var sizes = new List<long>();
            var totalBytes = 0L;

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var bundle = manifest.Bundles[i];
                var path = Path.Combine(_serverRoot, version, bundle.FileName);

                if (!File.Exists(path))
                {
                    return new PatchDownloadReport
                    {
                        Version = version,
                        Status = PatchApplyStatus.Failed,
                        Message = "服务端缺少 Bundle：" + path
                    };
                }

                // The simulated CDN is keyed by URL; the file name is the key.
                bundle.Url = bundle.FileName;
                server.AddFile(bundle.FileName, File.ReadAllBytes(path));
                sizes.Add(bundle.SizeBytes);
                totalBytes += bundle.SizeBytes;
            }

            server.SupportsRange = supportsRange;

            var transport = new SimulatedPatchTransport(server);

            if (interruptAtPercent > 0 && interruptAtPercent < 100)
            {
                transport.TransferBudget =
                    DownloadBudget.Compute(sizes, interruptAtPercent);
            }

            transport.Progress = transferred =>
            {
                var handler = DownloadProgress;

                if (handler != null)
                {
                    handler(transferred, totalBytes);
                }
            };

            return await _facade.DownloadAndStageAsync(
                manifest,
                manifestPath,
                transport,
                cancellationToken);
        }

        public Task<PatchActivationResult> ActivateAsync(
            string version,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _facade.ActivateAsync(version, cancellationToken);
        }

        // ------------------------------------------------------------------
        // Memory pressure
        // ------------------------------------------------------------------

        public void SetMemoryPressure(MemoryPressureLevel level)
        {
            _facade.SetMemoryPressure(level);
        }

        // ------------------------------------------------------------------
        // Diagnostics
        // ------------------------------------------------------------------

        public string DescribeStaging(string version)
        {
            return _facade.DescribeStaging(version);
        }

        public string DescribeBundles()
        {
            return _facade.DescribeBundles();
        }

        public string DescribeAssets()
        {
            return _facade.DescribeAssets();
        }

        public List<BundleSnapshotEntry> SnapshotBundles()
        {
            return _facade.SnapshotBundles();
        }

        public List<string> SnapshotIdleAssetKeys()
        {
            return _facade.SnapshotIdleAssetKeys();
        }

        public void AppendLog(string message)
        {
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            _logs.Add(line);

            while (_logs.Count > MaxLogLines)
            {
                _logs.RemoveAt(0);
            }

            Debug.Log("[FullFeature] " + message);
        }
    }

    /// <summary>
    /// Turns "interrupt at N percent of the whole download" into a byte budget
    /// for <see cref="SimulatedPatchTransport"/>. The stop point is nudged off
    /// a bundle boundary so a partial .part always exists and the resume path
    /// is what gets exercised.
    /// </summary>
    internal static class DownloadBudget
    {
        public static long Compute(
            IList<long> bundleSizes,
            int interruptAtPercent)
        {
            if (interruptAtPercent <= 0 || interruptAtPercent >= 100)
            {
                return -1;
            }

            var total = 0L;

            for (var i = 0; i < bundleSizes.Count; i++)
            {
                total += bundleSizes[i];
            }

            var budget = total * interruptAtPercent / 100;
            var cumulative = 0L;

            for (var i = 0; i < bundleSizes.Count; i++)
            {
                cumulative += bundleSizes[i];

                if (budget == cumulative && budget > 1)
                {
                    budget--;
                }
            }

            return Math.Max(0, budget);
        }
    }
}
