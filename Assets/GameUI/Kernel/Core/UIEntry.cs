using System;
using Cysharp.Threading.Tasks;

namespace GameUI.Kernel
{
    internal sealed class UIEntry
    {
        internal UIEntry(int id, UIViewInfo info, UIArgs args, string source)
        {
            Id = id;
            Info = info;
            Want = new UIDesired { Wanted = true, Args = args, Source = source };
            Act = new UIActual { Phase = UIPhase.Pending };
            Opened = new UniTaskCompletionSource<UIOpenResult>();
            Closed = new UniTaskCompletionSource<UICloseReason>();
            Result = new UniTaskCompletionSource<UIResultBox>();
        }

        internal readonly int Id;
        internal readonly UIViewInfo Info;
        internal UIDesired Want;
        internal UIActual Act;
        internal readonly UniTaskCompletionSource<UIOpenResult> Opened;
        internal readonly UniTaskCompletionSource<UICloseReason> Closed;
        internal readonly UniTaskCompletionSource<UIResultBox> Result;
        internal bool OpenCompleted;
        internal string LastSource;

        internal bool IsAlive
        {
            get { return Act.Phase != UIPhase.Closed; }
        }
    }

    internal struct UIDesired
    {
        internal bool Wanted;
        internal int ReplacedBy;
        internal UIExitIntent Intent;
        internal UIArgs Args;
        internal object Result;
        internal bool HasResult;
        internal string Source;
    }

    internal struct UIActual
    {
        internal UIPhase Phase;
        internal IUIViewInstance Instance;
        internal IUILoadOp<IUIViewInstance> Load;
        internal UICloseReason Reason;
        internal double LoadStart;
        internal bool EnteredThisSession;
        internal bool OpenAttempted;
        internal bool ResumeAttempted;
        internal bool HasContext;
        internal string LastContext;
    }

    internal sealed class UIEntryTable
    {
        internal readonly System.Collections.Generic.List<UIEntry> Stack = new System.Collections.Generic.List<UIEntry>();
        internal readonly System.Collections.Generic.List<UIEntry> Overlays = new System.Collections.Generic.List<UIEntry>();
        internal readonly System.Collections.Generic.Dictionary<int, UIEntry> ById = new System.Collections.Generic.Dictionary<int, UIEntry>();
    }
}
