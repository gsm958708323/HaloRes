using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Scene entry point for <c>Demo_Lessons.unity</c>.
    ///
    /// One scene, one component, one enum: pick the lesson in the Inspector and
    /// press Play. Nothing boots itself, so the scene stays empty unless this
    /// component is present — the same contract as the interactive and
    /// full-chain demo scenes.
    ///
    /// The demo scripts themselves were restored from the original
    /// Assets/Game/Lessons folder, but re-pointed at the current framework
    /// (Game.AssetSystem.*) instead of the per-lesson snapshots.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LessonDemoBootstrap : MonoBehaviour
    {
        [Header("Lesson Selection")]
        [Tooltip("Which restored lesson demo runs on Play.")]
        [SerializeField]
        private LessonDemoKind _demo = LessonDemoKind.MainThreadRunner;

        [Tooltip("Run the selected demo automatically when the scene starts.")]
        [SerializeField]
        private bool _runOnStart = true;

        [Tooltip("Log a begin/finish banner around the demo output.")]
        [SerializeField]
        private bool _logBanner = true;

        private ILessonDemo _current;
        private CancellationTokenSource _cancellation;
        private bool _isRunning;

        /// <summary>Currently selected lesson.</summary>
        public LessonDemoKind Demo
        {
            get { return _demo; }
        }

        /// <summary>True while a demo is running (or shutting down).</summary>
        public bool IsRunning
        {
            get { return _isRunning; }
        }

        private void Start()
        {
            if (_runOnStart)
            {
                RunSelected();
            }
        }

        private void Update()
        {
            // Only scheduler / load-gate lessons need a per-frame tick.
            if (_isRunning && _current != null)
            {
                _current.Tick();
            }
        }

        private void OnDestroy()
        {
            CancelRunning();
        }

        /// <summary>
        /// Selects a lesson without running it. Rejected while a demo is active
        /// so a running scenario cannot switch under itself.
        /// </summary>
        public void Select(LessonDemoKind kind)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException(
                    "Cannot change lesson while a demo is running.");
            }

            _demo = kind;
        }

        /// <summary>
        /// Runs the selected lesson. Safe to call from a button or the
        /// component context menu; a second call while running is ignored.
        /// </summary>
        [ContextMenu("Run Selected Lesson Demo")]
        public void RunSelected()
        {
            if (_isRunning)
            {
                Debug.LogWarning(
                    "[LessonDemo] a demo is already running: " +
                    (_current != null ? _current.Title : "<unknown>"));
                return;
            }

            ILessonDemo demo;

            try
            {
                demo = LessonDemoFactory.Create(_demo);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[LessonDemo] failed to create demo " + _demo + ": " +
                    exception.Message);
                return;
            }

            _current = demo;
            _cancellation = new CancellationTokenSource();
            _isRunning = true;

            // Fire-and-forget: RunAsync owns every exception and the finally
            // block, so no async void is needed here.
            _ = RunAsync(demo, _demo, _cancellation);
        }

        private async Task RunAsync(
            ILessonDemo demo,
            LessonDemoKind kind,
            CancellationTokenSource cancellation)
        {
            var watch = Stopwatch.StartNew();
            var token = cancellation.Token;

            if (_logBanner)
            {
                Debug.Log(
                    "[LessonDemo] begin kind=" + kind +
                    " title=" + demo.Title +
                    " frame=" + Time.frameCount +
                    " mainThreadId=" + MainThreadPump.MainThreadId);
            }

            try
            {
                await demo.RunAsync(token);

                if (_logBanner)
                {
                    Debug.Log(
                        "[LessonDemo] finished kind=" + kind +
                        " elapsedMs=" + watch.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[LessonDemo] canceled kind=" + kind);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[LessonDemo] failed kind=" + kind + "\n" + exception);
            }
            finally
            {
                _isRunning = false;
                _current = null;

                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                }

                cancellation.Dispose();
            }
        }

        private void CancelRunning()
        {
            var cancellation = _cancellation;

            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished; nothing to cancel.
            }
        }
    }
}
