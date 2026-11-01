using System.Collections.Generic;
using GameUI.Kernel;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private RectTransform _windowRoot;
        [SerializeField] private RectTransform _overlayRoot;
        [SerializeField] private Image _mask;
        [SerializeField] private Image _blocker;
        [SerializeField] private Image _loadingIndicator;
        [SerializeField] private Canvas _maskCanvas;

        private UIKernel _kernel;
        private UIViewPool _pool;

        public RectTransform WindowRoot { get { return _windowRoot; } }
        public RectTransform OverlayRoot { get { return _overlayRoot; } }
        public Image Mask { get { return _mask; } }
        public Image Blocker { get { return _blocker; } }
        public Image LoadingIndicator { get { return _loadingIndicator; } }
        public Canvas MaskCanvas { get { return _maskCanvas; } }

        public void Install(UIKernel kernel, UIViewPool pool)
        {
            _kernel = kernel;
            _pool = pool;
            EnsureVisualTree();
        }

        private void Awake() { EnsureVisualTree(); }

        private void LateUpdate()
        {
            if (_kernel == null || _kernel.IsDisposed) return;
            _kernel.Tick();
            var visible = _kernel.VisibleInDrawOrder;
            for (var i = 0; i < visible.Count; i++)
            {
                var view = visible[i].Logic as UIView;
                if (view != null) view.RefreshTree();
            }
        }

        private void OnDestroy()
        {
            _pool?.Dispose();
        }

        private void EnsureVisualTree()
        {
            if (_windowRoot != null) return;
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            _windowRoot = CreateRoot("Window");
            _overlayRoot = CreateRoot("Overlay");
            _mask = CreateImage("Mask", _windowRoot, new Color(0f, 0f, 0f, 0.55f));
            _maskCanvas = _mask.GetComponent<Canvas>();
            _blocker = CreateImage("Blocker", _windowRoot, new Color(0f, 0f, 0f, 0f));
            _blocker.raycastTarget = false;
            _loadingIndicator = CreateImage("LoadingIndicator", _windowRoot, new Color(1f, 1f, 1f, 0.7f));
            _mask.gameObject.SetActive(false);
            _blocker.gameObject.SetActive(false);
            _loadingIndicator.gameObject.SetActive(false);
        }

        private RectTransform CreateRoot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            return rect;
        }

        private Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
    }
}
