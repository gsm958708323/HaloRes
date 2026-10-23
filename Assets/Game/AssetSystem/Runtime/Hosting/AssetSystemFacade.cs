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
using Game.AssetSystem.Storage;
using UnityEngine;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// The framework's single entry point for game code.
    ///
    /// Before this existed the only complete client-side flow lived inside the
    /// full-feature demo: install / download / activate / rebuild / load /
    /// release / reclaim, wired together by hand. Any other system that wanted
    /// hot-updatable content had to copy that orchestration, which is how two
    /// subtly different activation paths appear.
    ///
    /// Everything that mutates state runs on the Unity main thread. The only
    /// background work is the transport, which the patch pipeline already
    /// marshals back.
    /// </summary>
    public sealed class AssetSystemFacade : IAssetSystemReadOnly
    {
        private readonly AssetSystemOptions _options;

        private readonly PatchActivationService _activation =
            new PatchActivationService();

        private readonly SafePointGate _safePoint = new SafePointGate();

        private readonly List<AssetScope> _scopes = new List<AssetScope>();

        private AssetLoadGate _gate;
        private AssetMemoryPolicy _policy;
        private AssetRuntimeDiagnostics _diagnostics;
        private BundleRuntime _bundles;
        private AssetCache _cache;
        private AssetMemoryEvaluator _memory;

        public AssetSystemFacade(AssetSystemOptions options)
        {
            _options = options ??
                       throw new ArgumentNullException(nameof(options));

            _options.Validate();
        }

        // ------------------------------------------------------------------
        // Identity / layout
        // ------------------------------------------------------------------

        public AssetSystemOptions Options
        {
            get { return _options; }
        }

        public string ClientRoot
        {
            get { return _options.ClientRoot; }
        }

        public string ContentRoot
        {
            get { return _options.ContentRoot; }
        }

        public string StagingRoot
        {
            get { return _options.StagingRoot; }
        }

        public string StateRoot
        {
            get { return _options.StateRoot; }
        }

        public string ActiveStatePath
        {
            get { return _options.ActiveStatePath; }
        }

        public string PlatformKey
        {
            get { return _options.PlatformKey; }
        }

        /// <summary>
        /// Raised for every framework-level step. The facade never writes to
        /// the console itself, so a caller decides where the narrative goes.
        /// </summary>
        public event Action<string> Log;

        // ------------------------------------------------------------------
        // Read-only state
        // ------------------------------------------------------------------

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

        public AssetLoadGate Gate
        {
            get { return _gate; }
        }

        public AssetRuntimeDiagnostics Diagnostics
        {
            get { return _diagnostics; }
        }

        public MemoryPressureLevel Pressure { get; private set; }

        public int LastReclaimedAssets { get; private set; }

        public int LastReclaimedBundles { get; private set; }

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
        /// How many bundle payload loads the gate may start per frame. Kept
        /// writable so a loading screen can trade frame time for latency.
        /// </summary>
        public int StartsPerFrame
        {
            get
            {
                return _gate == null
                    ? _options.MaxStartsPerFrame
                    : _gate.MaxStartsPerFrame;
            }
            set
            {
                _options.MaxStartsPerFrame = Math.Max(1, value);

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

        public int ScopeCount
        {
            get { return _scopes.Count; }
        }

        // ------------------------------------------------------------------
        // Boot
        // ------------------------------------------------------------------

        /// <summary>
        /// Restores the persisted active version, or installs the base version
        /// from <see cref="AssetSystemOptions.BaseContentRoot"/> on first run.
        /// Returns false with <see cref="InitError"/> set when no usable content
        /// could be activated.
        /// </summary>
        public async Task<bool> InitializeAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            MainThreadPump.EnsureMainThread(nameof(InitializeAsync));

            if (Ready)
            {
                return true;
            }

            Directory.CreateDirectory(ContentRoot);
            Directory.CreateDirectory(StagingRoot);
            Directory.CreateDirectory(StateRoot);

            PatchManifest manifest;
            string bundleRoot;

            if (File.Exists(ActiveStatePath))
            {
                var state = TryReadActiveState();

                if (state == null)
                {
                    InitError = "本地 active 状态无法解析：" + ActiveStatePath;
                    return false;
                }

                if (!Directory.Exists(state.bundleRoot))
                {
                    // The content folder was removed by hand: treat it as a
                    // first run instead of activating a missing folder.
                    if (!TryInstallBaseVersion(
                        out manifest,
                        out bundleRoot,
                        out var repairError))
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
                if (!TryInstallBaseVersion(
                    out manifest,
                    out bundleRoot,
                    out var installError))
                {
                    InitError = installError;
                    return false;
                }

                AppendLog("首启安装基础版本 " + manifest.ContentVersion);
            }

            var result = await _activation.ActivateAsync(
                manifest,
                bundleRoot,
                PlatformKey,
                0,
                true,
                cancellationToken);

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

        private ActiveContentState TryReadActiveState()
        {
            try
            {
                var state = JsonUtility.FromJson<ActiveContentState>(
                    File.ReadAllText(ActiveStatePath, Encoding.UTF8));

                if (state == null ||
                    state.manifest == null ||
                    string.IsNullOrEmpty(state.bundleRoot))
                {
                    return null;
                }

                return state;
            }
            catch (Exception exception)
            {
                AppendLog("读取 active 状态失败：" + exception.Message);
                return null;
            }
        }

        private bool TryInstallBaseVersion(
            out PatchManifest manifest,
            out string bundleRoot,
            out string error)
        {
            manifest = null;
            bundleRoot = null;
            error = null;

            if (string.IsNullOrWhiteSpace(_options.BaseContentRoot))
            {
                error = "客户端没有可恢复的 active 状态，且未配置 " +
                        "AssetSystemOptions.BaseContentRoot。";
                return false;
            }

            var version = _options.BaseVersion;
            var manifestPath = Path.Combine(
                _options.BaseContentRoot,
                version,
                _options.ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                error = "缺少服务端内容：" + manifestPath;
                return false;
            }

            manifest = BuildManifestReader.Read(manifestPath);

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

                var source = Path.Combine(
                    _options.BaseContentRoot,
                    version,
                    bundle.FileName);

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
                Path.Combine(target, _options.ManifestFileName),
                true);

            bundleRoot = target;
            return true;
        }

        private void PersistActiveState(PatchManifest manifest, string bundleRoot)
        {
            var state = new ActiveContentState
            {
                schemaVersion = _options.ActiveStateSchemaVersion,
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

            var startsPerFrame = _options.MaxStartsPerFrame > 0
                ? _options.MaxStartsPerFrame
                : 2;

            _gate = new AssetLoadGate
            {
                MaxStartsPerFrame = startsPerFrame,
                MaxConcurrent = Math.Max(1, _options.MaxConcurrentBundleLoads)
            };

            // One policy and one diagnostics sink shared by the bundle runtime
            // and the asset cache, so both halves report into the same view.
            _policy = new AssetMemoryPolicy();
            _diagnostics = new AssetRuntimeDiagnostics();

            IBundleLoader loader = new RealBundleLoader();

            if (_options.UseLoadGate)
            {
                loader = new GatedBundleLoader(loader, _gate);
            }

            _bundles = new BundleRuntime(
                _activation.ActiveContext.CreateBundleCatalog(),
                loader,
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

        /// <summary>
        /// Call once per frame from <c>Update</c>. Starts queued bundle loads
        /// and, when a memory pressure level is set, reclaims idle entries
        /// within that level's budget.
        /// </summary>
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

        /// <summary>
        /// Creates a scope tracked by the facade. Disposing it releases every
        /// handle it handed out; <see cref="ReleaseAllScopes"/> does the same
        /// for all of them at once.
        /// </summary>
        public AssetScope CreateScope(string name)
        {
            EnsureReady(nameof(CreateScope));

            var scope = _cache.CreateScope(name);
            _scopes.Add(scope);

            return scope;
        }

        public AssetLoadTask<T> LoadAsync<T>(
            string key,
            AssetScope scope,
            string owner = null)
            where T : class
        {
            EnsureReady(nameof(LoadAsync));

            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            return _cache.AcquireAsync<T>(
                new AssetKey(key),
                scope,
                string.IsNullOrEmpty(owner) ? scope.Name : owner);
        }

        /// <summary>
        /// Convenience overload for the common case: one key, one scope that
        /// lives until <see cref="ReleaseAllScopes"/>.
        /// </summary>
        public AssetLoadTask<T> LoadAsync<T>(string key, string scopeName)
            where T : class
        {
            var scope = CreateScope(scopeName);

            return LoadAsync<T>(key, scope, scopeName);
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

        // ------------------------------------------------------------------
        // Patch
        // ------------------------------------------------------------------

        /// <summary>
        /// Downloads one version into staging, commits it into the content
        /// folder and copies its manifest next to the bundles. Activation is a
        /// separate decision — see <see cref="ActivateAsync"/>.
        /// </summary>
        public async Task<PatchDownloadReport> DownloadAndStageAsync(
            PatchManifest manifest,
            string manifestSourcePath,
            IPatchTransport transport,
            CancellationToken cancellationToken)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            var version = manifest.ContentVersion;
            var report = new PatchDownloadReport
            {
                Version = version,
                TotalBytes = TotalBytes(manifest)
            };

            // AssetFileStore appends /content itself, so hand it the client
            // root, not ContentRoot.
            var fileStore = new AssetFileStore(_options.ClientRoot);
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

            // The transport owns "bytes moved" and every transport reports it
            // per bundle; summing the per-bundle reports keeps the facade free
            // of any transport-specific counter.
            for (var i = 0; i < result.Bundles.Count; i++)
            {
                report.TransferredBytes += result.Bundles[i].BytesTransferredThisRun;
            }

            if (result.Status == PatchApplyStatus.Succeeded)
            {
                if (!string.IsNullOrEmpty(manifestSourcePath) &&
                    File.Exists(manifestSourcePath))
                {
                    var target = Path.Combine(ContentRoot, version);
                    Directory.CreateDirectory(target);

                    File.Copy(
                        manifestSourcePath,
                        Path.Combine(target, _options.ManifestFileName),
                        true);
                }

                AppendLog("下载完成 " + version +
                          "（提交 " + report.Committed +
                          "，跳过 " + report.Skipped +
                          "，续传 " + report.ResumeBytes + " 字节）");
            }
            else
            {
                AppendLog("下载未完成 " + version + "：" + report.Message);
            }

            return report;
        }

        private static long TotalBytes(PatchManifest manifest)
        {
            if (manifest.Bundles == null)
            {
                return 0;
            }

            var total = 0L;

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                total += manifest.Bundles[i].SizeBytes;
            }

            return total;
        }

        /// <summary>
        /// Activates a version already present in the content folder. Rejected
        /// while handles are live or outside a safe point — both checks are
        /// re-read inside the swap, so a handle handed out between the precheck
        /// and the swap still blocks it.
        /// </summary>
        public async Task<PatchActivationResult> ActivateAsync(
            string version,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var manifestPath = Path.Combine(
                ContentRoot,
                version,
                _options.ManifestFileName);

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

            var manifest = BuildManifestReader.Read(manifestPath);
            var bundleRoot = Path.Combine(ContentRoot, version);

            var result = await _activation.ActivateAsync(
                manifest,
                bundleRoot,
                PlatformKey,
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

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void EnsureReady(string caller)
        {
            if (!Ready || _cache == null)
            {
                throw new InvalidOperationException(
                    caller + ": asset system is not ready. " +
                    "InitializeAsync() first.");
            }
        }

        private void AppendLog(string message)
        {
            var handler = Log;

            if (handler != null)
            {
                handler(message);
            }
        }
    }
}
