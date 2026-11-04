using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameFramework.UI
{
    public class UIGameView : UIView
    {
        private UIChildCollection _children;
        private readonly List<UIItem> _items = new List<UIItem>();
        private UIChildCollection Children => _children ?? (_children = new UIChildCollection(this));
        protected virtual void CloseSelf() => Manager.Close(this);
        public T FindChild<T>() where T : UISubView => Children.Find<T>();
        internal Task PrepareChildren() => Children.Prepare();

        public async Task<T> OpenChildAsync<T>(object[] args = null, CancellationToken ct = default) where T : UISubView
            => (T)await Children.Open(typeof(T), false, args, ct);
        public async Task<T> OpenOneChildUIAsync<T>(object[] args = null, CancellationToken ct = default) where T : UISubView
            => (T)await Children.Open(typeof(T), true, args, ct);
        public void OpenChild<T>(params object[] args) where T : UISubView => UIRuntime.Observe(OpenChildAsync<T>(args));
        public void OpenOneChildUI<T>(params object[] args) where T : UISubView => UIRuntime.Observe(OpenOneChildUIAsync<T>(args));
        public void CloseChild<T>() where T : UISubView => CloseChild(typeof(T));
        public void CloseChild(Type type) => UIRuntime.Observe(CloseChildAsync(type));
        public Task CloseChildAsync(Type type) { CheckAlive(); return Children.Hide(type, false); }
        public Task RemoveChildAsync<T>() where T : UISubView { CheckAlive(); return Children.Hide(typeof(T), true); }

        public T CreateItem<T>(GameObject template, Transform parent = null, params object[] args) where T : UIItem, new()
        {
            CheckAlive();
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!template.transform.IsChildOf(transform))
                throw new ArgumentException("Template items must belong to their owning view's prefab. Use CreateItemAsync for another asset.");
            if (parent != null && parent != transform && !parent.IsChildOf(transform))
                throw new ArgumentException("An item parent must belong to its owning view.", nameof(parent));
            var item = NewItem<T>();
            try
            {
                item.Attach(UnityEngine.Object.Instantiate(template, parent != null ? parent : transform), true);
                item.Awake();
                item.Start(args);
                if (IsVisible) item.Enable();
                Runtime.Enqueue(() => { });
                return item;
            }
            catch { _items.Remove(item); item.Release(); throw; }
        }
        public async Task<T> CreateItemAsync<T>(string address, Transform parent = null, object[] args = null,
            CancellationToken ct = default) where T : UIItem, new()
        {
            CheckAlive();
            if (parent != null && parent != transform && !parent.IsChildOf(transform))
                throw new ArgumentException("An item parent must belong to its owning view.", nameof(parent));
            var item = NewItem<T>();
            try
            {
                var instance = await item.Assets.InstantiateAsync(address, parent != null ? parent : transform, ct);
                CheckAlive();
                item.Attach(instance);
                item.Awake();
                item.Start(args);
                if (IsVisible) item.Enable();
                Runtime.Enqueue(() => { });
                return item;
            }
            catch { _items.Remove(item); item.Release(); throw; }
        }
        private T NewItem<T>() where T : UIItem, new()
        {
            var item = new T { Parent = this };
                item.Bind(Manager, Runtime, null, LayerType, Assets.CreateChild(typeof(T).Name));
            _items.Add(item);
            return item;
        }
        public void DestroyItem()
        {
            foreach (var item in _items.ToArray()) item.Release();
            _items.Clear();
        }
        internal override void Awake()
        {
            base.Awake();
            Children.AwakePreloaded();
        }
        internal override void Enable()
        {
            var changed = !IsVisible;
            base.Enable();
            if (IsReleased || !changed) return;
            Children.Enable();
            foreach (var item in _items.ToArray()) item.Enable();
        }
        internal override void Disable()
        {
            if (!IsVisible) return;
            Children.Disable();
            foreach (var item in _items.ToArray()) Cleanup(item.Disable);
            base.Disable();
        }
        internal override int SortingCount
        {
            get
            {
                // 数量与分配使用同一视图树：只计入当前选中的子视图及其 Item。
                var count = base.SortingCount;
                foreach (var item in _items) count += item.SortingCount;
                foreach (var child in Children.Selected) count += child.SortingCount;
                return count;
            }
        }
        internal override void SetCanvasOrder(ref int order)
        {
            base.SetCanvasOrder(ref order);
            foreach (var item in _items) item.SetCanvasOrder(ref order);
            foreach (var child in Children.Selected) child.SetCanvasOrder(ref order);
        }
        private void CheckAlive() { Runtime.AssertAlive(); Lifetime.ThrowIfCancellationRequested(); }
        protected override void ReleaseOwnedViews()
        {
            _children?.Dispose();
            DestroyItem();
        }
    }
}
