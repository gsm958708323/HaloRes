using System;
using System.Collections.Generic;

namespace GameFramework.UI
{
    internal sealed class UINavigationSnapshot
    {
        internal UINavigationSnapshot(long revision, NavigationViewState[] views, NavigationViewState[] visibleViews)
        {
            Revision = revision;
            Views = Array.AsReadOnly(views);
            VisibleViews = Array.AsReadOnly(visibleViews);
        }

        internal long Revision { get; }
        internal IReadOnlyList<NavigationViewState> Views { get; }
        internal IReadOnlyList<NavigationViewState> VisibleViews { get; }

        internal bool TryGet(Type viewType, out NavigationViewState state)
        {
            for (var i = 0; i < Views.Count; i++)
                if (Views[i].ViewType == viewType) { state = Views[i]; return true; }
            state = default;
            return false;
        }
    }

    internal readonly struct NavigationViewState
    {
        internal NavigationViewState(Type viewType, LayerType layer, long activeEntryId,
            object[] arguments, object savedState, bool hasSavedState, bool visible)
        {
            ViewType = viewType;
            Layer = layer;
            ActiveEntryId = activeEntryId;
            Arguments = Array.AsReadOnly(arguments ?? Array.Empty<object>());
            SavedState = savedState;
            HasSavedState = hasSavedState;
            IsVisible = visible;
        }

        internal Type ViewType { get; }
        internal LayerType Layer { get; }
        internal long ActiveEntryId { get; }
        internal IReadOnlyList<object> Arguments { get; }
        internal object SavedState { get; }
        internal bool HasSavedState { get; }
        internal bool IsVisible { get; }
    }
}
