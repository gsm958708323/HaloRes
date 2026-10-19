using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Game.AssetSystem.Demos.Interactive;
using Game.AssetSystem.Integration;
using Game.AssetSystem.Memory;
using Game.AssetSystem.Release;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.AssetSystem.Demos.FullFeature
{
    /// <summary>
    /// Interactive dashboard for <c>Demo_FullFeature.unity</c>.
    ///
    /// Every button calls the same <see cref="FullFeatureSession"/> API the
    /// PlayMode scenario uses, so the scene is a window onto real framework
    /// state rather than a mock: bundle states, lease counts, idle assets and
    /// staging files are read back, never simulated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FullFeatureDashboard : MonoBehaviour
    {
        private const float LeftWidth = 540f;
        private const float PanelHeight = 688f;
        private const float ContentWidth = 692f;

        private static FullFeatureDashboard _instance;

        private FullFeatureSession _session;
        private Canvas _canvas;

        private Text _statusLine1;
        private Text _statusLine2;
        private Text _statusLine3;
        private Text _downloadStatus;
        private Text _logText;
        private Text _contentBody;
        private Text _bundleTable;
        private Text _idleAssets;
        private Text _stagingText;
        private Text _activateCaption;
        private Image _progressFill;
        private Image _sharedSwatch;

        private readonly Dictionary<string, Image> _versionButtons =
            new Dictionary<string, Image>(StringComparer.Ordinal);

        private string _selectedVersion = "v2";
        private bool _supportsRange = true;
        private bool _busy;
        private float _nextRefresh;

        private CancellationTokenSource _downloadCts;
        private FullFeatureDownloadReport _lastDownload;

        private string _lastLabel = "-";
        private string _lastSharedVersion = "-";
        private Color _lastSharedColor = Color.white;

        /// <summary>Creates the dashboard. Safe to call more than once.</summary>
        public static FullFeatureDashboard Spawn()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var existing = FindFirstObjectByType<FullFeatureDashboard>();

            if (existing != null)
            {
                _instance = existing;
                return existing;
            }

            var go = new GameObject("[FullFeatureDashboard]");
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<FullFeatureDashboard>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            BuildCanvas();
            BuildLeftPanel();
            BuildContentCard();
            BuildStatusPanel();
        }

        private async void Start()
        {
            // The dashboard replaces the plain log HUD, which would otherwise
            // cover the top-left quarter of the screen.
            var hud = FindFirstObjectByType<
                Game.AssetSystem.Demos.Diagnostics.AssetSystemHud>();

            if (hud != null)
            {
                hud.gameObject.SetActive(false);
            }

            _session = new FullFeatureSession();
            _session.DownloadProgress += OnDownloadProgress;

            try
            {
                var ok = await _session.InitializeAsync();

                if (!ok)
                {
                    _downloadStatus.text = _session.InitError;
                    Debug.LogError("[FullFeature] init failed: " + _session.InitError);
                }
                else
                {
                    _session.AppendLog("就绪。内容根目录 " + _session.ClientRoot);
                }
            }
            catch (Exception exception)
            {
                _downloadStatus.text = "初始化异常：" + exception.Message;
                Debug.LogException(exception);
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.DownloadProgress -= OnDownloadProgress;
            }

            if (_downloadCts != null)
            {
                _downloadCts.Cancel();
                _downloadCts.Dispose();
                _downloadCts = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnDownloadProgress(long transferred, long total)
        {
            if (total <= 0)
            {
                return;
            }

            var ratio = Mathf.Clamp01((float)((double)transferred / total));
            _progressFill.rectTransform.sizeDelta =
                new Vector2(504f * ratio, 8f);
        }

        private void Update()
        {
            if (_session != null)
            {
                _session.Tick();
            }

            if (Time.unscaledTime < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.unscaledTime + 0.1f;
            Refresh();
        }

        // ------------------------------------------------------------------
        // UI construction
        // ------------------------------------------------------------------

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("FullFeatureCanvas");
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
                var eventGo = new GameObject("FullFeatureEventSystem");
                eventGo.transform.SetParent(transform, false);
                eventGo.AddComponent<EventSystem>();
                eventGo.AddComponent<StandaloneInputModule>();
            }

            var root = new GameObject("Root", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(canvasGo.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var background = root.AddComponent<Image>();
            background.color = AssetDemoUiKit.Background;
            background.raycastTarget = false;

            _canvas.transform.SetAsLastSibling();
        }

        private RectTransform Root
        {
            get { return (RectTransform)_canvas.transform.Find("Root"); }
        }

        private void BuildLeftPanel()
        {
            var panel = AssetDemoUiKit.PanelRect(
                Root,
                "LeftPanel",
                16f,
                16f,
                LeftWidth,
                PanelHeight,
                AssetDemoUiKit.Panel,
                8f);

            var root = panel.rectTransform;

            AssetDemoUiKit.Label(
                root, "Title", "全功能交互式 Demo",
                18f, 12f, 300f, 24f, 17,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            AssetDemoUiKit.Label(
                root, "Subtitle", "加载 → 共享 → 释放 → 下载中断 → 续传 → 激活 → 重建 → 回收",
                18f, 36f, 500f, 18f, 11, AssetDemoUiKit.TextMuted);

            _statusLine1 = AssetDemoUiKit.Label(
                root, "Status1", "-", 18f, 62f, 504f, 18f, 12,
                AssetDemoUiKit.TextPrimary);

            _statusLine2 = AssetDemoUiKit.Label(
                root, "Status2", "-", 18f, 80f, 504f, 18f, 11,
                AssetDemoUiKit.TextMuted);

            _statusLine3 = AssetDemoUiKit.Label(
                root, "Status3", "-", 18f, 98f, 504f, 18f, 11,
                AssetDemoUiKit.TextMuted);

            Header(root, "① 加载与共享（Scope → Handle → Bundle）", 122f);

            MakeButton(root, "LoadPanelA", "加载 ui/panel (A)", 18f, 146f, 160f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                () => LoadAsset(FullFeatureContent.PanelKey, "S.A"));

            MakeButton(root, "LoadFxB", "加载 fx/hit (B)", 186f, 146f, 160f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                () => LoadAsset(FullFeatureContent.HitKey, "S.B"));

            MakeButton(root, "LoadPanelC", "加载 ui/panel (C)", 354f, 146f, 160f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                () => LoadAsset(FullFeatureContent.PanelKey, "S.C"));

            MakeButton(root, "ReleaseAll", "释放全部 Handle", 18f, 180f, 160f, 28f,
                AssetDemoUiKit.AccentDim, AssetDemoUiKit.Accent, ReleaseAll);

            Header(root, "② 安全点", 216f);

            MakeButton(root, "SafeOn", "进入主菜单（安全点 ON）", 18f, 240f, 240f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                () => ToggleSafePoint(true));

            MakeButton(root, "SafeOff", "进入战斗（安全点 OFF）", 266f, 240f, 240f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                () => ToggleSafePoint(false));

            Header(root, "③ Patch 下载（.part 分块 + 断点续传）", 276f);

            BuildVersionSelector(root, 300f);

            MakeButton(root, "DownloadFull", "完整下载", 18f, 332f, 160f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                () => StartDownload(0));

            MakeButton(root, "DownloadInterrupt", "70% 中断", 186f, 332f, 160f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.Warn,
                () => StartDownload(70));

            MakeButton(root, "DownloadCancel", "中断当前下载", 354f, 332f, 160f, 28f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.Danger, CancelDownload);

            var barBackground = AssetDemoUiKit.PanelRect(
                root, "ProgressBg", 18f, 368f, 504f, 8f,
                AssetDemoUiKit.PanelAlt, 3f);

            _progressFill = AssetDemoUiKit.PanelRect(
                barBackground.rectTransform, "ProgressFill",
                0f, 0f, 0f, 8f, AssetDemoUiKit.Accent, 3f);

            _downloadStatus = AssetDemoUiKit.Label(
                root, "DownloadStatus", "选择版本后下载。中断会保留 .part 供续传。",
                18f, 382f, 504f, 34f, 11, AssetDemoUiKit.TextMuted);

            Header(root, "④ 激活（校验 + 安全点 + 活动 Handle 检查）", 420f);

            var activateButton = MakeButton(root, "Activate", "激活 v2", 18f, 444f, 240f, 28f,
                AssetDemoUiKit.AccentDim, AssetDemoUiKit.Accent,
                ActivateSelected);

            _activateCaption = activateButton.transform.Find("Caption")
                .GetComponent<Text>();

            Header(root, "⑤ 内存压力（分级 + 每帧预算）", 480f);

            MakeButton(root, "PressureMedium", "Medium", 18f, 504f, 120f, 26f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                () => SetPressure(MemoryPressureLevel.Medium));

            MakeButton(root, "PressureHigh", "High", 146f, 504f, 120f, 26f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.Warn,
                () => SetPressure(MemoryPressureLevel.High));

            MakeButton(root, "PressureCritical", "Critical", 274f, 504f, 120f, 26f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.Danger,
                () => SetPressure(MemoryPressureLevel.Critical));

            MakeButton(root, "PressureNormal", "恢复正常", 402f, 504f, 120f, 26f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextMuted,
                () => SetPressure(MemoryPressureLevel.Normal));

            Header(root, "⑥ 日志", 540f);

            _logText = AssetDemoUiKit.Label(
                root, "Log", "-", 18f, 562f, 504f, 118f, 11,
                AssetDemoUiKit.TextMuted);

            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void BuildVersionSelector(RectTransform root, float y)
        {
            var x = 18f;

            for (var i = 0; i < FullFeatureLayout.Versions.Length; i++)
            {
                var version = FullFeatureLayout.Versions[i];
                var captured = version;

                var button = MakeButton(
                    root,
                    "Version_" + version,
                    version,
                    x,
                    y,
                    60f,
                    26f,
                    AssetDemoUiKit.PanelAlt,
                    AssetDemoUiKit.TextPrimary,
                    () => SelectVersion(captured),
                    12);

                _versionButtons[version] = button.GetComponent<Image>();
                x += 66f;
            }

            MakeButton(root, "RangeToggle", "Range: ON", x + 8f, y, 120f, 26f,
                AssetDemoUiKit.PanelAlt, AssetDemoUiKit.TextPrimary,
                ToggleRange, 12);

            SelectVersion(_selectedVersion);
        }

        private void BuildContentCard()
        {
            var card = AssetDemoUiKit.PanelRect(
                Root, "ContentCard", 572f, 16f, ContentWidth, 200f,
                AssetDemoUiKit.Panel, 8f);

            var root = card.rectTransform;

            AssetDemoUiKit.Label(
                root, "Title", "当前内容", 16f, 12f, 200f, 22f, 15,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            _sharedSwatch = AssetDemoUiKit.PanelRect(
                root, "SharedSwatch", 16f, 44f, 44f, 44f,
                AssetDemoUiKit.PanelAlt, 6f);

            _contentBody = AssetDemoUiKit.Label(
                root, "Body", "-", 72f, 40f, 600f, 120f, 12,
                AssetDemoUiKit.TextPrimary);
        }

        private void BuildStatusPanel()
        {
            var panel = AssetDemoUiKit.PanelRect(
                Root, "StatusPanel", 572f, 232f, ContentWidth, 472f,
                AssetDemoUiKit.Panel, 8f);

            var root = panel.rectTransform;

            AssetDemoUiKit.Label(
                root, "Title", "Bundle / 资产状态（只读，来自运行时快照）",
                16f, 12f, 500f, 22f, 15,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);

            _bundleTable = AssetDemoUiKit.Label(
                root, "Bundles", "-", 16f, 42f, 660f, 96f, 12,
                AssetDemoUiKit.TextPrimary);

            _idleAssets = AssetDemoUiKit.Label(
                root, "IdleAssets", "-", 16f, 142f, 660f, 40f, 11,
                AssetDemoUiKit.TextMuted);

            _stagingText = AssetDemoUiKit.Label(
                root, "Staging", "-", 16f, 186f, 660f, 40f, 11,
                AssetDemoUiKit.TextMuted);

            AssetDemoUiKit.Label(
                root, "Hint",
                "建议操作顺序：\n" +
                "A. 加载 ui/panel(A) → 加载 fx/hit(B) → 加载 ui/panel(C)：观察 shared 只加载一次、C 命中缓存。\n" +
                "B. 释放全部 → 选择 v2 → 70% 中断 → 完整下载（看续传字节）→ 激活 v2 → 重新加载看 shared 变成 v2。\n" +
                "C. 先加载一个资源再激活 → RejectedActiveHandles；安全点 OFF 再激活 → RejectedUnsafePoint。\n" +
                "D. 释放全部后点 Critical → Idle 资源与 Bundle 依次归零。",
                16f, 236f, 660f, 200f, 11,
                AssetDemoUiKit.TextMuted);
        }

        private static void Header(RectTransform parent, string text, float y)
        {
            AssetDemoUiKit.Label(
                parent, "Header_" + y, text, 18f, y, 504f, 18f, 12,
                AssetDemoUiKit.TextPrimary, TextAnchor.UpperLeft, true);
        }

        private static Button MakeButton(
            RectTransform parent,
            string name,
            string caption,
            float x,
            float y,
            float width,
            float height,
            Color normal,
            Color captionColor,
            Action onClick,
            int fontSize = 12)
        {
            return AssetDemoUiKit.Button(
                parent, name, caption, x, y, width, height,
                normal, captionColor, onClick, fontSize);
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private bool CanAct
        {
            get { return _session != null && _session.Ready; }
        }

        private async void LoadAsset(string key, string scopeName)
        {
            if (!CanAct)
            {
                return;
            }

            try
            {
                var result = await _session.Load(key, scopeName);

                if (result.Status != AssetLoadStatus.Succeeded)
                {
                    _session.AppendLog("加载失败 " + key + "：" + result.Error);
                    return;
                }

                var info = result.Value;
                _lastLabel = info.label;
                _lastSharedVersion = info.shared == null
                    ? "?"
                    : info.shared.version;
                _lastSharedColor = info.shared == null
                    ? Color.white
                    : info.shared.color;

                _session.AppendLog(
                    "加载 " + key + "（" + scopeName + "）→ " + info.label +
                    " · shared=" + _lastSharedVersion);
            }
            catch (Exception exception)
            {
                _session.AppendLog("加载异常 " + key + "：" + exception.Message);
                Debug.LogException(exception);
            }

            Refresh();
        }

        private void ReleaseAll()
        {
            if (!CanAct)
            {
                return;
            }

            var live = _session.ReleaseAllScopes();
            _session.AppendLog("释放全部 Scope，剩余 Handle=" + live);
            Refresh();
        }

        private void ToggleSafePoint(bool enter)
        {
            if (_session == null)
            {
                return;
            }

            if (enter)
            {
                _session.SafePoint.Enter("MainMenu");
                _session.AppendLog("安全点 ON（主菜单）");
            }
            else
            {
                _session.SafePoint.Exit("MainMenu");
                _session.AppendLog("安全点 OFF（战斗中）");
            }

            Refresh();
        }

        private void SelectVersion(string version)
        {
            _selectedVersion = version;

            foreach (var pair in _versionButtons)
            {
                pair.Value.color = string.Equals(
                    pair.Key, version, StringComparison.Ordinal)
                    ? AssetDemoUiKit.AccentDim
                    : AssetDemoUiKit.PanelAlt;
            }

            if (_activateCaption != null)
            {
                _activateCaption.text = "激活 " + version;
            }
        }

        private void ToggleRange()
        {
            _supportsRange = !_supportsRange;
            _session.AppendLog("Server Range = " + (_supportsRange ? "ON" : "OFF"));
            Refresh();
        }

        private async void StartDownload(int interruptAtPercent)
        {
            if (!CanAct || _busy)
            {
                return;
            }

            _busy = true;
            _lastDownload = null;
            _downloadStatus.text = "下载 " + _selectedVersion + " 中…";

            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            try
            {
                var report = await _session.DownloadAsync(
                    _selectedVersion,
                    interruptAtPercent,
                    _supportsRange,
                    token);

                _lastDownload = report;

                _downloadStatus.text = report.Status +
                                  " · 提交 " + report.Committed +
                                  " · 跳过 " + report.Skipped +
                                  " · 续传 " + report.ResumeBytes + " 字节\n" +
                                  report.Message;
            }
            catch (Exception exception)
            {
                _downloadStatus.text = "下载异常：" + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                _busy = false;
                _downloadCts.Dispose();
                _downloadCts = null;
                Refresh();
            }
        }

        private void CancelDownload()
        {
            if (_downloadCts != null)
            {
                _downloadCts.Cancel();
                _session.AppendLog("已请求中断下载");
            }
        }

        private async void ActivateSelected()
        {
            if (!CanAct || _busy)
            {
                return;
            }

            _busy = true;

            try
            {
                var result = await _session.ActivateAsync(_selectedVersion);

                _downloadStatus.text = "激活 " + _selectedVersion + "：" +
                                  result.Status + "\n" + result.Message;

                if (result.Status == PatchActivationStatus.Succeeded)
                {
                    // The old runtime is gone: nothing is loaded any more.
                    _lastLabel = "-";
                    _lastSharedVersion = "-";
                    _lastSharedColor = AssetDemoUiKit.PanelAlt;
                    _progressFill.rectTransform.sizeDelta = new Vector2(0f, 8f);
                }
            }
            catch (Exception exception)
            {
                _downloadStatus.text = "激活异常：" + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                _busy = false;
                Refresh();
            }
        }

        private void SetPressure(MemoryPressureLevel level)
        {
            if (_session == null)
            {
                return;
            }

            _session.SetMemoryPressure(level);
            Refresh();
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------

        private void Refresh()
        {
            if (_session == null)
            {
                return;
            }

            if (!_session.Ready)
            {
                _statusLine1.text = "初始化中…";
                _statusLine2.text = _session.InitError ?? "";
                _statusLine3.text = "";
                return;
            }

            _statusLine1.text =
                "Active " + _session.ActiveVersion +
                "  ·  SafePoint " + (_session.SafePoint.IsSafePoint ? "ON" : "OFF") +
                "  ·  加载预算 " + _session.StartsPerFrame + "/帧" +
                "  ·  Scope " + _session.ScopeCount;

            _statusLine2.text =
                "Assets " + _session.DescribeAssets() +
                "  ·  Bundles " + _session.DescribeBundles() +
                "  ·  排队 " + _session.GatePendingCount;

            _statusLine3.text =
                "内存压力 " + _session.Pressure +
                "  ·  累计回收 " + _session.LastReclaimedAssets +
                " 资源 / " + _session.LastReclaimedBundles + " Bundle" +
                "  ·  暂存 " + _selectedVersion + " " +
                _session.DescribeStaging(_selectedVersion);

            _contentBody.text =
                "Active 版本：" + _session.ActiveVersion + "\n" +
                "最近加载：" + _lastLabel + "\n" +
                "shared.version：" + _lastSharedVersion + "\n" +
                "shared.color：#" + ColorUtility.ToHtmlStringRGB(_lastSharedColor);

            _sharedSwatch.color = _lastSharedColor;

            var table = new StringBuilder();
            var bundles = _session.SnapshotBundles();

            for (var i = 0; i < bundles.Count; i++)
            {
                var entry = bundles[i];

                table.Append(Pad(entry.GroupName, 12))
                    .Append(Pad(StateText(entry.State), 10))
                    .Append("refs=").Append(entry.UserRefs)
                    .Append(entry.WaitingCount > 0 ? " wait=" + entry.WaitingCount : "")
                    .Append(entry.HasPayload ? "  payload" : "  -")
                    .AppendLine();
            }

            if (bundles.Count == 0)
            {
                table.AppendLine("(no bundles)");
            }

            _bundleTable.text = table.ToString();

            var idle = _session.SnapshotIdleAssetKeys();
            _idleAssets.text = idle.Count == 0
                ? "Idle 资产：无"
                : "Idle 资产（可被回收）：" + string.Join(", ", idle);

            _stagingText.text =
                "暂存目录：" + _session.DescribeStaging(_selectedVersion) +
                "\n" + "状态目录：" + _session.ActiveStatePath;

            var logs = _session.Logs;
            var builder = new StringBuilder();

            for (var i = Mathf.Max(0, logs.Count - 8); i < logs.Count; i++)
            {
                builder.AppendLine(Shorten(logs[i], 88));
            }

            _logText.text = builder.ToString();

            if (_lastDownload != null && _lastDownload.TotalBytes > 0)
            {
                var ratio = Mathf.Clamp01(
                    (float)((double)_lastDownload.TransferredBytes /
                            _lastDownload.TotalBytes));

                _progressFill.rectTransform.sizeDelta =
                    new Vector2(504f * ratio, 8f);
            }
        }

        private static string StateText(ContentBundleState state)
        {
            switch (state)
            {
                case ContentBundleState.Loading:
                    return "Loading";
                case ContentBundleState.Ready:
                    return "Ready";
                case ContentBundleState.Idle:
                    return "Idle";
                case ContentBundleState.Failed:
                    return "Failed";
                default:
                    return "NotLoaded";
            }
        }

        private static string Pad(string value, int width)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new string(' ', width);
            }

            return value.Length >= width
                ? value + " "
                : value + new string(' ', width - value.Length);
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max - 1) + "…";
        }
    }
}
