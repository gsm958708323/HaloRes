using System;
using GameUI.Components;
using GameUI.Kernel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    public sealed class UGUIViewInstance : IUIViewInstance
    {
        private readonly GameObject _gameObject;
        private readonly IUITransition _enter;
        private readonly IUITransition _exit;
        private readonly Canvas _canvas;
        private readonly CanvasGroup _group;
        private readonly GraphicRaycaster _raycaster;
        private readonly IUIAssetHandle _asset;
        private bool _visible;

        public UGUIViewInstance(string viewId, GameObject gameObject, IUIViewLogic logic, IUIAssetHandle asset)
        {
            ViewId = viewId;
            _gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
            Logic = logic ?? throw new ArgumentNullException(nameof(logic));
            _asset = asset;
            _canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _raycaster = gameObject.GetComponent<GraphicRaycaster>() ?? gameObject.AddComponent<GraphicRaycaster>();
            var transitions = gameObject.GetComponent<UIViewTransitions>();
            _enter = transitions != null && transitions.enter != null ? (IUITransition)new UITimelineTransition(transitions.enter) : new UIInstantTransition();
            _exit = transitions != null && transitions.exit != null ? (IUITransition)new UITimelineTransition(transitions.exit) : new UIInstantTransition();
            SetVisible(false);
        }

        public IUIViewLogic Logic { get; }
        public string ViewId { get; }
        public bool HandoffRequested { get; set; }
        public bool IsTransitionDone { get { return _enter.IsDone && _exit.IsDone; } }
        public GameObject GameObject { get { return _gameObject; } }
        public IUIAssetHandle Asset { get { return _asset; } }

        public void SetBaseOrder(int baseOrder)
        {
            _canvas.sortingOrder = baseOrder + 1;
            var slots = _gameObject.GetComponentsInChildren<UISortingSlot>(true);
            for (var i = 0; i < slots.Length; i++)
            {
                var childCanvas = slots[i].GetComponent<Canvas>() ?? slots[i].gameObject.AddComponent<Canvas>();
                childCanvas.overrideSorting = true;
                childCanvas.sortingOrder = baseOrder + Mathf.Clamp(slots[i].offset, 1, 19);
            }
        }

        public void SetVisible(bool on)
        {
            _visible = on;
            _gameObject.SetActive(on);
            _group.alpha = on ? 1f : 0f;
            _group.interactable = on;
            _group.blocksRaycasts = on;
            if (on) _exit.Stop();
        }

        public void BeginEnter()
        {
            SetVisible(true);
            _enter.Play();
        }

        public void BeginExit()
        {
            _exit.Play();
        }

        public void CompleteTransition()
        {
            _enter.Finish();
            _exit.Finish();
        }

        internal void PrepareForPool()
        {
            HandoffRequested = false;
            _enter.Stop();
            _exit.Stop();
            SetVisible(false);
        }

        internal void DestroyForPool()
        {
            if (Logic is UIElement element) element.DestroyElement();
            if (_gameObject != null) UnityEngine.Object.Destroy(_gameObject);
            _asset?.Release();
        }
    }
}
