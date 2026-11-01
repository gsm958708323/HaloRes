using System;
using Cysharp.Threading.Tasks;
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
        private readonly GraphicRaycaster[] _raycasters;
        private readonly bool[] _raycasterStates;
        private readonly IUIAssetHandle _asset;
        private readonly UISortingSlot[] _slots;
        private readonly Canvas[] _canvases;
        private readonly bool[] _canvasStates;
        private readonly Renderer[][] _slotRenderers;
        private readonly Renderer[] _renderers;
        private readonly bool[] _rendererStates;
        private readonly ParticleSystem[] _particles;
        private readonly bool[] _pausedParticles;
        private readonly float _shownAlpha;
        private readonly bool _shownInteractable;
        private readonly bool _shownBlocksRaycasts;
        private int _transition;
        private bool _destroyed;
        private bool _visible;
        private bool _visibilityApplied;

        public UGUIViewInstance(string viewId, GameObject gameObject, IUIViewLogic logic, IUIAssetHandle asset, bool keepAlive)
        {
            ViewId = viewId;
            _gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
            Logic = logic ?? throw new ArgumentNullException(nameof(logic));
            _asset = asset;
            KeepAlive = keepAlive;
            _canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            _shownAlpha = _group.alpha;
            _shownInteractable = _group.interactable;
            _shownBlocksRaycasts = _group.blocksRaycasts;
            _slots = gameObject.GetComponentsInChildren<UISortingSlot>(true);
            _slotRenderers = new Renderer[_slots.Length][];
            for (var i = 0; i < _slots.Length; i++)
            {
                _slotRenderers[i] = _slots[i].GetComponentsInChildren<Renderer>(true);
            }
            _canvases = gameObject.GetComponentsInChildren<Canvas>(true);
            _canvasStates = new bool[_canvases.Length];
            _raycasters = gameObject.GetComponentsInChildren<GraphicRaycaster>(true);
            _raycasterStates = new bool[_raycasters.Length];
            _renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            _rendererStates = new bool[_renderers.Length];
            _particles = gameObject.GetComponentsInChildren<ParticleSystem>(true);
            _pausedParticles = new bool[_particles.Length];
            var transitions = gameObject.GetComponent<UIViewTransitions>();
            _enter = transitions != null && transitions.enter != null ? (IUITransition)new UITimelineTransition(transitions.enter) : new UIInstantTransition();
            _exit = transitions != null && transitions.exit != null ? (IUITransition)new UITimelineTransition(transitions.exit) : new UIInstantTransition();
            SetVisible(false);
        }

        public IUIViewLogic Logic { get; private set; }
        public string ViewId { get; }
        public bool KeepAlive { get; private set; }
        public long LastUsed { get; private set; }
        public bool IsExiting { get; private set; }
        public bool HandoffRequested { get; set; }
        public bool IsTransitionDone { get { return _transition == 1 ? _enter.IsDone : _transition == 2 ? _exit.IsDone : true; } }
        public GameObject GameObject { get { return _gameObject; } }
        public IUIAssetHandle Asset { get { return _asset; } }

        public void SetBaseOrder(int baseOrder)
        {
            _canvas.sortingOrder = baseOrder + 1;
            for (var i = 0; i < _slots.Length; i++)
            {
                var order = baseOrder + 1 + Mathf.Clamp(_slots[i].offset, 1, 17);
                var childCanvas = _slots[i].GetComponent<Canvas>();
                if (childCanvas != null) { childCanvas.overrideSorting = true; childCanvas.sortingOrder = order; }
                for (var j = 0; j < _slotRenderers[i].Length; j++) _slotRenderers[i][j].sortingOrder = order;
            }
        }

        public void SetVisible(bool on)
        {
            if (_visibilityApplied && _visible == on) return;
            _visibilityApplied = true;
            _visible = on;
            if (on && !_gameObject.activeSelf) _gameObject.SetActive(true);
            for (var i = 0; i < _canvases.Length; i++)
            {
                if (!on) _canvasStates[i] = _canvases[i].enabled;
                _canvases[i].enabled = on && _canvasStates[i];
            }
            for (var i = 0; i < _raycasters.Length; i++)
            {
                if (!on) _raycasterStates[i] = _raycasters[i].enabled;
                _raycasters[i].enabled = on && _raycasterStates[i];
            }
            _group.alpha = on ? _shownAlpha : 0f;
            _group.interactable = on && _shownInteractable;
            _group.blocksRaycasts = on && _shownBlocksRaycasts;
            for (var i = 0; i < _renderers.Length; i++)
            {
                if (!on) _rendererStates[i] = _renderers[i].enabled;
                _renderers[i].enabled = on && _rendererStates[i];
            }
            for (var i = 0; i < _particles.Length; i++)
            {
                var particle = _particles[i];
                if (on) { if (_pausedParticles[i] && particle.isPaused) particle.Play(false); }
                else { _pausedParticles[i] = particle.isPlaying; if (_pausedParticles[i]) particle.Pause(false); }
            }
            if (!on && _transition == 2) { _exit.Stop(); _transition = 0; }
        }

        public void BeginEnter()
        {
            SetVisible(true);
            _transition = 1;
            _enter.Play();
        }

        public void BeginExit()
        {
            IsExiting = true;
            _transition = 2;
            _exit.Play();
        }

        public void CompleteTransition()
        {
            if (_transition == 1) { _enter.Finish(); _enter.Stop(); _transition = 0; }
            else if (_transition == 2) _exit.Finish();
        }

        internal void PrepareForPool()
        {
            SetVisible(false);
            _enter.Stop();
            _exit.Stop();
            _transition = 0;
            HandoffRequested = false;
            IsExiting = false;
        }

        internal void DestroyForPool()
        {
            if (_destroyed) return;
            _destroyed = true;
            try { DestroyLogic(); }
            finally
            {
                if (_gameObject != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(_gameObject);
                        if (_asset != null) ReleaseAssetAfterDestroy(_gameObject, _asset).Forget();
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(_gameObject);
                        _asset?.Release();
                    }
                }
                else _asset?.Release();
            }
        }

        internal void DestroyLogic()
        {
            var logic = Logic;
            try
            {
                if (logic is UINode node) node.HostDestroy();
                else if (logic is UIElement element) element.DestroyElement();
            }
            finally { Logic = null; }
        }

        internal void MarkUsed(long sequence) { LastUsed = sequence; }
        internal void RequestHandoff() { HandoffRequested = true; }
        internal void CancelHandoffRequest() { HandoffRequested = false; }

        private static async UniTaskVoid ReleaseAssetAfterDestroy(GameObject instance, IUIAssetHandle asset)
        {
            while (instance != null) await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            asset.Release();
        }
    }
}
