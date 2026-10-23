using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    /// Ordering contract (this is the part that used to be accidental):
    /// 1. Higher priority always drains before lower priority.
    /// 2. Within one priority the order is exactly the enqueue order (FIFO).
    /// 3. The only exception is <see cref="PumpBudget.LowReserveEvery"/>: every
    ///    Nth slot of a frame goes to Low so it cannot starve.
    ///
    /// The previous implementation rotated High -> Normal -> Low with a cursor
    /// that survived across frames. That made "which one wins" depend on where
    /// the cursor happened to be, so a High posted later could overtake a Low
    /// posted earlier by anything between 0 and 2 slots. Priority was really
    /// "one third of the budget", and two actions posted to different queues
    /// had no defined relative order.
    ///
    /// Lifecycle transitions (complete / cancel / dispose) must go through
    /// <see cref="PostTerminal"/> so both sides of a race land in the same
    /// queue: the winner is then decided by enqueue order, not by queue
    /// assignment. The task state machines still guard themselves with
    /// first-writer-wins, so pump order decides who is *processed* first, not
    /// who is *allowed* to win.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class MainThreadPump : MonoBehaviour
    {
        private struct WorkItem
        {
            public Action Action;
            public long Sequence;
        }

        private static MainThreadPump _instance;
        private static int _mainThreadId;

        private readonly ConcurrentQueue<WorkItem>[] _queues =
        {
            new ConcurrentQueue<WorkItem>(),
            new ConcurrentQueue<WorkItem>(),
            new ConcurrentQueue<WorkItem>()
        };

        /// <summary>
        /// Actions parked for the next frame. Kept separate from the priority
        /// queues so "reschedule me next frame" cannot be drained again in the
        /// frame that asked for it — a self-rescheduling poll in a queue would
        /// otherwise spin until the action cap ran out.
        /// </summary>
        private readonly ConcurrentQueue<Action> _nextFrame =
            new ConcurrentQueue<Action>();

        private readonly PumpBudget _budget = new PumpBudget();

        private long _sequence;
        private int _effectiveActionCap;
        private int _lastFrameProcessed;
        private double _lastFrameMilliseconds;
        private double _longestActionMilliseconds;
        private int _overBudgetFrames;
        private long _totalPosted;
        private long _totalDrained;

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

        public PumpBudget Budget
        {
            get { return _budget; }
        }

        /// <summary>Kept for call sites that tune the pump directly.</summary>
        public int MaxActionsPerFrame
        {
            get { return _budget.MaxActionsPerFrame; }
            set { _budget.MaxActionsPerFrame = Math.Max(1, value); }
        }

        /// <summary>Kept for call sites that tune the pump directly.</summary>
        public float MaxMillisecondsPerFrame
        {
            get { return _budget.MaxMillisecondsPerFrame; }
            set { _budget.MaxMillisecondsPerFrame = value; }
        }

        public int LastFrameProcessed
        {
            get { return _lastFrameProcessed; }
        }

        public double LastFrameMilliseconds
        {
            get { return _lastFrameMilliseconds; }
        }

        /// <summary>
        /// Cost of the most expensive single action in the last frame. A value
        /// above the frame budget means one action blew it, which the
        /// between-actions check cannot prevent.
        /// </summary>
        public double LongestActionMilliseconds
        {
            get { return _longestActionMilliseconds; }
        }

        public int OverBudgetFrames
        {
            get { return _overBudgetFrames; }
        }

        public long TotalPosted
        {
            get { return Interlocked.Read(ref _totalPosted); }
        }

        public long TotalDrained
        {
            get { return Interlocked.Read(ref _totalDrained); }
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

        public int GetPendingCount(PumpPriority priority)
        {
            var index = (int)priority;

            if (index < 0 || index >= _queues.Length)
            {
                return 0;
            }

            return _queues[index].Count;
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
            EnsureExists();
        }

        /// <summary>
        /// Creates the pump when it is not alive yet. Auto-initialisation
        /// already runs before the first scene, so this is the safety net for
        /// callers that touch the framework from <c>Awake</c> of a component
        /// created earlier than that (or in an isolated test scene).
        /// </summary>
        public static void EnsureExists()
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
            _effectiveActionCap = _budget.MaxActionsPerFrame;
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

            _instance.Enqueue(action, index);
        }

        /// <summary>
        /// Posts a terminal lifecycle transition (complete / cancel / dispose).
        ///
        /// Every terminal transition uses the same priority on purpose. If a
        /// completion went to High while a cancellation went to Normal, the
        /// completion would always be processed first no matter which one was
        /// posted first, and the race would be decided by queue assignment
        /// instead of by arrival order.
        /// </summary>
        public static void PostTerminal(Action action)
        {
            Post(action, PumpPriority.High);
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the next frame, before that frame's
        /// budget is drained.
        ///
        /// This is what per-frame polling of a Unity async operation needs: the
        /// operation only makes progress between frames, so re-posting into a
        /// priority queue would let the same frame drain the action again and
        /// again until the action cap ran out.
        /// </summary>
        public static void PostNextFrame(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_instance == null)
            {
                throw new InvalidOperationException(
                    "MainThreadPump is not initialized. " +
                    "It must be initialized before PostNextFrame is used.");
            }

            _instance._nextFrame.Enqueue(action);
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

        private void Enqueue(Action action, int index)
        {
            _queues[index].Enqueue(new WorkItem
            {
                Action = action,
                Sequence = Interlocked.Increment(ref _sequence)
            });

            Interlocked.Increment(ref _totalPosted);
        }

        private void Update()
        {
            PromoteNextFrameActions();
            DrainFrame();
        }

        private void PromoteNextFrameActions()
        {
            while (_nextFrame.TryDequeue(out var action))
            {
                Enqueue(action, (int)PumpPriority.Normal);
            }
        }

        public void DrainFrame()
        {
            var processed = 0;
            var actionCap = ResolveActionCap();
            var maxMilliseconds = Math.Max(0.0f, _budget.MaxMillisecondsPerFrame);
            var lowReserveEvery = Math.Max(0, _budget.LowReserveEvery);

            var frameStart = Stopwatch.GetTimestamp();
            var longest = 0.0;
            var overrun = false;

            while (processed < actionCap)
            {
                if (maxMilliseconds > 0.0f &&
                    ElapsedMilliseconds(frameStart) >= maxMilliseconds)
                {
                    overrun = true;
                    break;
                }

                var slot = processed + 1;
                var preferLow = lowReserveEvery > 0 && slot % lowReserveEvery == 0;

                if (!TryDequeue(preferLow, out var item))
                {
                    break;
                }

                var actionStart = Stopwatch.GetTimestamp();

                try
                {
                    item.Action();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }

                var cost = ElapsedMilliseconds(actionStart);

                if (cost > longest)
                {
                    longest = cost;
                }

                processed++;
            }

            _lastFrameProcessed = processed;
            _lastFrameMilliseconds = ElapsedMilliseconds(frameStart);
            _longestActionMilliseconds = longest;

            if (overrun ||
                (maxMilliseconds > 0.0f &&
                 _lastFrameMilliseconds > maxMilliseconds))
            {
                _overBudgetFrames++;
            }

            Interlocked.Add(ref _totalDrained, processed);
            ApplyAdaptiveCap(processed, actionCap, overrun);
        }

        /// <summary>
        /// Strict priority, FIFO inside a priority. <paramref name="preferLow"/>
        /// is the counted reservation described in the type comment; it only
        /// takes effect when Low actually has work.
        /// </summary>
        private bool TryDequeue(bool preferLow, out WorkItem item)
        {
            if (preferLow && _queues[(int)PumpPriority.Low].TryDequeue(out item))
            {
                return true;
            }

            if (_queues[(int)PumpPriority.High].TryDequeue(out item))
            {
                return true;
            }

            if (_queues[(int)PumpPriority.Normal].TryDequeue(out item))
            {
                return true;
            }

            return _queues[(int)PumpPriority.Low].TryDequeue(out item);
        }

        private int ResolveActionCap()
        {
            var max = Math.Max(1, _budget.MaxActionsPerFrame);

            if (!_budget.Adaptive)
            {
                _effectiveActionCap = max;
                return max;
            }

            _effectiveActionCap = Math.Min(
                max,
                Math.Max(_budget.MinActionsPerFrame, _effectiveActionCap));

            return _effectiveActionCap;
        }

        private void ApplyAdaptiveCap(int processed, int actionCap, bool overrun)
        {
            if (!_budget.Adaptive)
            {
                _effectiveActionCap = Math.Max(1, _budget.MaxActionsPerFrame);
                return;
            }

            var max = Math.Max(1, _budget.MaxActionsPerFrame);

            if (overrun)
            {
                _effectiveActionCap = Math.Max(
                    _budget.MinActionsPerFrame,
                    actionCap / 2);

                return;
            }

            if (processed < actionCap && PendingCount == 0)
            {
                _effectiveActionCap = max;
            }
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) *
                   1000.0 /
                   Stopwatch.Frequency;
        }
    }
}
