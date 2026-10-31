using System;
using UnityEngine;

namespace GameFramework.Asset.Samples
{
    public static class AssetPlatform
    {
        public static string Current
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.WindowsPlayer: return "Windows";
                    case RuntimePlatform.OSXEditor:
                    case RuntimePlatform.OSXPlayer: return "OSX";
                    case RuntimePlatform.LinuxEditor:
                    case RuntimePlatform.LinuxPlayer: return "Linux";
                    case RuntimePlatform.Android: return "Android";
                    case RuntimePlatform.IPhonePlayer: return "iOS";
                    default: throw new PlatformNotSupportedException("Unsupported asset platform: " + Application.platform);
                }
            }
        }
    }
}
