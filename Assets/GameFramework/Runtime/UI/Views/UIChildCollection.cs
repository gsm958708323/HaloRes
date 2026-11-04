using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameFramework.UI
{
    /// <summary>父视图内部的子视图选择与复用；子视图不进入根导航历史。</summary>
    internal sealed class UIChildCollection
    {
        private sealed class Request
        {
            internal UISubView View;
            internal bool Exclusive, Ready;
            internal Exception Error;
            internal object[] Arguments;
            internal CancellationTokenRegistration Cancellation;
            internal TaskCompletionSource<UISubView> Completion = new TaskCompletionSource<UISubView>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly UIGameView _owner;
        private readonly Dictionary<Type, UISubView> _views = new Dictionary<Type, UISubView>();
        private readonly List<UISubView> _selected = new List<UISubView>();
        private readonly List<Request> _requests = new List<Request>();
        internal UIChildCollection(UIGameView owner) { _owner = owner; }
        internal IEnumerable<UISubView> Selected => _selected;
        internal T Find<T>() where T : UISubView => _views.TryGetValue(typeof(T), out var child) ? (T)child : null;

        private UISubView Get(Type type)
        {
            if (_views.TryGetValue(type, out var view)) return view;
            UIChildDefinition binding = null;
            foreach (var child in _owner.Configuration.Children) if (child.ViewType == type) binding = child;
            if (binding == null) throw new ArgumentException("UI child is not configured for its parent: " + type);
            view = (UISubView)_owner.Runtime.CreateChildView(type, _owner, binding);
            _views.Add(type, view);
            return view;
        }
        internal async Task Prepare()
        {
            foreach (var binding in _owner.Configuration.Children)
                if (binding.ResType == ResType.Static || binding.LoadWithParent) await Get(binding.ViewType).LoadAsync();
        }
        internal void AwakePreloaded()
        {
            foreach (var binding in _owner.Configuration.Children)
                if (binding.LoadWithParent) Get(binding.ViewType).Awake();
        }

        internal Task<UISubView> Open(Type type, bool exclusive, object[] arguments, CancellationToken ct)
        {
            _owner.Runtime.AssertAlive();
            _owner.Lifetime.ThrowIfCancellationRequested();
            ct.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<UISubView>(TaskCreationOptions.RunContinuationsAsynchronously);
            _owner.Runtime.Enqueue(() =>
            {
                if (_owner.IsReleased) { completion.TrySetCanceled(); return; }
                try
                {
                    var request = new Request { View = Get(type), Exclusive = exclusive,
                        Arguments = arguments == null ? Array.Empty<object>() : (object[])arguments.Clone(), Completion = completion };
                    _requests.Add(request);
                    if (ct.CanBeCanceled) request.Cancellation = ct.Register(() => _owner.Runtime.Post(() => Cancel(request)));
                    PrepareRequest(request);
                }
                catch (Exception error) { completion.TrySetException(error); }
            });
            return completion.Task;
        }
        private async void PrepareRequest(Request request)
        {
            Exception error = null;
            try { await request.View.LoadAsync(); }
            catch (Exception failure) { error = failure; }
            _owner.Runtime.Enqueue(() => { request.Ready = true; request.Error = error; Commit(); });
        }
        private void Commit()
        {
            // 按请求顺序提交；较早的子视图尚未准备好时，后续完成项必须等待。
            while (_requests.Count > 0 && _requests[0].Ready && !_owner.IsReleased)
            {
                var request = _requests[0];
                _requests.RemoveAt(0);
                request.Cancellation.Dispose();
                try
                {
                    if (request.Error != null) throw request.Error;
                    request.View.Awake();
                    if (_owner.IsReleased || _owner.Runtime.IsDisposed) { request.Completion.TrySetCanceled(); continue; }
                    request.View.Start(request.Arguments);
                    if (_owner.IsReleased || _owner.Runtime.IsDisposed) { request.Completion.TrySetCanceled(); continue; }
                    // 目标子视图成功启用后才替换旧选择，加载或启用失败不会影响当前界面。
                    if (_owner.IsVisible) request.View.Enable();
                    if (request.Exclusive)
                    {
                        foreach (var previous in _selected.ToArray()) if (previous != request.View) UIView.Cleanup(previous.Disable);
                        _selected.Clear();
                    }
                    if (!_selected.Contains(request.View)) _selected.Add(request.View);
                    _owner.Runtime.Notify(() => request.Completion.TrySetResult(request.View));
                }
                catch (Exception error)
                {
                    if (!_selected.Contains(request.View)) Release(request.View);
                    request.Completion.TrySetException(error);
                }
            }
        }
        private void Cancel(Request request)
        {
            if (!_requests.Remove(request)) return;
            request.Cancellation.Dispose();
            request.Completion.TrySetCanceled();
            // 只有选中状态和等待队列都不再引用该实例时才释放它。
            if (!_selected.Contains(request.View) && !_requests.Exists(other => other.View == request.View)) Release(request.View);
            Commit();
        }
        internal Task Hide(Type type, bool remove)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _owner.Runtime.Enqueue(() =>
            {
                try
                {
                    foreach (var request in _requests.ToArray()) if (request.View.GetType() == type) Cancel(request);
                    if (_views.TryGetValue(type, out var view))
                    {
                        _selected.Remove(view);
                        view.Disable();
                        if (remove) Release(view);
                    }
                    _owner.Runtime.Notify(() => done.TrySetResult(true));
                }
                catch (Exception error) { done.TrySetException(error); }
            });
            return done.Task;
        }
        private void Release(UISubView view)
        {
            _selected.Remove(view);
            if (_views.TryGetValue(view.GetType(), out var current) && current == view) _views.Remove(view.GetType());
            view.Release();
            // 静态节点仍由父 Prefab 所有；先还原节点，再让新控制器重新捕获原始显示状态。
            if (!_owner.IsReleased && view.Binding.ResType == ResType.Static)
            {
                var replacement = Get(view.GetType());
                UIRuntime.Observe(replacement.LoadAsync());
            }
        }
        internal void Enable()
        {
            foreach (var view in _selected.ToArray()) view.Enable();
        }
        internal void Disable()
        {
            foreach (var view in _selected.ToArray()) UIView.Cleanup(view.Disable);
        }
        internal void Dispose()
        {
            foreach (var request in _requests.ToArray())
            {
                request.Cancellation.Dispose();
                request.Completion.TrySetCanceled();
            }
            _requests.Clear();
            foreach (var view in new List<UISubView>(_views.Values)) view.Release();
            _views.Clear();
            _selected.Clear();
        }
    }
}
