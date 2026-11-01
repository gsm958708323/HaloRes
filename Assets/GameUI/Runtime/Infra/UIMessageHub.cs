using System;
using System.Collections.Generic;
using System.Threading;

namespace GameUI.Runtime
{
    public static class UIMessageHub
    {
        private static readonly Queue<Action> Dispatches = new Queue<Action>();
        private static bool _dispatching;
        private static int _mainThread;

        private static class Channel<T> where T : struct
        {
            private sealed class Subscription
            {
                internal readonly Action<T> Handler;
                internal bool Active=true;
                internal Subscription(Action<T> handler) { Handler=handler; }
            }
            private static readonly List<Subscription> Handlers=new List<Subscription>();
            private static readonly Queue<T> Pending=new Queue<T>();
            private static readonly Action DispatchAction=DispatchOne;

            internal static void Subscribe(Action<T> handler,UIScope scope)
            {
                var subscription=new Subscription(handler);
                scope.Add(delegate { subscription.Active=false; if (!_dispatching) Compact(); });
                Handlers.Add(subscription);
            }
            internal static void Publish(in T message) { Pending.Enqueue(message); Dispatches.Enqueue(DispatchAction); }
            private static void DispatchOne()
            {
                var message=Pending.Dequeue();
                var count=Handlers.Count;
                for (var i=0;i<count;i++)
                {
                    var subscription=Handlers[i];
                    if (!subscription.Active) continue;
                    try { subscription.Handler(message); } catch (Exception error) { UnityEngine.Debug.LogException(error); }
                }
                Compact();
            }
            private static void Compact()
            {
                for (var i=Handlers.Count-1;i>=0;i--) if (!Handlers[i].Active) Handlers.RemoveAt(i);
            }
        }

        internal static void CaptureMainThread() { _mainThread=Thread.CurrentThread.ManagedThreadId; }
        private static void CheckThread()
        {
            if (_mainThread == 0) CaptureMainThread();
            if (_mainThread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("UI messages must run on the Unity main thread.");
        }

        public static void Subscribe<T>(Action<T> handler,UIScope scope) where T : struct
        {
            CheckThread();
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (scope.IsDisposed) throw new ObjectDisposedException(nameof(scope));
            Channel<T>.Subscribe(handler,scope);
        }
        public static void Publish<T>(in T message) where T : struct
        {
            CheckThread();
            Channel<T>.Publish(message);
            if (_dispatching) return;
            _dispatching=true;
            try { while (Dispatches.Count>0) Dispatches.Dequeue()(); }
            finally { _dispatching=false; }
        }
    }
}
