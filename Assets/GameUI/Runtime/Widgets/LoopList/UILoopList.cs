using System;
using System.Collections.Generic;
using GameUI.Components;

namespace GameUI.Runtime
{
    public sealed class UILoopList<TItem, TData> : IUINodeChild, IUIItemHost where TItem : UIItem<TData>
    {
        private readonly Func<TItem> _factory;
        private readonly List<TData> _data = new List<TData>();
        private TItem _item;
        private bool _open;
        private int _index;

        public UILoopList(UINode host, UIBindingData template, Func<TItem> factory) { _factory = factory ?? throw new ArgumentNullException(nameof(factory)); }
        public event Action<int, int> ItemEvent;
        public event Action<int> ItemEnteredView;

        public void SetItems(IReadOnlyList<TData> data)
        {
            _data.Clear();
            if (data != null) for (var i = 0; i < data.Count; i++) _data.Add(data[i]);
            RefreshAt(_index);
        }

        public void ScrollTo(int index)
        {
            if (_data.Count == 0) return;
            _index = ((index % _data.Count) + _data.Count) % _data.Count;
            RefreshAt(_index);
        }

        public void RefreshAt(int index)
        {
            if (_data.Count == 0) return;
            _index = ((index % _data.Count) + _data.Count) % _data.Count;
            if (_item == null) { _item = _factory(); _item.BindHost(this); }
            if (_open) _item.Bind(_index, _data[_index]);
            ItemEnteredView?.Invoke(_index);
        }

        void IUIItemHost.OnItemEvent(int index, int code) { ItemEvent?.Invoke(index, code); }
        void IUINodeChild.HostOpen() { _open = true; RefreshAt(_index); }
        void IUINodeChild.HostVisibilityChanged(bool visible) { if (!visible) _item?.Unbind(); else RefreshAt(_index); }
        void IUINodeChild.HostRefresh() { }
        void IUINodeChild.HostClose() { _open = false; _item?.Unbind(); }
        void IUINodeChild.HostDestroy() { _item?.DestroyElement(); _item = null; }
        bool IUINodeChild.HostBack() { return false; }
    }
}
