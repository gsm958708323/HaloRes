using System;
using System.Collections.Generic;
using Game.AssetSystem.Integration.L51;

namespace Game.AssetSystem.Integration.Release.L52
{
    public sealed class SafePointGate
    {
        private readonly HashSet<string> _reasons =
            new HashSet<string>(StringComparer.Ordinal);

        public bool Enter(string reason)
        {
            IntegrationMainThreadPump.EnsureMainThread(nameof(Enter));

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "reason must not be empty.",
                    nameof(reason));
            }

            return _reasons.Add(reason);
        }

        public bool Exit(string reason)
        {
            IntegrationMainThreadPump.EnsureMainThread(nameof(Exit));

            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            return _reasons.Remove(reason);
        }

        public bool IsSafePoint
        {
            get { return _reasons.Count > 0; }
        }

        public int ReasonCount
        {
            get { return _reasons.Count; }
        }

        public bool CanActivate(AssetCache cache)
        {
            IntegrationMainThreadPump.EnsureMainThread(nameof(CanActivate));

            if (!IsSafePoint)
            {
                return false;
            }

            if (cache == null)
            {
                return false;
            }

            if (cache.TotalLiveHandles != 0)
            {
                return false;
            }

            if (cache.LoadingAssetCount != 0)
            {
                return false;
            }

            return true;
        }
    }
}
