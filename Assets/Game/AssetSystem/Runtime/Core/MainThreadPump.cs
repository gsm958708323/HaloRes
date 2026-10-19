using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Game.AssetSystem.Core
{
    public enum PumpPriority
    {
        High = 0,
        Normal = 1,
        Low = 2
    }

    /// <summary>
    /// Single main-thread dispatcher for the whole framework.
    ///
    /// Three priority queues (High / Normal / Low) drained under a per-frame
    /// action budget and a per-frame time budget, so a burst of completions can
    /// never stall a frame. Auto-created before the first scene loads, so every
    /// layer (loading, scheduling, bundles, assets, integration) shares it.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class MainThreadPump : MonoBehaviour
    {
        private static MainThreadPump _instance;
        private static int _mainThreadId;

        private readonly ConcurrentQueue<Action>[] _queues =
        {
            new ConcurrentQueue<Action>(),
            new ConcurrentQueue<Action>(),
            new ConcurrentQueue<Action>()
        };

        private int _cursor;
        private int _lastFrameProcessed;

        public int MaxActionsPerFrame = 64;
        public float MaxMillisecondsPerFrame = 2.0f;

        public static MainThreadPump Instance
        {
            get { return _instance; }
        }

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

        public int LastFrameProcessed
        {
            get { return _lastFrameProcessed; }
        }

        public int PendingCount
        {
            get
            {
                var count = 0;

                for (var i = 0; i < _queues.Length; i++)
                {
                    count += _queues[i].Count;
                }

                return count;
            }
        }

        /// <summary>
        /// Clears the singleton when a new play session starts. With "Enter Play
        /// Mode Options" configured to skip the domain reload, a stale reference
        /// would otherwise survive and <see cref="Post"/> would enqueue into a
        /// dead instance.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _mainThreadId = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            if (EnsureLiveInstance())
            {
                return;
            }

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            var go = new GameObject("[MainThreadPump]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThreadPump>();
        }

        /// <summary>
        /// True when a usable instance already exists. A leftover object from a
        /// previous play session (domain reload disabled) can still be alive but
        /// inactive, which would swallow every posted action.
        /// </summary>
        private static bool EnsureLiveInstance()
        {
            if (_instance == null)
            {
                return false;
            }

            if (_instance.isActiveAndEnabled)
            {
                return true;
            }

            UnityEngine.Object.Destroy(_instance.gameObject);
            _instance = null;

            return false;
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

        public static void Post(
            Action action,
            PumpPriority priority = PumpPriority.Normal)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_instance == null)
            {
                throw new InvalidOperationException(
                    "MainThreadPump is not initialized. " +
                    "It must be initialized before Post is used.");
            }

            var index = (int)priority;

            if (index < 0 || index >= _instance._queues.Length)
            {
                index = (int)PumpPriority.Normal;
            }

            _instance._queues[index].Enqueue(action);
        }

        /// <summary>
        /// Throws when the caller is not on the Unity main thread. Used by the
        /// layers that must never touch engine objects off-thread.
        /// </summary>
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
            DrainFrame();
        }

        public void DrainFrame()
        {
            var processed = 0;
            var startTime = Time.realtimeSinceStartup;
            var maxMilliseconds = Math.Max(0.0f, MaxMillisecondsPerFrame);

            while (processed < MaxActionsPerFrame)
            {
                if (maxMilliseconds > 0.0f &&
                    (Time.realtimeSinceStartup - startTime) * 1000.0f >= maxMilliseconds)
                {
                    break;
                }

                if (!TryDequeue(out var action))
                {
                    break;
                }

                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }

                processed++;
            }

            _lastFrameProcessed = processed;
        }

        private bool TryDequeue(out Action action)
        {
            for (var i = 0; i < _queues.Length; i++)
            {
                var priority = (_cursor + i) % _queues.Length;

                if (_queues[priority].TryDequeue(out action))
                {
                    _cursor = (priority + 1) % _queues.Length;
                    return true;
                }
            }

            action = null;
            return false;
        }
    }
}
