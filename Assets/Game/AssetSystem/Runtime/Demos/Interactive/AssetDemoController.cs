using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Game.AssetSystem.FullChain;
using Game.AssetSystem.Integration;
using Game.AssetSystem.Release;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.AssetSystem.Demos.Interactive
{
    /// <summary>
    /// Interactive, self-contained demo of the asset framework.
    /// Everything (canvas, viewport camera, cubes) is created at runtime, so it
    /// runs in any scene. Content comes from the real bundles produced by
    /// Tools/Game Asset System/Build Full-Chain Demo Content.
    /// </summary>
    public sealed class AssetDemoController : MonoBehaviour
    {
        private const string OwnerUi = "UI";
        private const string OwnerBattle = "Battle";
        private const int ViewportLayer = 31;

        private sealed class AssetRow
        {
            public string Key;
            public AssetHandle<DemoGraphNode> UiHandle;
            public AssetHandle<DemoGraphNode> BattleHandle;
            public int UiLoading;
            public int BattleLoading;

            public Text KeyText;
            public Text StateText;
            public Text OwnerText;
            public Image RefBar;
            public Text UiCaption;
            public Text BattleCaption;
            public Image UiButtonImage;
            public Image BattleButtonImage;

            public GameObject Cube;
            public Material CubeMaterial;
            public Color CubeColor;

            public AssetHandle<DemoGraphNode> HandleOf(string owner)
            {
                return owner == OwnerUi ? UiHandle : BattleHandle;
            }

            public void SetHandle(string owner, AssetHandle<DemoGraphNode> handle)
            {
                if (owner == OwnerUi)
                {
                    UiHandle = handle;
                }
                else
                {
                    BattleHandle = handle;
                }
            }

            public bool IsLoading(string owner)
            {
                return (owner == OwnerUi ? UiLoading : BattleLoading) > 0;
            }

            public void AddLoading(string owner, int delta)
            {
                if (owner == OwnerUi)
                {
                    UiLoading += delta;
                }
                else
                {
                    BattleLoading += delta;
                }
            }

            public int RefCount
            {
                get
                {
                    var count = 0;
                    if (UiHandle != null && UiHandle.IsValid)
                    {
                        count++;
                    }

                    if (BattleHandle != null && BattleHandle.IsValid)
                    {
                        count++;
                    }

                    return count;
                }
            }
        }

        private static AssetDemoController _instance;

        private readonly List<AssetRow> _rows = new List<AssetRow>();
        private readonly Queue<string> _log = new Queue<string>();
        private readonly List<Text> _bundleTexts = new List<Text>();
        private readonly List<string> _bundleNames = new List<string>();
        private readonly List<Text> _assetKeys = new List<Text>();

        private PatchActivationService _activation;
        private BundleRuntime _runtime;
        private AssetCache _cache;
        private AssetMemoryEvaluator _evaluator;
        private AssetScope _uiScope;
        private AssetScope _battleScope;
        private CountingFactory _factory;
        private PatchManifest _manifest;

        private string _version = "<none>";
        private int _totalBundleLoads;
        private int _reclaimedAssets;
        private int _reclaimedBundles;
        private int _activationCount;

        /// <summary>
        /// Incremented every time the runtime is rebuilt. A load that started
        /// under an older generation must not publish its handle into the new
        /// cache.
        /// </summary>
        private int _generation;

        /// <summary>True while a version switch is in flight.</summary>
        private bool _switching;

        private Canvas _canvas;
        private Transform _viewportRoot;
        private Camera _viewportCamera;
        private RenderTexture _viewportTexture;
        private Text _versionText;
        private Text _statsText;
        private Text _logText;
        private Text _toastText;
        private Text _bundleHeader;
        private GameObject _missingOverlay;
        private Text _missingDetail;
        private float _toastTimer;
        private float _refreshTimer;

        /// <summary>
        /// Creates the demo. Called by <c>InteractiveDemoBootstrap</c>, which is
        /// the only entry point: this demo never boots itself, so a plain scene
        /// stays empty.
        /// </summary>
        public static AssetDemoController Spawn()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("[AssetDemo]");
            DontDestroyOnLoad(go);

            return go.AddComponent<AssetDemoController>();
        }

        /// <summary>True while the demo is alive (used by the scene bootstrap).</summary>
        public static bool IsRunning
        {
            get { return _instance != null; }
        }

        private void Awake()
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);

            BuildCameraAndViewport();
            BuildCanvas();

            Log("演示启动。资产来自 4.3 的真实 Bundle 产物。");
        }

        private async void Start()
        {
            // The interactive demo replaces the plain log HUD.
            var hud = FindFirstObjectByType<Game.AssetSystem.Demos.Diagnostics.AssetSystemHud>();
            if (hud != null)
            {
                hud.gameObject.SetActive(false);
            }

            await InitializeAsync();
        }

        private void OnDestroy()
        {
            if (_viewportTexture != null)
            {
                _viewportTexture.Release();
                Destroy(_viewportTexture);
            }
        }

        // ------------------------------------------------------------------
        // initialization
        // ------------------------------------------------------------------
        private async Task InitializeAsync()
        {
            if (!FullChainLayout.HasContent(FullChainLayout.V1Version))
            {
                ShowMissing(
                    "还没有构建内容。\n\n" +
                    "菜单：Tools / Game Asset System / Build Full-Chain Demo Content\n" +
                    "构建完成后点下面的按钮重新检测。");
                return;
            }

            ShowMissing(null);

            var v1Dir = FullChainLayout.ContentDir(FullChainLayout.V1Version);
            _manifest = FullChainRunner.LoadManifest(v1Dir);

            BuildRows();

            _activation = new PatchActivationService();

            var result = await _activation.ActivateAsync(
                _manifest,
                v1Dir,
                FullChainLayout.PlatformKey,
                0,
                true);

            if (result.Status != PatchActivationStatus.Succeeded ||
                _activation.ActiveContext == null)
            {
                ShowMissing("v1 激活失败：" + result.Status + "\n" + result.Message);
                return;
            }

            _activationCount++;
            RebuildRuntime();
            Log("v1 激活成功：" + _activation.ActiveContext.BundleCount + " bundles / " +
                _activation.ActiveContext.AssetCount + " assets");
            Toast("v1 就绪");
        }

        private void RebuildRuntime()
        {
            if (_activation == null || _activation.ActiveContext == null)
            {
                return;
            }

            // 激活 = 重建运行时：旧运行时的 Bundle 必须彻底卸载
            // 先提升代际：此后的完成回调都属于旧 cache，必须丢弃。
            _generation++;

            AssetBundle.UnloadAllAssetBundles(true);

            _factory = new CountingFactory();
            _runtime = new BundleRuntime(_activation.ActiveContext, _factory);
            _cache = new AssetCache(_activation.ActiveContext, _runtime);
            _evaluator = new AssetMemoryEvaluator(_cache, _runtime) { MaxUnloadsPerCall = 10 };
            _uiScope = _cache.CreateScope("UI");
            _battleScope = _cache.CreateScope("Battle");

            _version = _activation.ActiveContext.ContentVersion;

            for (var i = 0; i < _rows.Count; i++)
            {
                ReleaseRowHandles(_rows[i]);
                DestroyCube(_rows[i]);
            }

            RefreshBundles();
            Refresh();
        }

        private static void ReleaseRowHandles(AssetRow row)
        {
            if (row.UiHandle != null)
            {
                var handle = row.UiHandle;
                row.SetHandle(OwnerUi, null);

                if (handle.IsValid)
                {
                    handle.Dispose();
                }
            }

            if (row.BattleHandle != null)
            {
                var handle = row.BattleHandle;
                row.SetHandle(OwnerBattle, null);

                if (handle.IsValid)
                {
                    handle.Dispose();
                }
            }
        }

        private bool AnyRowLoading()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsLoading(OwnerUi) ||
                    _rows[i].IsLoading(OwnerBattle))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // actions
        // ------------------------------------------------------------------
        private async void Toggle(AssetRow row, string owner)
        {
            if (_switching)
            {
                Toast("正在切换版本，请稍候");
                return;
            }

            if (row.IsLoading(owner))
            {
                return;
            }

            var handle = row.HandleOf(owner);

            if (handle != null)
            {
                Release(row, owner);
                return;
            }

            if (_cache == null)
            {
                Toast("运行时未就绪");
                return;
            }

            var generation = _generation;

            row.AddLoading(owner, 1);
            Refresh();

            try
            {
                var scope = owner == OwnerUi ? _uiScope : _battleScope;
                var task = _cache.AcquireAsync<DemoGraphNode>(
                    new AssetKey(row.Key),
                    scope,
                    owner + "." + row.Key);

                var result = await task.Completion;

                if (generation != _generation)
                {
                    // The runtime was rebuilt while this load was in flight.
                    // Publishing the handle would put a handle from the old
                    // cache onto a row that the new cache knows nothing about.
                    if (result.Handle != null && result.Handle.IsValid)
                    {
                        result.Handle.Dispose();
                    }

                    Log(owner + " 加载 " + row.Key + " 完成于旧运行时，已丢弃");
                    return;
                }

                if (result.Status == AssetLoadStatus.Succeeded && result.Handle != null)
                {
                    row.SetHandle(owner, result.Handle);
                    _totalBundleLoads = _factory.LoadCount;
                    Log(owner + " 加载 " + row.Key + " → " + result.Value.label +
                        "（refs=" + (row.RefCount) + "，bundleLoads=" + _factory.LoadCount + "）");
                }
                else
                {
                    Log(owner + " 加载 " + row.Key + " 失败：" + result.Status + " " + result.Error.Message);
                    Toast("加载失败：" + result.Status);
                }
            }
            catch (Exception exception)
            {
                Log("加载异常：" + exception.Message);
            }
            finally
            {
                row.AddLoading(owner, -1);
                Refresh();
            }
        }

        private void Release(AssetRow row, string owner)
        {
            var handle = row.HandleOf(owner);

            if (handle == null)
            {
                return;
            }

            row.SetHandle(owner, null);
            handle.Dispose();

            Log(owner + " 释放 " + row.Key + "（refs=" + row.RefCount + "）");
            Refresh();
        }

        public void ReleaseAll()
        {
            var released = 0;

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];

                if (row.UiHandle != null)
                {
                    var handle = row.UiHandle;
                    row.SetHandle(OwnerUi, null);
                    handle.Dispose();
                    released++;
                }

                if (row.BattleHandle != null)
                {
                    var handle = row.BattleHandle;
                    row.SetHandle(OwnerBattle, null);
                    handle.Dispose();
                    released++;
                }
            }

            Log("全部释放：" + released + " 个 Handle");
            Refresh();
        }

        /// <summary>Loads every asset from UI, then two of them from Battle.</summary>
        public void RunAutoDemo()
        {
            if (_cache == null)
            {
                Toast("运行时未就绪");
                return;
            }

            StartCoroutine(AutoDemoRoutine());
        }

        private IEnumerator AutoDemoRoutine()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                Toggle(_rows[i], OwnerUi);
                yield return new WaitForSeconds(0.22f);
            }

            yield return new WaitForSeconds(0.5f);

            if (_rows.Count > 0)
            {
                Toggle(_rows[0], OwnerBattle);
                yield return new WaitForSeconds(0.18f);
            }

            if (_rows.Count > 3)
            {
                Toggle(_rows[3], OwnerBattle);
            }

            Toast("已加载全部资产：UI 5 个，Battle 2 个");
        }

        public void Reclaim()
        {
            if (_evaluator == null)
            {
                return;
            }

            var assets = 0;
            var bundles = 0;
            var rounds = 0;

            for (var i = 0; i < 12; i++)
            {
                var report = _evaluator.Evaluate();

                if (report.AssetsUnloaded == 0 && report.BundlesUnloaded == 0)
                {
                    break;
                }

                assets += report.AssetsUnloaded;
                bundles += report.BundlesUnloaded;
                rounds++;
            }

            _reclaimedAssets += assets;
            _reclaimedBundles += bundles;

            if (assets == 0 && bundles == 0)
            {
                Log("回收：没有可卸载的空闲资产（先释放 Handle）");
                Toast("没有可回收内容");
            }
            else
            {
                Log("回收完成：Asset " + assets + " / Bundle " + bundles +
                    "（" + rounds + " 轮，先 Asset 后 Bundle）");
                Toast("回收 Asset " + assets + " / Bundle " + bundles);
            }

            RefreshBundles();
            Refresh();
        }

        public async void SwitchVersion(string target)
        {
            if (_switching)
            {
                Toast("正在切换中");
                return;
            }

            if (_activation == null || _cache == null)
            {
                Toast("运行时未就绪");
                return;
            }

            if (target == _version)
            {
                Toast("已经是 " + target);
                return;
            }

            var dir = FullChainLayout.ContentDir(target);

            if (!File.Exists(FullChainLayout.ManifestPath(target)))
            {
                Toast("缺少内容：" + target);
                return;
            }

            // A load in flight has no handle yet, so TotalLiveHandles can read 0
            // while work is still running. Refuse to switch until it settles.
            if (AnyRowLoading() || _cache.LoadingAssetCount > 0)
            {
                Log("切换 " + target + " 被拒：仍有加载在进行");
                Toast("还有加载在进行，稍后再切换");
                return;
            }

            var manifest = FullChainRunner.LoadManifest(dir);
            var liveHandles = _cache.TotalLiveHandles;

            _switching = true;

            try
            {
                var result = await _activation.ActivateAsync(
                    manifest,
                    dir,
                    FullChainLayout.PlatformKey,
                    liveHandles,
                    true,
                    default(System.Threading.CancellationToken),
                    () => _cache == null ? 0 : _cache.TotalLiveHandles,
                    () => true);

                if (result.Status != PatchActivationStatus.Succeeded)
                {
                    var hint = result.Status == PatchActivationStatus.RejectedActiveHandles
                        ? "（还有 " + liveHandles + " 个 Handle 存活，先全部释放）"
                        : string.Empty;

                    Log("激活 " + target + " 被拒：" + result.Status + hint);
                    Toast("激活被拒：" + result.Status);
                    return;
                }

                _activationCount++;
                RebuildRuntime();
                Log("激活 " + target + " 成功，运行时已重建（第 " + _activationCount + " 次）");
                Toast("已切换到 " + target);
            }
            finally
            {
                _switching = false;
            }
        }

        // ------------------------------------------------------------------
        // frame updates
        // ------------------------------------------------------------------
        private void Update()
        {
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.deltaTime;

                if (_toastText != null)
                {
                    var alpha = Mathf.Clamp01(_toastTimer);
                    _toastText.color = new Color(
                        _toastText.color.r,
                        _toastText.color.g,
                        _toastText.color.b,
                        alpha);
                }
            }

            _refreshTimer -= Time.deltaTime;

            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.15f;
                Refresh();
            }

            UpdateCubes();
        }

        private void Refresh()
        {
            if (_cache == null || _runtime == null)
            {
                return;
            }

            var idleKeys = _cache.SnapshotIdleAssetKeys();

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var refs = row.RefCount;
                var idle = Contains(idleKeys, row.Key);

                var state = refs > 0
                    ? "Ready"
                    : idle ? "Idle（payload 保留）"
                    : row.IsLoading(OwnerUi) || row.IsLoading(OwnerBattle) ? "Loading"
                    : "NotLoaded";

                row.StateText.text = state + "   refs=" + refs;
                row.StateText.color = refs > 0
                    ? AssetDemoUiKit.Accent
                    : idle ? AssetDemoUiKit.Warn : AssetDemoUiKit.TextMuted;

                row.RefBar.rectTransform.sizeDelta = new Vector2(
                    Mathf.Clamp(refs, 0, 2) * 0.5f * 250f,
                    6f);

                row.RefBar.color = refs > 0 ? AssetDemoUiKit.Accent : AssetDemoUiKit.Idle;

                row.OwnerText.text =
                    "UI " + (row.UiHandle != null ? "●" : "○") +
                    "    Battle " + (row.BattleHandle != null ? "●" : "○");

                UpdateButton(row.UiCaption, row.UiButtonImage, row.UiHandle != null, row.IsLoading(OwnerUi));
                UpdateButton(row.BattleCaption, row.BattleButtonImage, row.BattleHandle != null, row.IsLoading(OwnerBattle));
            }

            _versionText.text = "版本  " + _version;
            _statsText.text =
                "活动 Handle      " + _cache.TotalLiveHandles + "\n" +
                "Ready / Idle     " + _cache.ReadyAssetCount + " / " + _cache.IdleAssetCount + "\n" +
                "Bundle 加载次数  " + _factory.LoadCount + "\n" +
                "累计回收          Asset " + _reclaimedAssets + " / Bundle " + _reclaimedBundles + "\n" +
                "激活次数          " + _activationCount;

            RefreshBundles();

            if (_logText != null)
            {
                var builder = new StringBuilder();

                foreach (var line in _log)
                {
                    builder.Append(line).Append('\n');
                }

                _logText.text = builder.ToString();
            }
        }

        private static void UpdateButton(Text caption, Image image, bool held, bool loading)
        {
            if (caption == null || image == null)
            {
                return;
            }

            if (loading)
            {
                caption.text = "...";
                image.color = new Color(0.35f, 0.38f, 0.44f, 1f);
                return;
            }

            caption.text = held ? "释放" : "加载";
            image.color = held
                ? new Color(0.16f, 0.42f, 0.34f, 1f)
                : new Color(0.13f, 0.16f, 0.21f, 1f);
        }

        private void RefreshBundles()
        {
            if (_runtime == null || _activation == null || _activation.ActiveContext == null)
            {
                return;
            }

            var context = _activation.ActiveContext;

            if (_bundleNames.Count != context.BundleCount)
            {
                _bundleNames.Clear();

                for (var i = 0; i < context.BundleCount; i++)
                {
                    _bundleNames.Add(context.TryGetBundle(new BundleId(i), out var bundle)
                        ? bundle.BundleGroupName
                        : "bundle_" + i);
                }
            }

            var idleKeys = _cache == null
                ? new List<AssetKey>()
                : _cache.SnapshotIdleAssetKeys();

            for (var i = 0; i < _bundleTexts.Count; i++)
            {
                var text = _bundleTexts[i];

                if (i >= _bundleNames.Count)
                {
                    text.text = string.Empty;
                    continue;
                }

                var name = _bundleNames[i];
                var ready = 0;
                var idleAssets = 0;

                for (var r = 0; r < _rows.Count; r++)
                {
                    if (AssetBundleOf(_rows[r].Key) != name)
                    {
                        continue;
                    }

                    if (_rows[r].RefCount > 0)
                    {
                        ready++;
                    }
                    else if (Contains(idleKeys, _rows[r].Key))
                    {
                        idleAssets++;
                    }
                }

                var leases = ready + idleAssets;
                var state = ready > 0
                    ? "Ready"
                    : idleAssets > 0 ? "Idle(被缓存持有)" : "NotLoaded";

                text.text = name.PadRight(13) + state.PadRight(9) + "leases=" + leases;
                text.color = ready > 0
                    ? AssetDemoUiKit.TextPrimary
                    : idleAssets > 0 ? AssetDemoUiKit.Warn : AssetDemoUiKit.TextMuted;
            }

            if (_bundleHeader != null)
            {
                _bundleHeader.text =
                    "Bundle 运行时     加载 " + _runtime.LoadedPayloadCount +
                    " / 就绪 " + _runtime.ReadyBundleCount +
                    " / 空闲 " + _runtime.IdleBundleCount;
            }
        }

        private string AssetBundleOf(string assetKey)
        {
            if (_manifest == null || _manifest.Assets == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < _manifest.Assets.Length; i++)
            {
                if (_manifest.Assets[i].AssetKey == assetKey)
                {
                    return _manifest.Assets[i].BundleGroupName;
                }
            }

            return string.Empty;
        }

        // ------------------------------------------------------------------
        // cubes
        // ------------------------------------------------------------------
        private void UpdateCubes()
        {
            if (_cache == null)
            {
                return;
            }

            var idleKeys = _cache.SnapshotIdleAssetKeys();

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var refs = row.RefCount;
                var idle = Contains(idleKeys, row.Key);

                if (refs == 0 && !idle)
                {
                    DestroyCube(row);
                    continue;
                }

                if (row.Cube == null)
                {
                    CreateCube(row, i);
                }

                if (row.Cube == null)
                {
                    continue;
                }

                var targetScale = refs > 0 ? 0.85f + 0.22f * (refs - 1) : 0.62f;
                row.Cube.transform.localScale = Vector3.Lerp(
                    row.Cube.transform.localScale,
                    Vector3.one * targetScale,
                    Time.deltaTime * 8f);

                row.Cube.transform.Rotate(0f, 18f * Time.deltaTime, 0f, Space.World);

                var renderer = row.Cube.GetComponent<Renderer>();

                if (renderer != null)
                {
                    // Never read Renderer.material here: that getter instantiates
                    // a material. The row owns exactly one instance, created in
                    // CreateCube and destroyed with the cube.
                    if (row.CubeMaterial == null)
                    {
                        row.CubeMaterial = renderer.sharedMaterial;
                    }

                    if (row.CubeMaterial != null)
                    {
                        var target = refs > 0
                            ? row.CubeColor
                            : row.CubeColor * 0.32f + Color.black * 0.2f;

                        var materialColor = row.CubeMaterial.HasProperty("_BaseColor")
                            ? row.CubeMaterial.GetColor("_BaseColor")
                            : row.CubeMaterial.color;

                        var blended = Color.Lerp(
                            materialColor,
                            target,
                            Time.deltaTime * 8f);

                        SetMaterialColor(row.CubeMaterial, blended);
                    }
                }
            }
        }

        private void CreateCube(AssetRow row, int index)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Asset_" + row.Key;
            cube.layer = ViewportLayer;
            cube.transform.SetParent(_viewportRoot, false);
            cube.transform.localPosition = new Vector3(-3.2f + index * 1.6f, 0.5f, 0f);
            cube.transform.localScale = Vector3.one * 0.1f;

            var renderer = cube.GetComponent<Renderer>();

            if (renderer != null)
            {
                var material = new Material(ResolveCubeShader());
                SetMaterialColor(material, row.CubeColor);

                // sharedMaterial keeps this the single instance the row owns;
                // Renderer.material would hand back an instantiated copy.
                renderer.sharedMaterial = material;
                row.CubeMaterial = material;
            }

            row.Cube = cube;
        }

        private static void DestroyCube(AssetRow row)
        {
            if (row.Cube == null)
            {
                row.CubeMaterial = null;
                return;
            }

            var renderer = row.Cube.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = null;
            }

            if (row.CubeMaterial != null)
            {
                Destroy(row.CubeMaterial);
                row.CubeMaterial = null;
            }

            Destroy(row.Cube);
            row.Cube = null;
        }

        private static Shader ResolveCubeShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = ResolveShader();
            }

            return shader;
        }

        private static Shader ResolveShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            return shader;
        }

        private static void ApplyGridTexture(Material material, float tiling)
        {
            var texture = CreateGridTexture();

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
            }
        }

        private static Texture2D CreateGridTexture()
        {
            const int size = 64;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            var fill = new Color(0.085f, 0.105f, 0.135f, 1f);
            var line = new Color(0.19f, 0.23f, 0.29f, 1f);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var edge = x < 1 || y < 1;
                    texture.SetPixel(x, y, edge ? line : fill);
                }
            }

            texture.Apply();
            return texture;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        // ------------------------------------------------------------------
        // ui construction
        // ------------------------------------------------------------------
        private void BuildCameraAndViewport()
        {
            var main = Camera.main;

            if (main != null)
            {
                main.cullingMask &= ~(1 << ViewportLayer);
            }

            _viewportTexture = new RenderTexture(1024, 420, 16)
            {
                name = "AssetDemoViewport",
                antiAliasing = 1
            };

            var cameraGo = new GameObject("AssetDemoViewportCamera");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.75f, -9f);
            cameraGo.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);

            _viewportCamera = cameraGo.AddComponent<Camera>();
            _viewportCamera.clearFlags = CameraClearFlags.SolidColor;
            _viewportCamera.backgroundColor = new Color(0.035f, 0.045f, 0.062f, 1f);
            _viewportCamera.cullingMask = 1 << ViewportLayer;
            _viewportCamera.fieldOfView = 38f;
            _viewportCamera.targetTexture = _viewportTexture;

            var root = new GameObject("AssetDemoViewportRoot");
            root.transform.SetParent(transform, false);
            _viewportRoot = root.transform;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ViewportFloor";
            floor.layer = ViewportLayer;
            floor.transform.SetParent(_viewportRoot, false);
            floor.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            floor.transform.localScale = new Vector3(2.4f, 1f, 1.6f);

            var floorRenderer = floor.GetComponent<Renderer>();

            if (floorRenderer != null)
            {
                var material = new Material(ResolveShader());
                SetMaterialColor(material, Color.white);
                ApplyGridTexture(material, 10f);
                floorRenderer.material = material;
            }

            var lightGo = new GameObject("AssetDemoViewportLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(38f, -35f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.cullingMask = 1 << ViewportLayer;
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("AssetDemoCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var eventGo = new GameObject("AssetDemoEventSystem");
                eventGo.transform.SetParent(transform, false);
                eventGo.AddComponent<EventSystem>();
                eventGo.AddComponent<StandaloneInputModule>();
            }

            var root = (RectTransform)canvasGo.transform;

            AssetDemoUiKit.PanelRect(root, "Backdrop", 0f, 0f, 1280f, 720f, AssetDemoUiKit.Background, 0f);

            BuildHeader(root);
            BuildAssetPanel(root);
            BuildViewport(root);
            BuildBundlePanel(root);
            BuildStatsPanel(root);
            BuildLogPanel(root);
            BuildToast(root);
        }

        private void BuildHeader(RectTransform root)
        {
            AssetDemoUiKit.Label(root, "Title", "Asset System · 交互演示", 20f, 14f, 420f, 26f, 18,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            AssetDemoUiKit.Label(root, "Subtitle",
                "真实 AssetBundle · 引用计数 · 版本切换 · 内存回收", 20f, 38f, 420f, 20f, 12,
                AssetDemoUiKit.TextMuted);

            var pill = AssetDemoUiKit.PanelRect(root, "VersionPill", 560f, 14f, 150f, 30f,
                AssetDemoUiKit.AccentDim, 6f);

            _versionText = AssetDemoUiKit.Label(pill.transform, "Version", "版本  <none>", 0f, 0f, 150f, 30f, 12,
                AssetDemoUiKit.Accent, TextAnchor.MiddleCenter, true);

            AssetDemoUiKit.Button(root, "ActivateV2", "激活 v2", 726f, 14f, 104f, 30f,
                new Color(0.13f, 0.16f, 0.21f, 1f), AssetDemoUiKit.TextPrimary,
                () => SwitchVersion(FullChainLayout.V2Version));

            AssetDemoUiKit.Button(root, "ActivateV1", "激活 v1", 840f, 14f, 104f, 30f,
                new Color(0.13f, 0.16f, 0.21f, 1f), AssetDemoUiKit.TextPrimary,
                () => SwitchVersion(FullChainLayout.V1Version));

            AssetDemoUiKit.Button(root, "ReleaseAll", "全部释放", 954f, 14f, 110f, 30f,
                new Color(0.13f, 0.16f, 0.21f, 1f), AssetDemoUiKit.TextPrimary,
                ReleaseAll);

            AssetDemoUiKit.Button(root, "Reclaim", "回收内存", 1074f, 14f, 110f, 30f,
                new Color(0.16f, 0.42f, 0.34f, 1f), new Color(0.85f, 0.98f, 0.93f, 1f),
                Reclaim);

            AssetDemoUiKit.PanelRect(root, "HeaderLine", 16f, 56f, 1248f, 1f,
                AssetDemoUiKit.Border, 0f);
        }

        private void BuildAssetPanel(RectTransform root)
        {
            AssetDemoUiKit.PanelRect(root, "AssetPanel", 16f, 68f, 436f, 636f,
                AssetDemoUiKit.Panel, 10f);

            AssetDemoUiKit.Label(root, "AssetTitle", "资产（按 owner 加载 / 释放）", 32f, 82f, 400f, 22f, 13,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            AssetDemoUiKit.Label(root, "AssetHint", "同一个 key 被两个 owner 持有时，Bundle 只加载一次",
                32f, 104f, 300f, 18f, 11, AssetDemoUiKit.TextMuted);

            AssetDemoUiKit.Button(root, "AutoDemo", "一键演示", 316f, 78f, 112f, 30f,
                new Color(0.16f, 0.42f, 0.34f, 1f), new Color(0.85f, 0.98f, 0.93f, 1f),
                RunAutoDemo);
        }

        private void BuildViewport(RectTransform root)
        {
            AssetDemoUiKit.PanelRect(root, "ViewportPanel", 468f, 68f, 796f, 336f,
                AssetDemoUiKit.Panel, 10f);

            var viewportGo = new GameObject("ViewportImage", typeof(RectTransform));
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.SetParent(root, false);
            viewportRect.anchorMin = new Vector2(0f, 1f);
            viewportRect.anchorMax = new Vector2(0f, 1f);
            viewportRect.pivot = new Vector2(0f, 1f);
            viewportRect.anchoredPosition = new Vector2(478f, -78f);
            viewportRect.sizeDelta = new Vector2(776f, 292f);

            var raw = viewportGo.AddComponent<RawImage>();
            raw.texture = _viewportTexture;
            raw.raycastTarget = false;

            AssetDemoUiKit.Label(root, "ViewportCaption",
                "运行时视图 · 每个方块 = 一个已加载资产（尺寸随引用数，灰色 = 空闲待回收）",
                486f, 374f, 760f, 18f, 11, AssetDemoUiKit.TextMuted);
        }

        private void BuildBundlePanel(RectTransform root)
        {
            AssetDemoUiKit.PanelRect(root, "BundlePanel", 468f, 404f, 384f, 300f,
                AssetDemoUiKit.Panel, 10f);

            _bundleHeader = AssetDemoUiKit.Label(root, "BundleTitle", "Bundle 运行时",
                484f, 418f, 360f, 20f, 13, AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            for (var i = 0; i < 6; i++)
            {
                var text = AssetDemoUiKit.Label(root, "BundleRow" + i, string.Empty,
                    484f, 448f + i * 24f, 360f, 20f, 12, AssetDemoUiKit.TextMuted);
                _bundleTexts.Add(text);
            }
        }

        private void BuildStatsPanel(RectTransform root)
        {
            AssetDemoUiKit.PanelRect(root, "StatsPanel", 864f, 404f, 400f, 140f,
                AssetDemoUiKit.Panel, 10f);

            AssetDemoUiKit.Label(root, "StatsTitle", "统计", 880f, 418f, 200f, 20f, 13,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            _statsText = AssetDemoUiKit.Label(root, "StatsBody", string.Empty, 880f, 442f, 370f, 96f, 12,
                AssetDemoUiKit.TextMuted);
        }

        private void BuildLogPanel(RectTransform root)
        {
            AssetDemoUiKit.PanelRect(root, "LogPanel", 864f, 556f, 400f, 148f,
                AssetDemoUiKit.Panel, 10f);

            AssetDemoUiKit.Label(root, "LogTitle", "操作日志", 880f, 568f, 200f, 20f, 13,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            _logText = AssetDemoUiKit.Label(root, "LogBody", string.Empty, 880f, 592f, 372f, 104f, 11,
                AssetDemoUiKit.TextMuted);
        }

        private void BuildToast(RectTransform root)
        {
            _toastText = AssetDemoUiKit.Label(root, "Toast", string.Empty, 440f, 600f, 400f, 28f, 14,
                new Color(1f, 1f, 1f, 0f), TextAnchor.MiddleCenter, true);
        }

        private void BuildRows()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                DestroyRow(_rows[i]);
            }

            _rows.Clear();
            _assetKeys.Clear();

            if (_manifest == null || _manifest.Assets == null)
            {
                return;
            }

            var root = (RectTransform)_canvas.transform;

            for (var i = 0; i < _manifest.Assets.Length; i++)
            {
                var asset = _manifest.Assets[i];
                var y = 130f + i * 112f;

                var row = new AssetRow
                {
                    Key = asset.AssetKey,
                    CubeColor = ColorFromKey(asset.AssetKey)
                };

                AssetDemoUiKit.PanelRect(root, "Row" + i, 28f, y, 412f, 102f,
                    AssetDemoUiKit.PanelAlt, 8f);

                row.KeyText = AssetDemoUiKit.Label(root, "Key" + i, asset.AssetKey,
                    40f, y + 10f, 250f, 20f, 13, AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

                AssetDemoUiKit.Label(root, "Group" + i, "bundle: " + asset.BundleGroupName,
                    40f, y + 30f, 250f, 16f, 11, AssetDemoUiKit.TextMuted);

                row.StateText = AssetDemoUiKit.Label(root, "State" + i, "NotLoaded   refs=0",
                    40f, y + 50f, 250f, 18f, 11, AssetDemoUiKit.TextMuted);

                var barBg = AssetDemoUiKit.PanelRect(root, "BarBg" + i, 40f, y + 72f, 250f, 6f,
                    new Color(1f, 1f, 1f, 0.08f), 3f);

                row.RefBar = AssetDemoUiKit.PanelRect(barBg.transform, "BarFill" + i, 0f, 0f, 0f, 6f,
                    AssetDemoUiKit.Idle, 3f);
                row.RefBar.rectTransform.anchorMin = new Vector2(0f, 1f);
                row.RefBar.rectTransform.anchorMax = new Vector2(0f, 1f);
                row.RefBar.rectTransform.pivot = new Vector2(0f, 1f);
                row.RefBar.rectTransform.anchoredPosition = Vector2.zero;

                var uiButton = AssetDemoUiKit.Button(root, "UiBtn" + i, "加载",
                    302f, y + 16f, 62f, 32f, new Color(0.13f, 0.16f, 0.21f, 1f),
                    AssetDemoUiKit.TextPrimary, () => Toggle(row, OwnerUi), 12);

                row.UiButtonImage = uiButton.GetComponent<Image>();
                row.UiCaption = uiButton.transform.Find("Caption").GetComponent<Text>();

                var battleButton = AssetDemoUiKit.Button(root, "BattleBtn" + i, "加载",
                    368f, y + 16f, 62f, 32f, new Color(0.13f, 0.16f, 0.21f, 1f),
                    AssetDemoUiKit.TextPrimary, () => Toggle(row, OwnerBattle), 12);

                row.BattleButtonImage = battleButton.GetComponent<Image>();
                row.BattleCaption = battleButton.transform.Find("Caption").GetComponent<Text>();

                AssetDemoUiKit.Label(root, "OwnerTag" + i, "UI", 302f, y + 4f, 40f, 14f, 10,
                    AssetDemoUiKit.TextMuted, TextAnchor.UpperCenter);

                AssetDemoUiKit.Label(root, "OwnerTag2" + i, "Battle", 368f, y + 4f, 62f, 14f, 10,
                    AssetDemoUiKit.TextMuted, TextAnchor.UpperCenter);

                row.OwnerText = AssetDemoUiKit.Label(root, "Owner" + i, "UI ○    Battle ○",
                    302f, y + 54f, 130f, 16f, 10, AssetDemoUiKit.TextMuted);

                _rows.Add(row);
                _assetKeys.Add(row.KeyText);
            }

            var lastY = 130f + _rows.Count * 112f;

            AssetDemoUiKit.Label(root, "PanelFooter",
                "提示：先点「加载」再点「释放」，观察 refs 归零后进入 Idle；" +
                "点「回收内存」后 Asset 先于 Bundle 卸载。",
                32f, lastY + 6f, 400f, 40f, 11, AssetDemoUiKit.TextMuted);
        }

        private void DestroyRow(AssetRow row)
        {
            DestroyCube(row);
        }

        private void ShowMissing(string detail)
        {
            if (detail == null)
            {
                if (_missingOverlay != null)
                {
                    Destroy(_missingOverlay);
                    _missingOverlay = null;
                }

                return;
            }

            if (_missingOverlay == null)
            {
                var root = (RectTransform)_canvas.transform;

                _missingOverlay = AssetDemoUiKit.PanelRect(root, "MissingOverlay",
                    0f, 0f, 1280f, 720f, new Color(0.02f, 0.03f, 0.04f, 0.92f), 0f).gameObject;

                var card = AssetDemoUiKit.PanelRect(_missingOverlay.transform, "MissingCard",
                    340f, 220f, 600f, 280f, AssetDemoUiKit.Panel, 12f);

                AssetDemoUiKit.Label(card.transform, "MissingTitle", "需要先生成演示资源",
                    24f, 20f, 550f, 24f, 16, AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

                _missingDetail = AssetDemoUiKit.Label(card.transform, "MissingBody", detail,
                    24f, 56f, 550f, 120f, 12, AssetDemoUiKit.TextMuted);

                AssetDemoUiKit.Button(card.transform, "Retry", "重新检测", 24f, 200f, 130f, 36f,
                    new Color(0.16f, 0.42f, 0.34f, 1f), new Color(0.85f, 0.98f, 0.93f, 1f),
                    () => Retry());
            }
            else if (_missingDetail != null)
            {
                _missingDetail.text = detail;
            }
        }

        private async void Retry()
        {
            ShowMissing(null);
            await InitializeAsync();
        }

        private void Log(string message)
        {
            if (_log.Count >= 7)
            {
                _log.Dequeue();
            }

            _log.Enqueue(message);

            if (_logText != null)
            {
                var builder = new StringBuilder();

                foreach (var line in _log)
                {
                    builder.Append(line).Append('\n');
                }

                _logText.text = builder.ToString();
            }
        }

        private void Toast(string message)
        {
            if (_toastText == null)
            {
                return;
            }

            _toastText.text = message;
            _toastText.color = new Color(
                AssetDemoUiKit.Accent.r,
                AssetDemoUiKit.Accent.g,
                AssetDemoUiKit.Accent.b,
                1f);
            _toastTimer = 2.2f;
        }

        private static bool Contains(List<AssetKey> keys, string value)
        {
            for (var i = 0; i < keys.Count; i++)
            {
                if (keys[i].Value == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static Color ColorFromKey(string key)
        {
            var hash = 17;

            for (var i = 0; i < key.Length; i++)
            {
                hash = hash * 31 + key[i];
            }

            var hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.55f, 0.95f);
        }

        private sealed class CountingFactory : IBundlePayloadFactory
        {
            private readonly RealBundlePayloadFactory _inner = new RealBundlePayloadFactory();
            private int _loadCount;

            public int LoadCount
            {
                get { return _loadCount; }
            }

            public Task<IBundlePayload> LoadAsync(
                RuntimeBundle bundle,
                System.Threading.CancellationToken cancellationToken)
            {
                _loadCount++;
                return _inner.LoadAsync(bundle, cancellationToken);
            }
        }
    }
}
