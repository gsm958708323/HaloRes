using System;
using System.Collections.Generic;
using GameUI.Components;
using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UIItemList<TItem, TData> : IUINodeChild, IUIItemHost where TItem : UIItem<TData>
    {
        private readonly UINode _host;
        private readonly Func<TItem> _factory;
        private readonly List<TItem> _items = new List<TItem>();
        private readonly List<TData> _data = new List<TData>();
        private bool _open;

        public UIItemList(UINode host, UIBindingData template, Func<TItem> factory)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public event Action<int, int> ItemEvent;
        public int Count { get { return _data.Count; } }

        public void SetItems(IReadOnlyList<TData> data)
        {
            _data.Clear();
            if (data != null) for (var i = 0; i < data.Count; i++) _data.Add(data[i]);
            Rebuild();
        }

        public void RefreshAt(int index)
        {
            if (index < 0 || index >= _items.Count || index >= _data.Count) return;
            _items[index].Bind(index, _data[index]);
        }

        private void Rebuild()
        {
            for (var i = 0; i < _items.Count; i++) _items[i].Unbind();
            _items.Clear();
            for (var i = 0; i < _data.Count; i++)
            {
                var item = _factory();
                item.BindHost(this);
                _items.Add(item);
                if (_open) item.Bind(i, _data[i]);
            }
        }

        void IUIItemHost.OnItemEvent(int index, int code) { ItemEvent?.Invoke(index, code); }
        void IUINodeChild.HostOpen() { _open = true; for (var i = 0; i < _items.Count; i++) _items[i].Bind(i, _data[i]); }
        void IUINodeChild.HostVisibilityChanged(bool visible) { }
        void IUINodeChild.HostRefresh() { }
        void IUINodeChild.HostClose() { _open = false; for (var i = 0; i < _items.Count; i++) _items[i].Unbind(); }
        void IUINodeChild.HostDestroy() { for (var i = 0; i < _items.Count; i++) _items[i].DestroyElement(); _items.Clear(); }
        bool IUINodeChild.HostBack() { return false; }
    }
}
