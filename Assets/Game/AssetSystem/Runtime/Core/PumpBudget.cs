using System;

namespace Game.AssetSystem.Core
{
    /// <summary>
    /// Per-frame drain policy for <see cref="MainThreadPump"/>.
    ///
    /// The numbers below are a starting point, not a conclusion. The lesson
    /// used to present 64 actions / 2 ms as if they had been measured; they
    /// were not. Keep them configurable, and use
    /// <see cref="MainThreadPump.LastFrameMilliseconds"/> /
    /// <see cref="MainThreadPump.OverBudgetFrames"/> to decide what the real
    /// value should be once there is real concurrency.
    /// </summary>
    [Serializable]
    public sealed class PumpBudget
    {
        /// <summary>Hard cap on actions drained in one Unity frame.</summary>
        public int MaxActionsPerFrame = 64;

        /// <summary>
        /// Soft cap on wall-clock time spent draining one frame. Checked
        /// between actions, so a single slow action can overrun it; that case
        /// is reported through <see cref="MainThreadPump.LongestActionMilliseconds"/>.
        /// Zero or negative disables the time cap.
        /// </summary>
        public float MaxMillisecondsPerFrame = 2.0f;

        /// <summary>
        /// Every Nth slot of a frame is reserved for the Low queue. This is the
        /// only rule that lets a lower priority jump ahead of a higher one, and
        /// it is counted per frame instead of being derived from queue
        /// occupancy, so the same input always produces the same order.
        /// Zero disables the reservation (strict priority, Low may starve).
        /// </summary>
        public int LowReserveEvery = 8;

        /// <summary>
        /// When enabled, a frame that overruns halves the next frame's action
        /// cap (down to <see cref="MinActionsPerFrame"/>) and a frame that
        /// drains the queue restores it. Off by default: an auto-tuning budget
        /// makes ordering tests non-deterministic.
        /// </summary>
        public bool Adaptive = false;

        public int MinActionsPerFrame = 8;

        public PumpBudget Clone()
        {
            return new PumpBudget
            {
                MaxActionsPerFrame = MaxActionsPerFrame,
                MaxMillisecondsPerFrame = MaxMillisecondsPerFrame,
                LowReserveEvery = LowReserveEvery,
                Adaptive = Adaptive,
                MinActionsPerFrame = MinActionsPerFrame
            };
        }
    }
}
