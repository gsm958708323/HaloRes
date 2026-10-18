using System;
using System.IO;
using UnityEngine;

namespace Game.AssetSystem.FullChain
{
    /// <summary>
    /// Shared location contract for the full-chain demo.
    /// The Editor builder writes real AssetBundles here, the runtime demo and
    /// the PlayMode test read them back. Nothing is hard-coded per machine.
    /// </summary>
    public static class FullChainLayout
    {
        public const string V1Version = "fullchain-v1";
        public const string V2Version = "fullchain-v2";
        public const string ManifestFileName = "lesson43-manifest.json";
        public const string V2LabelSuffix = "_v2";

        public static string ProjectRoot
        {
            get
            {
                var parent = Directory.GetParent(Application.dataPath);
                return parent == null ? Application.dataPath : parent.FullName;
            }
        }

        public static string BuildRoot
        {
            get
            {
                return Path.Combine(
                    ProjectRoot,
                    "GameAssetSystemBuild",
                    "Lesson43",
                    PlatformKey);
            }
        }

        public static string PlatformKey
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

        public static string ContentDir(string contentVersion)
        {
            return Path.Combine(BuildRoot, contentVersion);
        }

        public static string ManifestPath(string contentVersion)
        {
            return Path.Combine(ContentDir(contentVersion), ManifestFileName);
        }

        public static bool HasContent(string contentVersion)
        {
            return File.Exists(ManifestPath(contentVersion));
        }
    }
}
