using System;
using System.Collections.Generic;
using GameUI.Components;
using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UIItemList<TItem, TData> : IUINodeChild, IUIItemHost where TItem : UIItem<TData>
    {
        private readonly UIBindingData _template;
        private readonly Func<TItem> _factory;
        private readonly List<TItem> _items = new List<TItem>();
        private readonly List<TData> _data = new List<TData>();
        private bool _open;

        public UIItemList(UINode host, UIBindingData template, Func<TItem> factory)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _template = template ?? throw new ArgumentNullException(nameof(template));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _template.gameObject.SetActive(false);
            host.AddChild(this);
        }

        public event Action<int, int, int> ItemEvent;
        public int Count { get { return _data.Count; } }

        public void SetItems(IReadOnlyList<TData> data)
        {
            _data.Clear();
            if (data != null) for (var i = 0; i < data.Count; i++) _data.Add(data[i]);

            for (var i = 0; i < _data.Count; i++)
            {
                var item = GetOrCreate(i);
                item.GameObject.SetActive(true);
                if (_open) item.Bind(i, _data[i]);
            }

            for (var i = _data.Count; i < _items.Count; i++)
            {
                _items[i].Unbind();
                _items[i].GameObject.SetActive(false);
            }
        }

        public void RefreshAt(int index)
        {
            if (!_open || index < 0 || index >= _data.Count || index >= _items.Count) return;
            _items[index].Bind(index, _data[index]);
        }

        private TItem GetOrCreate(int index)
        {
            if (index < _items.Count) return _items[index];

            var itemObject = UnityEngine.Object.Instantiate(_template.gameObject, _template.transform.parent, false);
            itemObject.name = _template.gameObject.name + "[Item]";
            itemObject.SetActive(true);
            try
            {
                var item = _factory();
                if (item == null) throw new InvalidOperationException("UI item factory returned null.");
                item.BindHost(this);
                item.Initialize(itemObject);
                itemObject.SetActive(false);
                _items.Add(item);
                return item;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(itemObject);
                else UnityEngine.Object.DestroyImmediate(itemObject);
                throw;
            }
        }

        void IUIItemHost.OnItemEvent(int index, int code, int bindingVersion)
        {
            ItemEvent?.Invoke(index, code, bindingVersion);
        }

        void IUINodeChild.HostOpen()
        {
            _open = true;
            for (var i = 0; i < _data.Count; i++)
            {
                var item = GetOrCreate(i);
                item.GameObject.SetActive(true);
                item.Bind(i, _data[i]);
            }
        }

        void IUINodeChild.HostVisibilityChanged(bool visible) { }
        void IUINodeChild.HostRefresh() { }

        void IUINodeChild.HostClose()
        {
            _open = false;
            Exception failure = null;
            for (var i = 0; i < _items.Count; i++)
                try { _items[i].Unbind(); }
                catch (Exception error) { if (failure == null) failure = error; else Debug.LogException(error); }
            if (failure != null) throw failure;
        }

        void IUINodeChild.HostDestroy()
        {
            Exception failure = null;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var itemObject = item.GameObject;
                try { item.DestroyElement(); }
                catch (Exception error) { if (failure == null) failure = error; else Debug.LogException(error); }
                finally
                {
                    if (itemObject != null)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(itemObject);
                        else UnityEngine.Object.DestroyImmediate(itemObject);
                    }
                }
            }
            _items.Clear();
            _data.Clear();
            if (failure != null) throw failure;
        }

        bool IUINodeChild.HostBack() { return false; }
    }
}
