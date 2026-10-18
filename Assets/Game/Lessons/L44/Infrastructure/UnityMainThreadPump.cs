using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Game.AssetSystem.Infrastructure.L44
{
    [DefaultExecutionOrder(-10000)]
    public sealed class UnityMainThreadPump : MonoBehaviour
    {
        private static UnityMainThreadPump _instance;
        private static int _mainThreadId;

        private readonly ConcurrentQueue<Action> _queue =
            new ConcurrentQueue<Action>();

        public static int MainThreadId
        {
            get { return _mainThreadId; }
        }

        public static bool IsMainThread
        {
            get
            {
                return _mainThreadId != 0 &&
                       Thread.CurrentThread.ManagedThreadId == _mainThreadId;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            if (_instance != null)
            {
                return;
            }

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            var go = new GameObject("[UnityMainThreadPump]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<UnityMainThreadPump>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        public static void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_instance == null)
            {
                throw new InvalidOperationException(
                    "UnityMainThreadPump is not initialized.");
            }

            _instance._queue.Enqueue(action);
        }

        private void Update()
        {
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
