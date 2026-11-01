using System;
using System.Collections.Generic;

namespace GameUI.Runtime
{
    public static class UIMessageHub
    {
        private static class Channel<T> where T : struct
        {
            internal static readonly List<Action<T>> Handlers = new List<Action<T>>();
        }

        public static void Subscribe<T>(Action<T> handler, UIScope scope) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            Channel<T>.Handlers.Add(handler);
            scope.Add(delegate { Channel<T>.Handlers.Remove(handler); });
        }

        public static void Publish<T>(in T message) where T : struct
        {
            var handlers = Channel<T>.Handlers.ToArray();
            for (var i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](message); }
                catch (Exception error) { UnityEngine.Debug.LogException(error); }
            }
        }
    }
}
