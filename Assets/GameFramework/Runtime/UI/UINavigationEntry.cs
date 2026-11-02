using System;
using System.Threading;

namespace GameFramework.UI
{
    internal sealed class UINavigationEntry
    {
        internal UIHandle Handle;
        internal UIViewRecord Record;
        internal object[] Arguments;
        internal object SavedState;
        internal bool HasSavedState;
        internal bool Committed;
        internal CancellationTokenRegistration Cancellation;
    }

    /// <summary>Owns one root view, its shared preparation, and all navigation references to it.</summary>
    internal sealed class UIViewRecord
    {
        internal UIGameView View;
        internal int References;
        internal bool Ready;
        internal Exception Error;
        internal UINavigationEntry Active;
    }
}
