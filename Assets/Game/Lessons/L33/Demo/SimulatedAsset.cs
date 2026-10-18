using System.Threading;
using UnityEngine;

namespace Game.AssetSystem.Demo.L33
{
    public sealed class SimulatedAsset
    {
        private int _releaseCount;

        public SimulatedAsset(string key)
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
                "[SimulatedAsset] release key=" + Key +
                " reason=" + reason +
                " releaseCount=" + count +
                " thread=" + Thread.CurrentThread.ManagedThreadId);
        }
    }
}
