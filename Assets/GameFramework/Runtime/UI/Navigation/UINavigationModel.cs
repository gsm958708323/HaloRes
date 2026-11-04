using System;
using System.Collections.Generic;

namespace GameFramework.UI
{
    /// <summary>纯 C# 导航状态：只维护访问记录及其派生结果，不加载资源或调用 Unity API。</summary>
    internal sealed class UINavigationModel
    {
        private readonly SortedDictionary<LayerType, List<UINavigationEntry>> _layers =
            new SortedDictionary<LayerType, List<UINavigationEntry>>();
        private long _revision;

        internal long Revision => _revision;
        internal int Count(LayerType layer) => GetLayer(layer).Count;
        internal IReadOnlyList<long> History(LayerType layer)
        {
            var entries = GetLayer(layer);
            var ids = new long[entries.Count];
            for (var i = 0; i < ids.Length; i++) ids[i] = entries[i].Id;
            return Array.AsReadOnly(ids);
        }

        internal UINavigationEntry Add(long id, UIViewDefinition definition, object[] arguments)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (Find(id) != null) throw new InvalidOperationException("Duplicate navigation id: " + id);
            var entry = new UINavigationEntry {
                Id = id,
                ViewType = definition.ViewType,
                Layer = definition.LayerType,
                IsPopup = definition.IsPopup,
                Arguments = arguments == null ? Array.Empty<object>() : (object[])arguments.Clone(),
                Status = NavigationEntryStatus.Loading
            };
            GetLayer(entry.Layer).Add(entry);
            _revision++;
            return entry;
        }

        internal UINavigationEntry Find(long id)
        {
            foreach (var entries in _layers.Values)
                for (var i = 0; i < entries.Count; i++) if (entries[i].Id == id) return entries[i];
            return null;
        }

        internal UINavigationEntry FindLast(LayerType layer, Type viewType = null)
        {
            var entries = GetLayer(layer);
            for (var i = entries.Count - 1; i >= 0; i--)
                if (viewType == null || entries[i].ViewType == viewType) return entries[i];
            return null;
        }

        internal void MarkReady(Type viewType)
        {
            var changed = false;
            foreach (var entries in _layers.Values)
                foreach (var entry in entries)
                    if (entry.ViewType == viewType && entry.Status == NavigationEntryStatus.Loading)
                    { entry.Status = NavigationEntryStatus.Ready; changed = true; }
            if (changed) _revision++;
        }

        internal List<UINavigationEntry> CommitReadyInOrder()
        {
            var committed = new List<UINavigationEntry>();
            foreach (var entries in _layers.Values)
            {
                foreach (var entry in entries)
                {
                    if (entry.Status == NavigationEntryStatus.Open) continue;
                    // 同层按请求顺序提交；较早的 Loading 记录会阻止后续记录越序打开。
                    if (entry.Status == NavigationEntryStatus.Loading) break;
                    if (entry.Status != NavigationEntryStatus.Ready) continue;
                    entry.Status = NavigationEntryStatus.Open;
                    committed.Add(entry);
                }
            }
            if (committed.Count > 0) _revision++;
            return committed;
        }

        internal List<UINavigationEntry> FailView(Type viewType)
        {
            var removed = new List<UINavigationEntry>();
            foreach (var entries in _layers.Values)
                for (var i = entries.Count - 1; i >= 0; i--)
                    if (entries[i].ViewType == viewType)
                    {
                        entries[i].Status = NavigationEntryStatus.Failed;
                        removed.Add(entries[i]);
                        entries.RemoveAt(i);
                    }
            if (removed.Count > 0) _revision++;
            return removed;
        }

        internal UINavigationEntry Close(long id)
        {
            foreach (var entries in _layers.Values)
                for (var i = entries.Count - 1; i >= 0; i--)
                    if (entries[i].Id == id)
                    {
                        var entry = entries[i];
                        entry.Status = NavigationEntryStatus.Closed;
                        entries.RemoveAt(i);
                        _revision++;
                        return entry;
                    }
            return null;
        }

        internal List<UINavigationEntry> RemoveAll()
        {
            var removed = new List<UINavigationEntry>();
            foreach (var entries in _layers.Values)
            {
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    entries[i].Status = NavigationEntryStatus.Closed;
                    removed.Add(entries[i]);
                }
                entries.Clear();
            }
            if (removed.Count > 0) _revision++;
            return removed;
        }

        internal UINavigationEntry Pop(LayerType layer) => Close(FindLast(layer)?.Id ?? 0);

        internal bool SaveState(long id, object state)
        {
            var entry = Find(id);
            if (entry == null) return false;
            entry.SavedState = state;
            entry.HasSavedState = true;
            return true;
        }

        internal UINavigationSnapshot CreateSnapshot()
        {
            var visible = new List<UINavigationEntry>();
            foreach (var pair in _layers)
            {
                var entries = pair.Value;
                var lastFullscreen = 0;
                for (var i = entries.Count - 1; i >= 0; i--)
                    if (entries[i].Status == NavigationEntryStatus.Open && !entries[i].IsPopup)
                    { lastFullscreen = i; break; }

                // 全屏界面截断同层背景；同一共享实例的重复访问只保留最上方那条可见记录。
                var layerVisible = new List<UINavigationEntry>();
                for (var i = lastFullscreen; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry.Status != NavigationEntryStatus.Open) continue;
                    layerVisible.RemoveAll(previous => previous.ViewType == entry.ViewType);
                    layerVisible.Add(entry);
                }
                visible.AddRange(layerVisible);
            }

            var allTypes = new List<Type>();
            foreach (var pair in _layers)
                foreach (var entry in pair.Value)
                    if (entry.Status == NavigationEntryStatus.Open && !allTypes.Contains(entry.ViewType)) allTypes.Add(entry.ViewType);

            var states = new NavigationViewState[allTypes.Count];
            for (var i = 0; i < allTypes.Count; i++)
            {
                var type = allTypes[i];
                UINavigationEntry active = null;
                for (var j = 0; j < visible.Count; j++) if (visible[j].ViewType == type) active = visible[j];
                if (active == null)
                    foreach (var entries in _layers.Values)
                        for (var j = entries.Count - 1; j >= 0; j--)
                            if (entries[j].ViewType == type && entries[j].Status == NavigationEntryStatus.Open)
                            { active = entries[j]; break; }
                states[i] = new NavigationViewState(type, active.Layer, active.Id, active.Arguments,
                    active.SavedState, active.HasSavedState, visible.Exists(entry => entry.ViewType == type));
            }

            var visibleStates = new NavigationViewState[visible.Count];
            for (var i = 0; i < visible.Count; i++)
            {
                var entry = visible[i];
                visibleStates[i] = new NavigationViewState(entry.ViewType, entry.Layer, entry.Id,
                    entry.Arguments, entry.SavedState, entry.HasSavedState, true);
            }
            return new UINavigationSnapshot(_revision, states, visibleStates);
        }

        private List<UINavigationEntry> GetLayer(LayerType layer)
        {
            if (!_layers.TryGetValue(layer, out var entries)) _layers.Add(layer, entries = new List<UINavigationEntry>());
            return entries;
        }
    }
}
