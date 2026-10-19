using System.Threading;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Payload double for the loading lessons (L3.2 / L3.3). It only counts
    /// releases so a leaked or double-released shared load is visible in the
    /// console.
    /// </summary>
    public sealed class LessonSharedAsset
    {
        private int _releaseCount;

        public LessonSharedAsset(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public int ReleaseCount
        {
            get { return Volatile.Read(ref _releaseCount); }
        }

        public bool IsReleased
        {
            get { return ReleaseCount > 0; }
        }

        public void Release(string reason)
        {
            var count = Interlocked.Increment(ref _releaseCount);

            Debug.Log(
                "[LessonAsset] release key=" + Key +
                " reason=" + reason +
                " count=" + count);
        }

        public override string ToString()
        {
            return "LessonSharedAsset(" + Key + ")";
        }
    }
}
