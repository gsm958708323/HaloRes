using System;
using System.Collections.Generic;
using GameUI.Components;
using GameUI.Kernel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))]
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasScaler _scaler;
        [SerializeField] private RectTransform _contentArea;
        [SerializeField] private RectTransform _windowRoot;
        [SerializeField] private RectTransform _overlayRoot;
        [SerializeField] private Image _mask;
        [SerializeField] private Image _blocker;
        [SerializeField] private Image _loadingIndicator;
        [SerializeField] private Canvas _maskCanvas;
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField] private UIScreenFitMode _screenFitMode = UIScreenFitMode.Expand;

        private readonly Dictionary<UILayer, RectTransform> _layerRoots = new Dictionary<UILayer, RectTransform>();
        private UIKernel _kernel;
        private UIViewPool _pool;
        private Button _maskButton;
        private Canvas _blockerCanvas;
        private Canvas _loadingCanvas;
        private Image _letterbox;
        private RectTransform _worldHudRoot;
        private Canvas _worldHudCanvas;
        private Transform _modelStageRoot;
        private UIScreenFitter _screenFitter;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        private ScreenOrientation _lastOrientation;
        private int _maskOwnerId;
        private int _fittersVersion = -1;
        private bool _built;

        public RectTransform WindowRoot { get { return _windowRoot; } }
        public RectTransform OverlayRoot { get { return _overlayRoot; } }
        public RectTransform ContentArea { get { return _contentArea; } }
        public RectTransform WorldHudRoot { get { return _worldHudRoot; } }
        public Transform ModelStageRoot { get { return _modelStageRoot; } }
        public Canvas RootCanvas { get { return _canvas; } }
        public Image Mask { get { return _mask; } }
        public Image Blocker { get { return _blocker; } }
        public Image LoadingIndicator { get { return _loadingIndicator; } }
        public Canvas MaskCanvas { get { return _maskCanvas; } }
        public Vector2 ReferenceResolution { get { return _referenceResolution; } }
        public static int UIRenderLayer
        {
            get
            {
                var layer = LayerMask.NameToLayer("UI");
                return layer >= 0 ? layer : 5;
            }
        }

        public void Install(UIKernel kernel, UIViewPool pool)
        {
            EnsureVisualTree();
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            ApplyScreenMetrics(true);
        }

        public void ConfigureScreen(Vector2 referenceResolution, UIScreenFitMode fitMode)
        {
            _referenceResolution = new Vector2(Mathf.Max(1f, referenceResolution.x), Mathf.Max(1f, referenceResolution.y));
            _screenFitMode = fitMode;
            _screenFitter = null;
            EnsureVisualTree();
            _scaler.referenceResolution = _referenceResolution;
            _screenFitter = new UIScreenFitter(_contentArea, _scaler, _referenceResolution, _screenFitMode);
            ApplyScreenMetrics(true);
        }

        public Transform GetLayerRoot(UILayer layer)
        {
            EnsureVisualTree();
            RectTransform root;
            if (_layerRoots.TryGetValue(layer, out root)) return root;
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "This layer cannot host a view.");
        }

        public void SetCamera(Camera camera)
        {
            EnsureVisualTree();
            _canvas.renderMode = camera != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = camera;
            _canvas.planeDistance = 100f;
            if (_worldHudCanvas != null) _worldHudCanvas.worldCamera = camera;
            ApplyScreenMetrics(true);
        }

        public void ApplyMask(in UIPresentation presentation)
        {
            if (_mask == null) return;
            _mask.gameObject.SetActive(presentation.MaskVisible);
            _mask.color = presentation.MaskStyle == UIMaskStyle.Dim
                ? new Color(0f, 0f, 0f, 0.55f) : new Color(0f, 0f, 0f, 0f);
            _maskOwnerId = presentation.MaskOwnerId;
            if (_maskCanvas != null) _maskCanvas.sortingOrder = presentation.MaskOrder;
            if (_maskButton != null) _maskButton.interactable = presentation.MaskVisible && presentation.MaskCloseOnClick;
        }

        public void ApplyInputBlocker(bool locked, bool systemOverlayVisible, int systemOverlayOrder)
        {
            if (systemOverlayVisible) UIBoot.Drags?.CancelAll();
            if (_blocker == null) return;
            _blocker.gameObject.SetActive(locked || systemOverlayVisible);
            _blocker.raycastTarget = locked || systemOverlayVisible;
            if (_blockerCanvas != null) _blockerCanvas.sortingOrder = locked ? 9900 : systemOverlayOrder;
        }

        public void SetLoadingIndicator(bool visible)
        {
            if (_loadingIndicator != null) _loadingIndicator.gameObject.SetActive(visible);
        }

        private void Awake() { EnsureVisualTree(); }

        private void Update()
        {
            if (_kernel == null || _kernel.IsDisposed) return;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.startButton.wasPressedThisFrame))
                _kernel.Navigator.Back();
        }

        private void LateUpdate()
        {
            if (_kernel == null || _kernel.IsDisposed) return;
            _kernel.Tick();
            UIBoot.Camera?.Tick();
            UIBoot.TickWorldHuds();
            _loadingIndicator.rectTransform.Rotate(0f, 0f, -240f * Time.unscaledDeltaTime);
            ApplyScreenMetrics(false);
            var visible = _kernel.VisibleInDrawOrder;
            for (var i = 0; i < visible.Count; i++)
            {
                var view = visible[i].Logic as UIView;
                if (view != null)
                    try { view.RefreshTree(); }
                    catch (Exception error) { _kernel.ReportViewFailure(visible[i], error); break; }
            }
        }

        private void OnDestroy()
        {
            if (UIBoot.Root == this) UIBoot.Shutdown();
            else _pool?.Dispose();
            if (_worldHudRoot != null)
            {
                if (Application.isPlaying) Destroy(_worldHudRoot.gameObject);
                else DestroyImmediate(_worldHudRoot.gameObject);
            }
        }

        private void HandleMaskClick()
        {
            if (_maskOwnerId != 0 && _kernel != null && !_kernel.IsDisposed)
                _kernel.Navigator.Close(_maskOwnerId);
        }

        private void ApplyScreenMetrics(bool force)
        {
            if (_screenFitter == null) return;
            var size = new Vector2Int(Screen.width, Screen.height);
            var safe = Screen.safeArea;
            var orientation = Screen.orientation;
            if (!force && size == _lastScreenSize && safe == _lastSafeArea && orientation == _lastOrientation
                && _fittersVersion == UIComponentIndex<UISafeAreaFitter>.Version) return;
            _fittersVersion = UIComponentIndex<UISafeAreaFitter>.Version;
            _lastScreenSize = size;
            _lastSafeArea = safe;
            _lastOrientation = orientation;
            _screenFitter.Apply(safe, size, UIComponentIndex<UISafeAreaFitter>.All, _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera);
            if (_letterbox != null) _letterbox.gameObject.SetActive(_screenFitMode == UIScreenFitMode.LetterboxHeight
                || _screenFitMode == UIScreenFitMode.LetterboxWidth || _screenFitMode == UIScreenFitMode.LetterboxAuto);
        }

        private void EnsureVisualTree()
        {
            if (_built) return;
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null) _scaler = gameObject.AddComponent<CanvasScaler>();
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            _canvas.renderMode = _canvas.worldCamera == null ? RenderMode.ScreenSpaceOverlay : RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = -10000;
            gameObject.layer = UIRenderLayer;

            if (_contentArea == null) _contentArea = CreateRoot("ContentArea", transform);
            else Fill(_contentArea);
            if (_letterbox == null) _letterbox = CreateImage("Letterbox", transform, Color.black, false);
            _letterbox.transform.SetAsFirstSibling();

            CreateLayerRoot(UILayer.Background, "Seg_Background", 1000);
            _windowRoot = CreateLayerRoot(UILayer.Window, "Seg_Window", 2000);
            _overlayRoot = CreateLayerRoot(UILayer.Tip, "Seg_Tip", 8000);
            CreateLayerRoot(UILayer.Info, "Seg_Info", 5000);
            CreateLayerRoot(UILayer.Guide, "Seg_Guide", 6000);
            CreateLayerRoot(UILayer.Top, "Seg_Top", 7000);
            CreateLayerRoot(UILayer.Drag, "Seg_Drag", 8500);
            CreateLayerRoot(UILayer.System, "Seg_System", 9000);
            CreateLayerRoot(UILayer.Blocker, "Seg_Blocker", 9900);
            CreateLayerRoot(UILayer.Letterbox, "Seg_Letterbox", 9990);

            if (_mask == null) _mask = CreateImage("ModalMask", _windowRoot, new Color(0f, 0f, 0f, 0.55f), true);
            _mask.raycastTarget = true;
            _maskCanvas = _mask.GetComponent<Canvas>();
            if (_maskCanvas == null) _maskCanvas = _mask.gameObject.AddComponent<Canvas>();
            _maskCanvas.overrideSorting = true;
            if (_mask.gameObject.GetComponent<GraphicRaycaster>() == null) _mask.gameObject.AddComponent<GraphicRaycaster>();
            _maskButton = _mask.GetComponent<Button>();
            if (_maskButton == null) _maskButton = _mask.gameObject.AddComponent<Button>();
            _maskButton.transition = Selectable.Transition.None;
            _maskButton.targetGraphic = _mask;
            _maskButton.onClick.RemoveListener(HandleMaskClick);
            _maskButton.onClick.AddListener(HandleMaskClick);

            if (_blocker == null) _blocker = CreateImage("InputBlocker", transform, new Color(0f, 0f, 0f, 0f), true);
            _blocker.raycastTarget = false;
            _blockerCanvas = _blocker.GetComponent<Canvas>();
            if (_blockerCanvas == null) _blockerCanvas = _blocker.gameObject.AddComponent<Canvas>();
            _blockerCanvas.overrideSorting = true;
            if (_blocker.gameObject.GetComponent<GraphicRaycaster>() == null) _blocker.gameObject.AddComponent<GraphicRaycaster>();

            if (_loadingIndicator == null) _loadingIndicator = CreateImage("LoadingIndicator", transform, Color.white, true);
            var loadingRect = _loadingIndicator.rectTransform;
            loadingRect.anchorMin = loadingRect.anchorMax = new Vector2(0.5f, 0.5f);
            loadingRect.pivot = new Vector2(0.5f, 0.5f);
            loadingRect.sizeDelta = new Vector2(48f, 48f);
            loadingRect.anchoredPosition = Vector2.zero;
            _loadingIndicator.raycastTarget = false;
            _loadingCanvas = _loadingIndicator.GetComponent<Canvas>();
            if (_loadingCanvas == null) _loadingCanvas = _loadingIndicator.gameObject.AddComponent<Canvas>();
            _loadingCanvas.overrideSorting = true;
            _loadingCanvas.sortingOrder = 10000;
            if (_loadingIndicator.gameObject.GetComponent<GraphicRaycaster>() == null) _loadingIndicator.gameObject.AddComponent<GraphicRaycaster>();

            var worldHud = transform.Find("UIWorldHudRoot") as RectTransform;
            if (worldHud == null)
            {
                var worldHudObject = new GameObject("UIWorldHudRoot", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                if (Application.isPlaying) DontDestroyOnLoad(worldHudObject);
                worldHud = worldHudObject.GetComponent<RectTransform>();
                worldHudObject.layer = 0;
                worldHud.sizeDelta = _referenceResolution;
                worldHudObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                worldHudObject.GetComponent<Canvas>().overrideSorting = true;
                worldHudObject.GetComponent<Canvas>().sortingOrder = 1000;
                worldHud.localScale = Vector3.one * 0.01f;
            }
            _worldHudRoot = worldHud;
            _worldHudCanvas = _worldHudRoot.GetComponent<Canvas>();
            if (_modelStageRoot == null)
            {
                var modelRootObject = new GameObject("UIModelStages");
                modelRootObject.transform.SetParent(transform, false);
                _modelStageRoot = modelRootObject.transform;
            }
            _screenFitter = new UIScreenFitter(_contentArea, _scaler, _referenceResolution, _screenFitMode);
            _built = true;
        }

        private RectTransform CreateLayerRoot(UILayer layer, string name, int order)
        {
            RectTransform root;
            if (!_layerRoots.TryGetValue(layer, out root))
            {
                root = FindRoot(name);
                if (root == null) root = CreateRoot(name, _contentArea);
                _layerRoots.Add(layer, root);
            }
            else root.SetParent(_contentArea, false);
            Fill(root);
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            if (root.GetComponent<GraphicRaycaster>() == null) root.gameObject.AddComponent<GraphicRaycaster>();
            return root;
        }

        private RectTransform FindRoot(string name)
        {
            var child = _contentArea.Find(name) as RectTransform;
            if (child == null) child = transform.Find(name) as RectTransform;
            return child;
        }

        private static RectTransform CreateRoot(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            Fill(rect);
            return rect;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image CreateImage(string name, Transform parent, Color color, bool fullScreen)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            if (fullScreen) Fill(rect);
            else
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
