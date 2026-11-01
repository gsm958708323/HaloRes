using System;
using System.Collections.Generic;

namespace GameUI.Kernel
{
    internal sealed class UIFrame
    {
        internal readonly Dictionary<int, UITarget> Targets = new Dictionary<int, UITarget>();
        internal readonly Dictionary<int, int> BaseOrders = new Dictionary<int, int>();
        internal readonly List<UIEntry> VisibleInDrawOrder = new List<UIEntry>();
        internal readonly List<UIEntry> BrokenChains = new List<UIEntry>();
        internal UIPresentation Presentation;

        internal void Clear()
        {
            Targets.Clear();
            BaseOrders.Clear();
            VisibleInDrawOrder.Clear();
            BrokenChains.Clear();
        }
    }

    internal static class UIResolver
    {
        private const int WindowOrderStart = 2000;
        private const int SlotWidth = 20;
        private const double LoadingIndicatorDelay = 0.2;

        public static void Resolve(UIEntryTable table, double now, bool externalLocked, UIFrame frame)
        {
            frame.Clear();
            for (var i = 0; i < table.Stack.Count; i++)
            {
                var entry = table.Stack[i];
                frame.Targets[entry.Id] = UITarget.Absent;
                frame.BaseOrders[entry.Id] = WindowOrderStart + i * SlotWidth;
            }

            var activeStack = new List<UIEntry>();
            for (var i = 0; i < table.Stack.Count; i++)
            {
                var entry = table.Stack[i];
                if ((entry.Want.Wanted || entry.Want.ReplacedBy != 0) && entry.Act.Phase < UIPhase.Exiting) activeStack.Add(entry);
            }

            var worldCovered = false;
            string topContext = null;
            var firstReadyFullScreen = -1;
            for (var i = activeStack.Count - 1; i >= 0; i--)
            {
                var entry = activeStack[i];
                if (!IsReady(entry)) continue;
                if (entry.Info.Mode == UIMode.FullScreen)
                {
                    firstReadyFullScreen = table.Stack.IndexOf(entry);
                    worldCovered = entry.Info.CoversWorld;
                    topContext = entry.Info.Context;
                    break;
                }
            }

            for (var i = 0; i < activeStack.Count; i++)
            {
                var entry = activeStack[i];
                var stackIndex = table.Stack.IndexOf(entry);
                frame.Targets[entry.Id] = firstReadyFullScreen >= 0 && stackIndex < firstReadyFullScreen
                    ? UITarget.Hidden : UITarget.Shown;
                ApplySuccessorHold(entry, table, frame);
            }

            var popupMaskVisible = false;
            var popupMaskOrder = 0;
            var popupMaskOwner = 0;
            var popupMaskStyle = UIMaskStyle.None;
            for (var i = 0; i < table.Stack.Count; i++)
            {
                var entry = table.Stack[i];
                if (entry.Want.Wanted && entry.Info.Mode == UIMode.Popup && IsPresented(entry, frame.Targets))
                {
                    popupMaskVisible = true;
                    popupMaskOrder = frame.BaseOrders[entry.Id];
                    popupMaskOwner = entry.Id;
                    if ((int)entry.Info.MaskStyle > (int)popupMaskStyle) popupMaskStyle = entry.Info.MaskStyle;
                }
            }

            var systemVisible = false;
            var systemOrder = 0;
            var loading = false;
            var transitionLocked = false;
            for (var i = 0; i < table.Stack.Count; i++)
            {
                var entry = table.Stack[i];
                if (entry.Want.Wanted && entry.Act.Phase == UIPhase.Loading)
                {
                    transitionLocked = true;
                    if (now - entry.Act.LoadStart >= LoadingIndicatorDelay) loading = true;
                }
                if (entry.Act.Phase == UIPhase.Entering || entry.Act.Phase == UIPhase.Exiting) transitionLocked = true;
                if (entry.Act.Instance != null && entry.Act.Phase != UIPhase.Hidden && entry.Act.Phase != UIPhase.Loading)
                    frame.VisibleInDrawOrder.Add(entry);
            }

            for (var i = 0; i < table.Overlays.Count; i++)
            {
                var entry = table.Overlays[i];
                frame.BaseOrders[entry.Id] = LayerOrder(entry.Info.Layer) + i * SlotWidth;
                frame.Targets[entry.Id] = ResolveOverlayTarget(entry, worldCovered, topContext);
                if (entry.Want.Wanted && entry.Info.Layer == UILayer.System && frame.Targets[entry.Id] == UITarget.Shown)
                {
                    systemVisible = true;
                    systemOrder = frame.BaseOrders[entry.Id];
                }
                if (entry.Want.Wanted && entry.Act.Phase == UIPhase.Loading)
                {
                    transitionLocked = true;
                    if (now - entry.Act.LoadStart >= LoadingIndicatorDelay) loading = true;
                }
                if (entry.Act.Phase == UIPhase.Entering || entry.Act.Phase == UIPhase.Exiting) transitionLocked = true;
                if (entry.Act.Instance != null && entry.Act.Phase != UIPhase.Hidden && entry.Act.Phase != UIPhase.Loading)
                    frame.VisibleInDrawOrder.Add(entry);
            }

            frame.Presentation = new UIPresentation(
                worldCovered,
                topContext,
                externalLocked || transitionLocked || systemVisible,
                loading,
                popupMaskVisible,
                popupMaskOrder,
                popupMaskStyle,
                popupMaskOwner,
                systemVisible,
                systemOrder);

            for (var i = 0; i < table.Stack.Count; i++) CheckBrokenChain(table.Stack[i], table, frame);
            for (var i = 0; i < table.Overlays.Count; i++) CheckBrokenChain(table.Overlays[i], table, frame);
        }

        private static void CheckBrokenChain(UIEntry entry, UIEntryTable table, UIFrame frame)
        {
            if (entry.Want.ReplacedBy == 0 || entry.Want.Wanted || entry.Act.Phase >= UIPhase.Exiting) return;
            UIEntry successor;
            if (!table.ById.TryGetValue(entry.Want.ReplacedBy, out successor) || successor.Act.Phase == UIPhase.Closed)
                frame.BrokenChains.Add(entry);
        }

        private static void ApplySuccessorHold(UIEntry entry, UIEntryTable table, UIFrame frame)
        {
            if (entry.Want.Wanted || entry.Want.ReplacedBy == 0) return;
            UIEntry successor;
            if (!table.ById.TryGetValue(entry.Want.ReplacedBy, out successor) || successor.Act.Phase == UIPhase.Closed) return;
            var successorStable = successor.Act.Phase == UIPhase.Shown;
            if (successorStable)
            {
                frame.Targets[entry.Id] = UITarget.Absent;
                return;
            }

            switch (entry.Act.Phase)
            {
                case UIPhase.Shown:
                case UIPhase.Entering:
                case UIPhase.Exiting:
                    frame.Targets[entry.Id] = UITarget.Shown;
                    break;
                case UIPhase.Hidden:
                    frame.Targets[entry.Id] = UITarget.Hidden;
                    break;
            }
        }

        private static bool IsReady(UIEntry entry)
        {
            return entry.Act.Phase == UIPhase.Hidden || entry.Act.Phase == UIPhase.Entering || entry.Act.Phase == UIPhase.Shown;
        }

        private static bool IsPresented(UIEntry entry, Dictionary<int, UITarget> targets)
        {
            UITarget target;
            if (!targets.TryGetValue(entry.Id, out target) || target != UITarget.Shown) return false;
            return entry.Act.Phase == UIPhase.Entering || entry.Act.Phase == UIPhase.Shown || entry.Act.Phase == UIPhase.Exiting;
        }

        private static UITarget ResolveOverlayTarget(UIEntry entry, bool worldCovered, string topContext)
        {
            if (!entry.Want.Wanted || entry.Act.Phase >= UIPhase.Exiting) return UITarget.Absent;
            switch (entry.Info.Visibility)
            {
                case UIOverlayVisibility.WhenWorldVisible: return worldCovered ? UITarget.Hidden : UITarget.Shown;
                case UIOverlayVisibility.WithContext:
                    return !string.IsNullOrEmpty(topContext) && (string.IsNullOrEmpty(entry.Info.ContextTag) || topContext.IndexOf(entry.Info.ContextTag, StringComparison.Ordinal) >= 0)
                        ? UITarget.Shown : UITarget.Hidden;
                default: return UITarget.Shown;
            }
        }

        private static int LayerOrder(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.Background: return 1000;
                case UILayer.Info: return 5000;
                case UILayer.Guide: return 6000;
                case UILayer.Top: return 7000;
                case UILayer.Tip: return 8000;
                case UILayer.Drag: return 8500;
                case UILayer.System: return 9000;
                case UILayer.Blocker: return 9900;
                case UILayer.Letterbox: return 9990;
                default: return 2000;
            }
        }
    }
}
