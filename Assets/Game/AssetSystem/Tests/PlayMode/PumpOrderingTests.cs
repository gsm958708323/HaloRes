using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine.TestTools;
using Game.AssetSystem.Core;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// Locks down the pump's ordering contract. These are the assertions the
    /// previous cursor-based rotation could not satisfy:
    /// - same priority keeps enqueue order;
    /// - higher priority drains first;
    /// - the Low reservation is counted per frame, not derived from occupancy;
    /// - terminal transitions posted to the same priority keep post order,
    ///   which is what makes a cancel/complete race decided by arrival.
    /// </summary>
    public sealed class PumpOrderingTests
    {
        private int _savedActions;
        private float _savedMilliseconds;
        private int _savedLowReserve;
        private bool _savedAdaptive;

        [SetUp]
        public void SetUp()
        {
            var budget = MainThreadPump.Instance.Budget;

            _savedActions = budget.MaxActionsPerFrame;
            _savedMilliseconds = budget.MaxMillisecondsPerFrame;
            _savedLowReserve = budget.LowReserveEvery;
            _savedAdaptive = budget.Adaptive;

            budget.MaxActionsPerFrame = 64;
            budget.MaxMillisecondsPerFrame = 0.0f;
            budget.Adaptive = false;

            DrainUntilEmpty();
        }

        [TearDown]
        public void TearDown()
        {
            var budget = MainThreadPump.Instance.Budget;

            budget.MaxActionsPerFrame = _savedActions;
            budget.MaxMillisecondsPerFrame = _savedMilliseconds;
            budget.LowReserveEvery = _savedLowReserve;
            budget.Adaptive = _savedAdaptive;

            DrainUntilEmpty();
        }

        [UnityTest]
        public IEnumerator Pump_SamePriority_KeepsEnqueueOrder()
        {
            var order = new List<int>();

            for (var i = 0; i < 8; i++)
            {
                var captured = i;
                MainThreadPump.Post(
                    () => order.Add(captured),
                    PumpPriority.Normal);
            }

            MainThreadPump.Instance.DrainFrame();

            CollectionAssert.AreEqual(
                new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                order,
                "same-priority actions must run in enqueue order");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pump_HighDrainsBeforeNormal_EvenWhenPostedLater()
        {
            var order = new List<string>();

            MainThreadPump.Post(
                () => order.Add("normal-1"),
                PumpPriority.Normal);

            MainThreadPump.Post(
                () => order.Add("normal-2"),
                PumpPriority.Normal);

            MainThreadPump.Post(
                () => order.Add("high-1"),
                PumpPriority.High);

            MainThreadPump.Instance.DrainFrame();

            CollectionAssert.AreEqual(
                new[] { "high-1", "normal-1", "normal-2" },
                order,
                "High must drain before Normal regardless of post order");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pump_LowReserveIsCounted_NotCursorBased()
        {
            var budget = MainThreadPump.Instance.Budget;
            budget.MaxActionsPerFrame = 8;
            budget.LowReserveEvery = 4;

            var order = new List<string>();

            for (var i = 0; i < 8; i++)
            {
                var captured = i;
                MainThreadPump.Post(
                    () => order.Add("H" + captured),
                    PumpPriority.High);

                MainThreadPump.Post(
                    () => order.Add("L" + captured),
                    PumpPriority.Low);
            }

            MainThreadPump.Instance.DrainFrame();

            Assert.AreEqual(8, order.Count, "one frame must drain exactly the cap");

            // Slots 4 and 8 are reserved for Low; everything else is High.
            CollectionAssert.AreEqual(
                new[] { "H0", "H1", "H2", "L0", "H3", "H4", "H5", "L1" },
                order,
                "the Low reservation must land on fixed slot numbers");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pump_TerminalTransitions_ShareOneQueueSoPostOrderWins()
        {
            var order = new List<string>();

            MainThreadPump.PostTerminal(() => order.Add("cancel"));
            MainThreadPump.PostTerminal(() => order.Add("complete"));

            MainThreadPump.Instance.DrainFrame();

            CollectionAssert.AreEqual(
                new[] { "cancel", "complete" },
                order,
                "terminal transitions must keep post order");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pump_DifferentQueues_LosePostOrder_WhichIsWhyPostTerminalExists()
        {
            var order = new List<string>();

            // The shape of the old bug: cancellation on Normal, completion on
            // High. The completion overtakes the cancellation even though the
            // cancellation was posted first, so the race outcome is decided by
            // queue assignment instead of by arrival.
            MainThreadPump.Post(() => order.Add("cancel"), PumpPriority.Normal);
            MainThreadPump.Post(() => order.Add("complete"), PumpPriority.High);

            MainThreadPump.Instance.DrainFrame();

            CollectionAssert.AreEqual(
                new[] { "complete", "cancel" },
                order,
                "this characterises the bug PostTerminal is meant to prevent");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pump_ReportsSingleActionThatBlowsTheBudget()
        {
            var budget = MainThreadPump.Instance.Budget;
            budget.MaxMillisecondsPerFrame = 0.5f;

            var before = MainThreadPump.Instance.OverBudgetFrames;

            MainThreadPump.Post(() =>
            {
                var until = Stopwatch.GetTimestamp() +
                            Stopwatch.Frequency / 100; // ~10 ms

                while (Stopwatch.GetTimestamp() < until)
                {
                }
            });

            MainThreadPump.Instance.DrainFrame();

            Assert.Greater(
                MainThreadPump.Instance.LongestActionMilliseconds,
                1.0,
                "a single slow action must be reported, not silently swallowed");

            Assert.Greater(
                MainThreadPump.Instance.OverBudgetFrames,
                before,
                "an over-budget frame must be counted");

            yield return null;
        }

        private static void DrainUntilEmpty()
        {
            var pump = MainThreadPump.Instance;

            for (var i = 0; i < 16 && pump.PendingCount > 0; i++)
            {
                pump.DrainFrame();
            }
        }
    }
}
