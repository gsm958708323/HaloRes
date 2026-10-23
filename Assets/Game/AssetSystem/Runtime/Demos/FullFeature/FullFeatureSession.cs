using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Content;
using Game.AssetSystem.Core;
using Game.AssetSystem.Diagnostics;
using Game.AssetSystem.Memory;
using Game.AssetSystem.Patch;
using Game.AssetSystem.Release;
using Game.AssetSystem.Simulation;
using Game.AssetSystem.Storage;
using UnityEngine;

namespace Game.AssetSystem.Demos.FullFeature
{
    public sealed class FullFeatureDownloadReport
    {
        public string Version;
        public PatchApplyStatus Status;
        public string Message;
        public int Committed;
        public int Skipped;
        public long ResumeBytes;
        public long TransferredBytes;
        public long TotalBytes;

        public bool Success
        {
            get { return Status == PatchApplyStatus.Succeeded; }
        }

        public override string ToString()
        {
            return "status=" + Status +
                   " committed=" + Committed +
                   " skipped=" + Skipped +
                   " resume=" + ResumeBytes +
                   " transferred=" + TransferredBytes + "/" + TotalBytes;
        }
    }

    /// <summary>
    /// The whole client side of the demo in one place: install / download /
    /// activate / load / release / reclaim.
    ///
    /// Everything that mutates state runs on the Unity main thread; the only
    /// background work is the simulated download, which the patch service
    /// already marshals back. The dashboard and the PlayMode scenario both
    /// drive this same object, so what the test proves is what the UI does.
    /// </summary>
    public sealed class FullFeatureSession
    {
        private const int ActiveStateSchemaVersion = 1;
        private const int MaxLogLines = 40;

        private readonly string _clientRoot;
        private readonly string _serverRoot;

        private readonly PatchActivationService _activation =
            new PatchActivationService();

        private readonly SafePointGate _safePoint = new SafePointGate();
        private readonly List<AssetScope> _scopes = new List<AssetScope>();
        private readonly List<string> _logs = new List<string>();

        private AssetLoadGate _gate;
        private AssetMemoryPolicy _policy;
        private AssetRuntimeDiagnostics _diagnostics;
        private BundleRuntime _bundles;
        private AssetCache _cache;
        private AssetMemoryEvaluator _memory;

        public FullFeatureSession(string clientRoot = null, string serverRoot = null)
        {
            _clientRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(clientRoot)
                    ? FullFeatureLayout.DefaultClientRoot
                    : clientRoot);

            _serverRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(serverRoot)
                    ? FullFeatureLayout.BuildRoot
                    : serverRoot);
        }

        public string ClientRoot
        {
            get { return _clientRoot; }
        }

        public string ServerRoot
        {
            get { return _serverRoot; }
        }

        public string ContentRoot
        {
            get { return Path.Combine(_clientRoot, "content"); }
        }

        public string StagingRoot
        {
            get { return Path.Combine(_clientRoot, "staging"); }
        }

        public string StateRoot
        {
            get { return Path.Combine(_clientRoot, "state"); }
        }

        public string ActiveStatePath
        {
            get { return Path.Combine(StateRoot, "active.json"); }
        }

        public bool Ready { get; private set; }

        public string InitError { get; private set; }

        public AssetCache Cache
        {
            get { return _cache; }
        }

        public BundleRuntime Bundles
        {
            get { return _bundles; }
        }

        public SafePointGate SafePoint
        {
            get { return _safePoint; }
        }

        public MemoryPressureLevel Pressure { get; private set; }

        public int LastReclaimedAssets { get; private set; }

        public int LastReclaimedBundles { get; private set; }

        public IReadOnlyList<string> Logs
        {
            get { return _logs; }
        }

        /// <summary>Live download progress: (transferred, total) in bytes.</summary>
        public event Action<long, long> DownloadProgress;

        public string ActiveVersion
        {
            get
            {
                return _activation.ActiveContext == null
                    ? "<none>"
                    : _activation.ActiveContext.ContentVersion;
            }
        }

        /// <summary>
        /// How many bundle payload loads the gate may start per frame. The
        /// dashboard exposes it so the budget is observable, not theoretical.
        /// </summary>
        public int StartsPerFrame
        {
            get { return _gate == null ? 0 : _gate.MaxStartsPerFrame; }
            set
            {
                if (_gate != null)
                {
                    _gate.MaxStartsPerFrame = Math.Max(1, value);
                }
            }
        }

        public int GatePendingCount
        {
            get { return _gate == null ? 0 : _gate.PendingCount; }
        }

        // ------------------------------------------------------------------
        // Boot
        // ------------------------------------------------------------------

        /// <summary>
        /// Restores the persisted active version, or installs the base version
        /// from the server folder on first run. Returns false with
        /// <see cref="InitError"/> set when content was never built.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            MainThreadPump.EnsureMainThread(nameof(InitializeAsync));

            Directory.CreateDirectory(ContentRoot);
            Directory.CreateDirectory(StagingRoot);
            Directory.CreateDirectory(StateRoot);

            PatchManifest manifest;
            string bundleRoot;

            if (File.Exists(ActiveStatePath))
            {
                var state = JsonUtility.FromJson<FullFeatureActiveState>(
                    File.ReadAllText(ActiveStatePath, Encoding.UTF8));

                if (state == null || state.manifest == null ||
                    string.IsNullOrEmpty(state.bundleRoot))
                {
                    InitError = "本地 active 状态无法解析：" + ActiveStatePath;
                    return false;
                }

                if (!Directory.Exists(state.bundleRoot))
                {
                    // Content folder was removed by hand: treat it as a first
                    // run instead of failing to activate a missing folder.
                    string repairError;

                    if (!TryInstallBaseVersion(
                        out manifest,
                        out bundleRoot,
                        out repairError))
                    {
                        InitError = repairError;
                        return false;
                    }

                    AppendLog("本地内容目录丢失，重新安装基础版本 " +
                              manifest.ContentVersion);
                }
                else
                {
                    manifest = state.manifest;
                    bundleRoot = state.bundleRoot;
                    AppendLog("恢复本地状态：" + state.contentVersion);
                }
            }
            else
            {
                string installError;

                if (!TryInstallBaseVersion(
                    out manifest,
                    out bundleRoot,
                    out installError))
                {
                    InitError = installError;
                    return false;
                }

                AppendLog("首启安装基础版本 " + manifest.ContentVersion);
            }

            var result = await _activation.ActivateAsync(
                manifest,
                bundleRoot,
                FullFeatureLayout.PlatformKey,
                0,
                true);

            if (result.Status != PatchActivationStatus.Succeeded)
            {
                InitError = "激活失败：" + result.Status + " " + result.Message;
                return false;
            }

            PersistActiveState(manifest, bundleRoot);
            RebuildRuntime("冷启动");

            Ready = true;
            return true;
        }

        private bool TryInstallBaseVersion(
            out PatchManifest manifest,
            out string bundleRoot,
            out string error)
        {
            manifest = null;
            bundleRoot = null;
            error = null;

            var version = FullFeatureLayout.BaseVersion;
            var manifestPath = Path.Combine(
                _serverRoot,
                version,
                FullFeatureLayout.ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                error = "缺少服务端内容：" + manifestPath +
                        "\n请先执行 Tools/Game Asset System/" +
                        "Build Full-Feature Demo Content";
                return false;
            }

            manifest = FullFeatureLayout.LoadManifest(manifestPath);

            var target = Path.Combine(ContentRoot, version);
            Directory.CreateDirectory(target);

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var bundle = manifest.Bundles[i];

                if (!PathSafety.TryResolveChild(
                    target,
                    bundle.FileName,
                    out var destination,
                    out var pathError))
                {
                    error = "非法 Bundle 文件名 '" + bundle.FileName + "'：" +
                            pathError;
                    return false;
                }

                var source = Path.Combine(_serverRoot, version, bundle.FileName);

                if (!File.Exists(source))
                {
                    error = "服务端缺少 Bundle：" + source;
                    return false;
                }

                File.Copy(source, destination, true);

                if (!BundleHashVerifier.VerifySha256Hex(
                    destination,
                    bundle.Sha256Hex,
                    out _,
                    out var hashError))
                {
                    error = "安装校验失败 " + bundle.FileName + "：" + hashError;
                    return false;
                }
            }

            File.Copy(
                manifestPath,
                Path.Combine(target, FullFeatureLayout.ManifestFileName),
                true);

            bundleRoot = target;
            return true;
        }

        private void PersistActiveState(PatchManifest manifest, string bundleRoot)
        {
            var state = new FullFeatureActiveState
            {
                schemaVersion = ActiveStateSchemaVersion,
                contentVersion = manifest.ContentVersion,
                bundleRoot = bundleRoot,
                manifest = manifest
            };

            var json = JsonUtility.ToJson(state, true);
            var tempPath = ActiveStatePath + ".tmp";

            Directory.CreateDirectory(StateRoot);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            if (File.Exists(ActiveStatePath))
            {
                File.Delete(ActiveStatePath);
            }

            File.Move(tempPath, ActiveStatePath);
        }

        /// <summary>
        /// Drops the old runtime and builds a new one over the active context.
        /// This is the "激活 = 重建运行时" step: no cache entry, lease or handle
        /// survives a version switch.
        /// </summary>
        public void RebuildRuntime(string reason)
        {
            MainThreadPump.EnsureMainThread(nameof(RebuildRuntime));

            for (var i = 0; i < _scopes.Count; i++)
            {
                _scopes[i].Dispose();
            }

            _scopes.Clear();

            // The old runtime is dropped whole: its idle assets still hold
            // bundle leases, so drain it first instead of reaching for
            // AssetBundle.UnloadAllAssetBundles (which would also unload
            // bundles this runtime never touched).
            DrainRuntime(_cache, _bundles);

            var startsPerFrame = StartsPerFrame > 0 ? StartsPerFrame : 2;

            _gate = new AssetLoadGate
            {
                MaxStartsPerFrame = startsPerFrame,
                MaxConcurrent = 4
            };

            // One policy and one diagnostics sink shared by the bundle runtime
            // and the asset cache, so both halves report into the same view.
            _policy = new AssetMemoryPolicy();
            _diagnostics = new AssetRuntimeDiagnostics();

            _bundles = new BundleRuntime(
                _activation.ActiveContext.CreateBundleCatalog(),
                new GatedBundleLoader(new RealBundleLoader(), _gate),
                _policy,
                _diagnostics);

            _cache = new AssetCache(
                _activation.ActiveContext.CreateAssetCatalog(),
                _bundles,
                _policy,
                _diagnostics);

            _memory = new AssetMemoryEvaluator(_cache, _bundles)
            {
                MaxUnloadsPerCall = BudgetFor(Pressure)
            };

            AppendLog("重建运行时（" + reason + "）→ " + ActiveVersion);
        }

        /// <summary>
        /// Unloads every idle asset and bundle of a retired runtime. Bundle
        /// payloads release their dependency leases on the way out, so a second
        /// pass is needed before shared bundles become idle themselves.
        /// </summary>
        private static void DrainRuntime(
            AssetCache cache,
            BundleRuntime bundles)
        {
            if (cache == null || bundles == null)
            {
                return;
            }

            var evaluator = new AssetMemoryEvaluator(cache, bundles)
            {
                MaxUnloadsPerCall = 1000
            };

            for (var i = 0; i < 32; i++)
            {
                var report = evaluator.Evaluate();

                if (report.AssetsUnloaded == 0 && report.BundlesUnloaded == 0)
                {
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Per-frame work
        // ------------------------------------------------------------------

        public void Tick()
        {
            if (_gate != null)
            {
                _gate.Tick();
            }

            if (Pressure == MemoryPressureLevel.Normal || _memory == null)
            {
                return;
            }

            var report = _memory.Evaluate();

            LastReclaimedAssets += report.AssetsUnloaded;
            LastReclaimedBundles += report.BundlesUnloaded;

            if (report.AssetsUnloaded > 0 || report.BundlesUnloaded > 0)
            {
                AppendLog("内存压力 " + Pressure + "：卸载 " +
                          report.AssetsUnloaded + " 资源 / " +
                          report.BundlesUnloaded + " Bundle");
            }

            if (report.AssetsUnloaded == 0 && report.BundlesUnloaded == 0)
            {
                Pressure = MemoryPressureLevel.Normal;
                AppendLog("内存压力回落 Normal：已无可回收 Idle");
            }
        }

        // ------------------------------------------------------------------
        // Assets
        // ------------------------------------------------------------------

        public AssetLoadTask<AssetInfo> Load(string key, string scopeName)
        {
            if (!Ready || _cache == null)
            {
                throw new InvalidOperationException(
                    "Session is not ready. InitializeAsync() first.");
            }

            var scope = _cache.CreateScope(scopeName);
            _scopes.Add(scope);

            return _cache.AcquireAsync<AssetInfo>(
                new AssetKey(key),
                scope,
                scopeName);
        }

        public int ReleaseAllScopes()
        {
            for (var i = 0; i < _scopes.Count; i++)
            {
                _scopes[i].Dispose();
            }

            _scopes.Clear();

            return _cache == null ? 0 : _cache.TotalLiveHandles;
        }

        public int ScopeCount
        {
            get { return _scopes.Count; }
        }

        // ------------------------------------------------------------------
        // Patch
        // ------------------------------------------------------------------

        public async Task<FullFeatureDownloadReport> DownloadAsync(
            string version,
            int interruptAtPercent,
            bool supportsRange,
            CancellationToken cancellationToken)
        {
            var report = new FullFeatureDownloadReport { Version = version };

            var manifestPath = Path.Combine(
                _serverRoot,
                version,
                FullFeatureLayout.ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                report.Status = PatchApplyStatus.Failed;
                report.Message = "服务端缺少清单：" + manifestPath;
                return report;
            }

            var manifest = FullFeatureLayout.LoadManifest(manifestPath);
            var server = new SimulatedCdnServer();
            var sizes = new List<long>();

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var bundle = manifest.Bundles[i];
                var path = Path.Combine(_serverRoot, version, bundle.FileName);

                if (!File.Exists(path))
                {
                    report.Status = PatchApplyStatus.Failed;
                    report.Message = "服务端缺少 Bundle：" + path;
                    return report;
                }

                // The simulated CDN is keyed by URL; the file name is the key.
                bundle.Url = bundle.FileName;
                server.AddFile(bundle.FileName, File.ReadAllBytes(path));
                sizes.Add(bundle.SizeBytes);
                report.TotalBytes += bundle.SizeBytes;
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
                report.TransferredBytes = transferred;

                var handler = DownloadProgress;

                if (handler != null)
                {
                    handler(transferred, report.TotalBytes);
                }
            };

            // AssetFileStore appends /content itself, so hand it the client
            // root, not ContentRoot.
            var fileStore = new AssetFileStore(_clientRoot);
            fileStore.EnsureDirectories();

            var result = await new PatchApplyController(
                    fileStore,
                    new PatchStagingStore(StagingRoot),
                    transport,
                    1,
                    false)
                .ApplyAsync(manifest, null, cancellationToken);

            report.Status = result.Status;
            report.Message = result.Message;
            report.Committed = result.BundlesCommitted;
            report.Skipped = result.BundlesSkipped;
            report.ResumeBytes = result.ResumeBytesTotal;
            report.TransferredBytes = transport.Transferred;

            if (result.Status == PatchApplyStatus.Succeeded)
            {
                var target = Path.Combine(ContentRoot, version);
                Directory.CreateDirectory(target);

                File.Copy(
                    manifestPath,
                    Path.Combine(target, FullFeatureLayout.ManifestFileName),
                    true);

                AppendLog("下载完成 " + version +
                          "（提交 " + report.Committed +
                          "，跳过 " + report.Skipped +
                          "，续传 " + report.ResumeBytes + " 字节）");
            }
            else
            {
                AppendLog("下载未完成 " + version + "：" + report.Message +
                          "（本次传输 " + report.TransferredBytes + " 字节）");
            }

            return report;
        }

        public async Task<PatchActivationResult> ActivateAsync(
            string version,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var manifestPath = Path.Combine(
                ContentRoot,
                version,
                FullFeatureLayout.ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                var missing = new PatchActivationResult
                {
                    Status = PatchActivationStatus.BundleMissing,
                    Message = "本地没有 " + version + " 内容，请先下载。",
                    ActiveVersionBefore = ActiveVersion,
                    ActiveVersionAfter = ActiveVersion
                };

                AppendLog("激活被拒 " + version + "：" + missing.Message);
                return missing;
            }

            var manifest = FullFeatureLayout.LoadManifest(manifestPath);
            var bundleRoot = Path.Combine(ContentRoot, version);

            var result = await _activation.ActivateAsync(
                manifest,
                bundleRoot,
                FullFeatureLayout.PlatformKey,
                InFlightCount(),
                _safePoint.IsSafePoint,
                cancellationToken,
                InFlightCount,
                () => _safePoint.IsSafePoint);

            if (result.Status == PatchActivationStatus.Succeeded)
            {
                PersistActiveState(manifest, bundleRoot);
                RebuildRuntime("激活 " + version);
                AppendLog("激活成功 " + version + "：" + result.Message);
            }
            else if (result.Status == PatchActivationStatus.AlreadyActive)
            {
                AppendLog("已是当前版本 " + version + "：" + result.Message);
            }
            else
            {
                AppendLog("激活被拒 " + version + "：" + result.Status +
                          " " + result.Message);
            }

            return result;
        }

        /// <summary>
        /// Live handles plus loads still in flight. An in-flight load will hand
        /// out a handle on the old context, so it blocks a switch exactly like a
        /// live handle does.
        /// </summary>
        private int InFlightCount()
        {
            if (_cache == null)
            {
                return 0;
            }

            return _cache.TotalLiveHandles + _cache.LoadingAssetCount;
        }

        // ------------------------------------------------------------------
        // Memory pressure
        // ------------------------------------------------------------------

        public void SetMemoryPressure(MemoryPressureLevel level)
        {
            Pressure = level;

            if (_memory != null)
            {
                _memory.MaxUnloadsPerCall = BudgetFor(level);
            }

            if (level == MemoryPressureLevel.Normal)
            {
                AppendLog("内存压力 Normal：停止回收");
                return;
            }

            AppendLog("内存压力 " + level + "：每帧预算 " +
                      BudgetFor(level) + " 次卸载");
        }

        private static int BudgetFor(MemoryPressureLevel level)
        {
            switch (level)
            {
                case MemoryPressureLevel.Medium:
                    return 2;
                case MemoryPressureLevel.High:
                    return 6;
                case MemoryPressureLevel.Critical:
                    return 64;
                default:
                    return 0;
            }
        }

        // ------------------------------------------------------------------
        // Diagnostics
        // ------------------------------------------------------------------

        public string DescribeStaging(string version)
        {
            var directory = Path.Combine(StagingRoot, version);

            if (!Directory.Exists(directory))
            {
                return "无暂存";
            }

            var bytes = 0L;
            var parts = 0;
            var metas = 0;

            foreach (var file in Directory.GetFiles(directory))
            {
                var extension = Path.GetExtension(file);

                if (string.Equals(extension, ".part", StringComparison.Ordinal))
                {
                    parts++;
                    bytes += new FileInfo(file).Length;
                }
                else if (string.Equals(extension, ".meta", StringComparison.Ordinal))
                {
                    metas++;
                }
            }

            if (parts == 0 && metas == 0)
            {
                return "无暂存";
            }

            return parts + " 个 .part（" + bytes + " 字节）· " +
                   metas + " 个 .meta";
        }

        public string DescribeBundles()
        {
            if (_bundles == null)
            {
                return "-";
            }

            return "R" + _bundles.ReadyBundleCount +
                   "/I" + _bundles.IdleBundleCount +
                   "/L" + _bundles.LoadedPayloadCount +
                   "/Ref" + _bundles.TotalUserRefs;
        }

        public string DescribeAssets()
        {
            if (_cache == null)
            {
                return "-";
            }

            return "R" + _cache.ReadyAssetCount +
                   "/I" + _cache.IdleAssetCount +
                   "/L" + _cache.LoadingAssetCount +
                   "/H" + _cache.TotalLiveHandles;
        }

        public List<BundleSnapshotEntry> SnapshotBundles()
        {
            return _bundles == null
                ? new List<BundleSnapshotEntry>()
                : _bundles.CaptureSnapshot().Entries;
        }

        public List<string> SnapshotIdleAssetKeys()
        {
            var list = new List<string>();

            if (_cache == null)
            {
                return list;
            }

            var keys = _cache.SnapshotIdleAssetKeys();

            for (var i = 0; i < keys.Count; i++)
            {
                list.Add(keys[i].Value);
            }

            list.Sort(StringComparer.Ordinal);
            return list;
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
