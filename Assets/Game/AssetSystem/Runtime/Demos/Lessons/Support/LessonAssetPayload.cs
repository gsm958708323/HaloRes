using System.Threading;
using Game.AssetSystem.Assets;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Asset payload double for L4.5 / L4.6 / L5.1. Release is counted so a
    /// missed or duplicated release is visible in the console.
    /// </summary>
    public sealed class LessonAssetPayload : IAssetPayload
    {
        private int _releaseCount;

        public LessonAssetPayload(string debugName)
        {
            DebugName = debugName;
        }

        public string DebugName { get; }

        public int ReleaseCount
        {
            get { return Volatile.Read(ref _releaseCount); }
        }

        public void Release()
        {
            var count = Interlocked.Increment(ref _releaseCount);

            Debug.Log(
                "[LessonAssetPayload] release " + DebugName +
                " count=" + count);
        }

        public override string ToString()
        {
            return "LessonAssetPayload(" + DebugName + ")";
        }
    }
}
