using System;
using System.Threading;
using GameFramework.Asset.Diagnostics;
using Cysharp.Threading.Tasks;

namespace GameFramework.Asset.Core
{
    internal static class MainThreadGuard
    {
        private static int _mainThreadId;

        public static void Capture()
        {
            if (!PlayerLoopHelper.IsMainThread)
                throw new InvalidOperationException("Initialize AssetSystem from Unity's main thread.");
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void Assert()
        {
            if (_mainThreadId == 0)
            {
                Capture();
            }

            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                throw new AssetException(new AssetError(
                    AssetErrorCode.MainThreadViolation,
                    string.Empty,
                    string.Empty,
                    "AssetSystem must be used from the thread that initialized it."));
            }
        }
    }
}
