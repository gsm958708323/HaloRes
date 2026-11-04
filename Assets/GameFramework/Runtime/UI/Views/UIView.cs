using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFramework.Asset.Scope;
using UnityEngine;

namespace GameFramework.UI
{
    public class UIView
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _visibility;
        private UIViewPresentation _presentation;
        private Task _loadTask;
        private bool _awakened, _started, _releasing, _manualInstance;
        private IReadOnlyList<object> _arguments = Array.AsReadOnly(Array.Empty<object>());
        public GameObject gameObject { get; private set; }
        public Transform transform => gameObject == null ? null : gameObject.transform;
        public RectTransform rectTransform => transform as RectTransform;
        public UIManager Manager { get; private set; }
        internal UIRuntime Runtime { get; private set; }
        public UIViewDefinition Configuration { get; private set; }
        public LayerType LayerType { get; private set; }
        public AssetScope Assets { get; private set; }
        public UIState UIState { get; private set; }
        public int SortingOrder { get; private set; }
        public bool IsVisible => UIState == UIState.Enable;
        public bool IsReleased => _releasing || UIState == UIState.Release;
        public CancellationToken Lifetime { get; private set; }
        internal bool IsAwakened => _awakened;
        internal bool IsStarted => _started;
        public CancellationToken VisibilityToken => _visibility?.Token ?? new CancellationToken(true);
        public IReadOnlyList<object> Arguments => _arguments;
        public UIHandle ActiveNavigation { get; private set; }
        internal virtual int SortingCount => _presentation?.SortingCount ?? 0;

        internal void Bind(UIManager manager, UIRuntime runtime, UIViewDefinition definition, LayerType layer, AssetScope scope)
        {
            Manager = manager; Runtime = runtime; Configuration = definition; LayerType = layer; Assets = scope;
            Lifetime = _lifetime.Token;
        }
        // 并发访问共享同一个准备任务，避免重复实例化或重复加载同一视图。
        internal Task LoadAsync() => _loadTask ?? (_loadTask = LoadCore());
        private async Task LoadCore()
        {
            Lifetime.ThrowIfCancellationRequested();
            UIState = UIState.Loading;
            GameObject instance;
            if (this is UISubView child && child.Parent != null && child.Binding.ResType == ResType.Static)
                instance = child.Parent.transform.Find(child.Binding.NodePath)?.gameObject
                    ?? throw new InvalidOperationException("Static UI NodePath was not found: " + child.Binding.NodePath);
            else instance = await Assets.InstantiateAsync(Configuration.PrefabAddress, Runtime.GetCanvas(LayerType).transform, Lifetime);
            Lifetime.ThrowIfCancellationRequested();
            Attach(instance);
            if (this is UIGameView parent) await parent.PrepareChildren();
        }
        internal void Attach(GameObject instance, bool manualInstance = false)
        {
            gameObject = instance != null ? instance : throw new ArgumentNullException(nameof(instance));
            _manualInstance = manualInstance;
            _presentation = new UIViewPresentation(instance, Configuration?.Children);
            instance.SetActive(true);
        }
        internal virtual void Awake()
        {
            Lifetime.ThrowIfCancellationRequested();
            if (_awakened) return;
            _awakened = true;
            UIState = UIState.Awake;
            OnAwake();
        }
        internal void Start(object[] args)
        {
            Lifetime.ThrowIfCancellationRequested();
            if (_started) return;
            _started = true;
            _arguments = Array.AsReadOnly(args ?? Array.Empty<object>());
            UIState = UIState.Start;
            OnStart(args ?? Array.Empty<object>());
        }
        internal void SelectNavigation(UIHandle handle, object[] arguments, object savedState, bool hasSavedState)
        {
            ActiveNavigation = handle;
            _arguments = Array.AsReadOnly(arguments ?? Array.Empty<object>());
            RestoreNavigationState(hasSavedState ? savedState : null);
        }
        internal object CaptureState() => CaptureNavigationState();
        protected virtual object CaptureNavigationState() => null;
        protected virtual void RestoreNavigationState(object state) { }

        internal virtual void Enable()
        {
            if (IsReleased || !_started || IsVisible) return;
            UIState = UIState.Enable;
            _visibility = new CancellationTokenSource();
            _presentation?.SetVisible(true);
            OnEnable();
        }
        internal virtual void Disable()
        {
            if (!IsVisible) return;
            UIState = UIState.Disable;
            try
            {
                Cleanup(() => _visibility?.Cancel());
                OnDisable();
            }
            finally
            {
                _visibility?.Dispose();
                _visibility = null;
                _presentation?.SetVisible(false);
            }
        }
        internal virtual void SetCanvasOrder(ref int order)
        {
            SortingOrder = order;
            _presentation?.Assign(ref order);
        }
        internal void Release()
        {
            if (IsReleased) return;
            _releasing = true;
            // 先取消实例任务并隐藏，再释放子对象、触发生命周期清理并归还 AssetScope。
            Cleanup(_lifetime.Cancel);
            Cleanup(Disable);
            Cleanup(ReleaseOwnedViews);
            if (_awakened) { UIState = UIState.Destroy; Cleanup(OnDestroy); }
            if (this is UISubView child && child.Parent != null && child.Binding.ResType == ResType.Static)
                Cleanup(() => _presentation?.RestoreBorrowed());
            Cleanup(() => Assets?.Dispose());
            if (_manualInstance && gameObject != null) UnityEngine.Object.Destroy(gameObject);
            gameObject = null;
            _presentation = null;
            ActiveNavigation = null;
            _arguments = Array.AsReadOnly(Array.Empty<object>());
            UIState = UIState.Release;
            _lifetime.Dispose();
        }
        protected virtual void ReleaseOwnedViews() { }
        protected virtual void OnAwake() { }
        protected virtual void OnStart(object[] args) { }
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        protected virtual void OnDestroy() { }
        internal static void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
