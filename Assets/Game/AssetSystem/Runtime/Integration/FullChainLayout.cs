using System.IO;
using Game.AssetSystem.Hosting;
using UnityEngine;

namespace Game.AssetSystem.FullChain
{
    /// <summary>
    /// Shared location contract for the full-chain content.
    /// The Editor builder writes real AssetBundles here, the PlayMode test
    /// reads them back. Nothing is hard-coded per machine.
    ///
    /// This is a build-output contract, not a demo contract: no scene uses it.
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
            get { return PlatformKeys.Current; }
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
