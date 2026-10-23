using UnityEngine;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// One place that turns the running platform into the build-target key the
    /// content pipeline writes into a manifest. Layouts used to carry their own
    /// copy of this switch; a mismatch between two copies is exactly the kind
    /// of bug that only shows up on a device build.
    /// </summary>
    public static class PlatformKeys
    {
        public static string Current
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.WindowsPlayer:
                        return "StandaloneWindows64";
                    case RuntimePlatform.OSXEditor:
                    case RuntimePlatform.OSXPlayer:
                        return "StandaloneOSX";
                    case RuntimePlatform.LinuxEditor:
                    case RuntimePlatform.LinuxPlayer:
                        return "StandaloneLinux64";
                    case RuntimePlatform.Android:
                        return "Android";
                    case RuntimePlatform.IPhonePlayer:
                        return "iOS";
                    default:
                        return Application.platform.ToString();
                }
            }
        }
    }
}
