using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Game.AssetSystem.Integration.L51
{
    [DefaultExecutionOrder(-10000)]
    public sealed class IntegrationMainThreadPump : MonoBehaviour
    {
        private static IntegrationMainThreadPump _instance;
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
            var go = new GameObject("[IntegrationMainThreadPump]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<IntegrationMainThreadPump>();
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
                    "IntegrationMainThreadPump is not initialized.");
            }

            _instance._queue.Enqueue(action);
        }

        public static void EnsureMainThread(string caller)
        {
            if (!IsMainThread)
            {
                throw new InvalidOperationException(
                    caller + " must be called on the Unity main thread.");
            }
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
