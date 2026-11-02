using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.UI
{
    public sealed class UILayer
    {
        private readonly List<UINavigationEntry> _entries = new List<UINavigationEntry>();
        internal UILayer(LayerType type, Canvas canvas) { LayerType = type; Canvas = canvas; }
        public LayerType LayerType { get; }
        public Canvas Canvas { get; }
        public IReadOnlyList<UIHandle> History => _entries.ConvertAll(entry => entry.Handle).AsReadOnly();
        public int PendingCount => _entries.FindAll(entry => !entry.Committed).Count;
        public int Count => _entries.Count;
        internal IReadOnlyList<UINavigationEntry> Entries => _entries;
        internal void Add(UINavigationEntry entry) => _entries.Add(entry);
        internal void Remove(UINavigationEntry entry) => _entries.Remove(entry);

        internal UINavigationEntry FindLast(Type type = null)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
                if (type == null || _entries[i].Handle.ViewType == type) return _entries[i];
            return null;
        }

        internal void CommitReady(UIManager manager)
        {
            foreach (var entry in _entries.ToArray())
            {
                if (!_entries.Contains(entry)) continue;
                if (entry.Committed) continue;
                if (entry.Record.Error != null) { manager.Remove(entry, entry.Record.Error); continue; }
                if (!entry.Record.Ready) break;
                try
                {
                    entry.Record.View.Awake();
                    if (manager.IsDisposed) return;
                    if (entry.Record.Active == null)
                    {
                        entry.Record.Active = entry;
                        entry.Record.View.SelectNavigation(entry);
                    }
                    entry.Record.View.Start(entry.Arguments);
                    if (manager.IsDisposed) return;
                    entry.Committed = true;
                    entry.Handle.State = UINavigationState.Open;
                }
                catch (Exception error) { manager.Fault(entry.Record, error); }
                if (manager.IsDisposed) return;
            }
        }

        internal List<UINavigationEntry> VisibleEntries()
        {
            var result = new List<UINavigationEntry>();
            foreach (var entry in _entries)
            {
                if (!entry.Committed) continue;
                if (!entry.Record.View.Configuration.IsPopup) result.Clear();
                // A singleton popup can occur more than once in history, but is drawn only at its latest position.
                result.RemoveAll(previous => previous.Record == entry.Record);
                result.Add(entry);
            }
            return result;
        }
    }
}
