using System;
using System.Collections.Generic;
using GameUI.Components;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    public sealed class UILoopList<TItem, TData> : IUINodeChild, IUIItemHost where TItem : UIItem<TData>
    {
        private sealed class Slot
        {
            internal TItem Item;
            internal int DataIndex = -1;
            internal int DataVersion = -1;
        }

        private readonly Func<TItem> _factory;
        private readonly ScrollRect _scrollRect;
        private readonly UIBindingData _template;
        private readonly RectTransform _content;
        private readonly List<TData> _data = new List<TData>();
        private readonly List<float> _prefix = new List<float>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly UnityEngine.Events.UnityAction<Vector2> _scrollHandler;
        private readonly bool _horizontal;
        private readonly float _spacing;
        private readonly Func<TData, float> _sizeOf;
        private readonly float _fallbackSize;
        private float _viewportExtent;
        private int _visibleStart;
        private int _visibleEnd;
        private bool _open;
        private bool _visible;
        private bool _dirty = true;
        private int _dataVersion;

        public UILoopList(UINode host, ScrollRect scrollRect, UIBindingData template, Func<TItem> factory,
            float spacing = 0f, Func<TData, float> sizeOf = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _scrollRect = scrollRect != null ? scrollRect : throw new ArgumentNullException(nameof(scrollRect));
            _template = template != null ? template : throw new ArgumentNullException(nameof(template));
            _content = scrollRect.content != null ? scrollRect.content : throw new ArgumentException("ScrollRect.content is required.", nameof(scrollRect));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _horizontal = scrollRect.horizontal;
            if (!_horizontal && !scrollRect.vertical) throw new ArgumentException("ScrollRect must scroll in at least one direction.", nameof(scrollRect));
            _spacing = Mathf.Max(0f, spacing);
            _content.pivot = _horizontal ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 1f);
            _content.anchorMin = _horizontal ? Vector2.zero : new Vector2(0f, 1f);
            _content.anchorMax = _horizontal ? new Vector2(0f, 1f) : Vector2.one;
            _template.gameObject.SetActive(false);
            _sizeOf = sizeOf;
            var rect = template.transform as RectTransform;
            _fallbackSize = Mathf.Max(1f, rect == null ? 100f : (_horizontal ? rect.rect.width : rect.rect.height));
            _scrollHandler = OnScrollChanged;
            scrollRect.onValueChanged.AddListener(_scrollHandler);
            host.AddChild(this);
        }

        public event Action<int, int, int> ItemEvent;
        public event Action<int> ItemEnteredView;
        public int Count { get { return _data.Count; } }

        public void SetItems(IReadOnlyList<TData> data)
        {
            _data.Clear();
            _dataVersion++;
            if (data != null) for (var i = 0; i < data.Count; i++) _data.Add(data[i]);
            RebuildPrefix();
            UpdateContentSize();
            _dirty = true;
            RefreshVisible(true);
        }

        public void ScrollTo(int index)
        {
            if (_data.Count == 0) return;
            index = Mathf.Clamp(index, 0, _data.Count - 1);
            var offset = _prefix[index];
            var total = Mathf.Max(1f, _prefix[_prefix.Count - 1]);
            if (_horizontal) _content.anchoredPosition = new Vector2(-offset, _content.anchoredPosition.y);
            else _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, offset);
            if (_horizontal && _scrollRect.horizontal) _scrollRect.horizontalNormalizedPosition = Mathf.Clamp01(offset / Mathf.Max(1f, total - _viewportExtent));
            else if (!_horizontal && _scrollRect.vertical) _scrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(offset / Mathf.Max(1f, total - _viewportExtent));
            _dirty = true;
            RefreshVisible(true);
        }

        public void RefreshAt(int index)
        {
            if (index < 0 || index >= _data.Count) return;
            if (_sizeOf != null)
            {
                RebuildPrefix();
                UpdateContentSize();
                _dirty = true;
                for (var i = 0; i < _slots.Count; i++)
                    if (_slots[i].DataIndex >= 0) SetItemPosition(_slots[i].Item.Rect, _slots[i].DataIndex);
            }
            if (!_open || !_visible) return;
            for (var i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].DataIndex != index) continue;
                _slots[i].Item.Bind(index, _data[index]);
                break;
            }
            if (_dirty) RefreshVisible(true);
        }

        private void OnScrollChanged(Vector2 value)
        {
            _dirty = true;
        }

        private void RebuildPrefix()
        {
            _prefix.Clear();
            _prefix.Add(0f);
            for (var i = 0; i < _data.Count; i++)
            {
                var extent = _sizeOf == null ? _fallbackSize : Mathf.Max(1f, _sizeOf(_data[i]));
                _prefix.Add(_prefix[i] + extent + _spacing);
            }
        }

        private void UpdateContentSize()
        {
            var size = _content.sizeDelta;
            var total = _data.Count == 0 ? 0f : Mathf.Max(0f, _prefix[_prefix.Count - 1] - _spacing);
            if (_horizontal) size.x = total; else size.y = total;
            _content.sizeDelta = size;
        }

        private void RefreshVisible(bool force)
        {
            if (!_open || !_visible || _scrollRect.viewport == null || _data.Count == 0)
            {
                if (_data.Count == 0) UnbindAll();
                return;
            }
            if (!_dirty && !force) return;
            _dirty = false;
            _viewportExtent = Mathf.Max(1f, _horizontal ? _scrollRect.viewport.rect.width : _scrollRect.viewport.rect.height);
            var offset = Mathf.Max(0f, _horizontal ? -_content.anchoredPosition.x : _content.anchoredPosition.y);
            var firstVisible = Mathf.Clamp(FindIndex(offset), 0, _data.Count - 1);
            var visibleEnd = Mathf.Clamp(FindIndex(offset + _viewportExtent - 0.001f) + 1, firstVisible + 1, _data.Count);
            var oldStart = _visibleStart;
            var oldEnd = _visibleEnd;
            _visibleStart = firstVisible;
            _visibleEnd = visibleEnd;
            var desiredStart = Mathf.Max(0, firstVisible - 1);
            var desiredEnd = Mathf.Min(_data.Count, visibleEnd + 1);
            var required = desiredEnd - desiredStart;
            EnsureCapacity(required);

            for (var slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
            {
                var slot = _slots[slotIndex];
                if (slot.DataIndex < desiredStart || slot.DataIndex >= desiredEnd)
                {
                    slot.Item.Unbind();
                    slot.DataIndex = -1;
                    slot.Item.GameObject.SetActive(false);
                }
            }
            for (var dataIndex = desiredStart; dataIndex < desiredEnd; dataIndex++)
            {
                Slot slot = null;
                for (var i = 0; i < _slots.Count; i++) if (_slots[i].DataIndex == dataIndex) { slot = _slots[i]; break; }
                if (slot == null)
                    for (var i = 0; i < _slots.Count; i++) if (_slots[i].DataIndex < 0) { slot = _slots[i]; break; }
                if (slot.DataIndex != dataIndex || slot.DataVersion != _dataVersion)
                {
                    slot.Item.GameObject.SetActive(true);
                    SetItemPosition(slot.Item.Rect, dataIndex);
                    slot.Item.Bind(dataIndex, _data[dataIndex]);
                    slot.DataIndex = dataIndex;
                    slot.DataVersion = _dataVersion;
                }
            }
            for (var index = firstVisible; index < visibleEnd; index++)
                if (index < oldStart || index >= oldEnd) ItemEnteredView?.Invoke(index);
        }

        private int FindIndex(float offset)
        {
            var low = 0;
            var high = _data.Count;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (_prefix[mid + 1] <= offset) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        private void EnsureCapacity(int capacity)
        {
            while (_slots.Count < capacity)
            {
                var itemObject = UnityEngine.Object.Instantiate(_template.gameObject, _content, false);
                itemObject.name = _template.gameObject.name + "[LoopItem]";
                itemObject.SetActive(true);
                try
                {
                    var item = _factory();
                    if (item == null) throw new InvalidOperationException("UI item factory returned null.");
                    item.BindHost(this);
                    item.Initialize(itemObject);
                    itemObject.SetActive(false);
                    _slots.Add(new Slot { Item = item });
                }
                catch
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(itemObject);
                    else UnityEngine.Object.DestroyImmediate(itemObject);
                    throw;
                }
            }
        }

        private void SetItemPosition(RectTransform rect, int index)
        {
            if (rect == null) return;
            if (_horizontal)
            {
                rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(_prefix[index], 0f);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, 0f);
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -_prefix[index]);
                rect.sizeDelta = new Vector2(0f, rect.sizeDelta.y);
            }
            if (_sizeOf != null)
            {
                var size = rect.sizeDelta;
                if (_horizontal) size.x = Mathf.Max(1f, _sizeOf(_data[index]));
                else size.y = Mathf.Max(1f, _sizeOf(_data[index]));
                rect.sizeDelta = size;
            }
        }

        private void UnbindAll()
        {
            _visibleStart = _visibleEnd = 0;
            Exception failure = null;
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                try { slot.Item.Unbind(); }
                catch (Exception error) { if (failure == null) failure = error; else Debug.LogException(error); }
                finally
                {
                    slot.DataIndex = -1;
                    slot.Item.GameObject.SetActive(false);
                }
            }
            if (failure != null) throw failure;
        }

        void IUIItemHost.OnItemEvent(int index, int code, int bindingVersion)
        {
            ItemEvent?.Invoke(index, code, bindingVersion);
        }

        void IUINodeChild.HostOpen() { _open = true; _dirty = true; }
        void IUINodeChild.HostVisibilityChanged(bool visible)
        {
            _visible = visible;
            if (!visible) UnbindAll();
            else { _dirty = true; RefreshVisible(true); }
        }
        void IUINodeChild.HostRefresh()
        {
            var viewport = _scrollRect.viewport;
            if (viewport != null && Mathf.Abs((_horizontal ? viewport.rect.width : viewport.rect.height) - _viewportExtent) > 0.01f) _dirty = true;
            if (_dirty) RefreshVisible(false);
        }
        void IUINodeChild.HostClose() { _open = false; _visible = false; UnbindAll(); }
        void IUINodeChild.HostDestroy()
        {
            _scrollRect.onValueChanged.RemoveListener(_scrollHandler);
            Exception failure = null;
            for (var i = 0; i < _slots.Count; i++)
            {
                var item = _slots[i].Item;
                var gameObject = item.GameObject;
                try { item.DestroyElement(); }
                catch (Exception error) { if (failure == null) failure = error; else Debug.LogException(error); }
                finally
                {
                    if (gameObject != null)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(gameObject);
                        else UnityEngine.Object.DestroyImmediate(gameObject);
                    }
                }
            }
            _slots.Clear();
            _data.Clear();
            _prefix.Clear();
            if (failure != null) throw failure;
        }
        bool IUINodeChild.HostBack() { return false; }
    }
}
