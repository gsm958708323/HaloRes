using System.IO;
using Game.AssetSystem.Content;
using Game.AssetSystem.Hosting;
using Game.AssetSystem.Patch;
using UnityEngine;

namespace Game.AssetSystem.Demos.FullFeature
{
    /// <summary>
    /// Location contract shared by the Editor content builder and the runtime
    /// demo. The builder writes real AssetBundles under
    /// <c>GameAssetSystemBuild/FullFeature/&lt;platform&gt;/&lt;version&gt;/</c>;
    /// the runtime treats that folder as the patch server and installs into
    /// <c>Application.persistentDataPath</c>.
    ///
    /// The client-side orchestration itself is not here any more: it lives in
    /// <see cref="AssetSystemFacade"/>. This type only answers "where".
    /// </summary>
    public static class FullFeatureLayout
    {
        public const string ManifestFileName = "lesson43-manifest.json";
        public const string ClientFolderName = "AssetSystemFullFeatureDemo";
        public const string BaseVersion = "v1";

        /// <summary>Build output folder name, next to the Lesson43 output.</summary>
        public const string BuildFolderName = "FullFeature";

        public static readonly string[] Versions = { "v1", "v2", "v3" };

        public static string ProjectRoot
        {
            get
            {
                var parent = Directory.GetParent(Application.dataPath);
                return parent == null ? Application.dataPath : parent.FullName;
            }
        }

        public static string PlatformKey
        {
            get { return PlatformKeys.Current; }
        }

        public static string BuildRoot
        {
            get
            {
                return Path.Combine(
                    ProjectRoot,
                    "GameAssetSystemBuild",
                    BuildFolderName,
                    PlatformKey);
            }
        }

        public static string ServerVersionDir(string version)
        {
            return Path.Combine(BuildRoot, version);
        }

        public static string ServerManifestPath(string version)
        {
            return Path.Combine(ServerVersionDir(version), ManifestFileName);
        }

        public static bool HasServerContent(string version)
        {
            return File.Exists(ServerManifestPath(version));
        }

        public static string DefaultClientRoot
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    ClientFolderName);
            }
        }

        /// <summary>
        /// Reads a build-pipeline manifest. Delegates to
        /// <see cref="BuildManifestReader"/> so the demo and the framework agree
        /// on the manifest shape, dependencies included.
        /// </summary>
        public static PatchManifest LoadManifest(string manifestPath)
        {
            return BuildManifestReader.Read(manifestPath);
        }
    }
}
