using System;
using System.IO;
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
        /// Reads a build-pipeline manifest (the shape
        /// <c>AssetBundleBuilder</c> writes) and converts it to the patch
        /// manifest the runtime activates.
        /// </summary>
        public static PatchManifest LoadManifest(string manifestPath)
        {
            var json = File.ReadAllText(manifestPath);
            var dto = JsonUtility.FromJson<BuildManifestDto>(json);

            if (dto == null)
            {
                throw new InvalidOperationException(
                    "Failed to parse manifest: " + manifestPath);
            }

            var manifest = new PatchManifest
            {
                SchemaVersion = dto.schemaVersion,
                ContentVersion = dto.contentVersion,
                PlatformKey = dto.targetPlatformKey
            };

            if (dto.bundles != null)
            {
                var bundles = new PatchBundleDescriptor[dto.bundles.Length];

                for (var i = 0; i < dto.bundles.Length; i++)
                {
                    var source = dto.bundles[i];

                    bundles[i] = new PatchBundleDescriptor
                    {
                        BundleGroupName = source.bundleGroupName,
                        FileName = source.bundleFileName,
                        SizeBytes = source.sizeBytes,
                        Sha256Hex = source.sha256Hex,
                        Dependencies = source.dependencies
                    };
                }

                manifest.Bundles = bundles;
            }

            if (dto.assets != null)
            {
                var assets = new PatchAssetDescriptor[dto.assets.Length];

                for (var i = 0; i < dto.assets.Length; i++)
                {
                    var source = dto.assets[i];

                    assets[i] = new PatchAssetDescriptor
                    {
                        AssetKey = source.assetKey,
                        BundleGroupName = source.bundleGroupName,
                        InternalPath = source.internalAssetPath
                    };
                }

                manifest.Assets = assets;
            }

            return manifest;
        }

        [Serializable]
        private sealed class BuildManifestDto
        {
            public int schemaVersion;
            public string contentVersion;
            public string targetPlatformKey;
            public BuildBundleDto[] bundles;
            public BuildAssetDto[] assets;
        }

        [Serializable]
        private sealed class BuildBundleDto
        {
            public string bundleGroupName;
            public string bundleFileName;
            public long sizeBytes;
            public string sha256Hex;
            public string[] dependencies;
        }

        [Serializable]
        private sealed class BuildAssetDto
        {
            public string assetKey;
            public string internalAssetPath;
            public string bundleGroupName;
        }
    }

    /// <summary>
    /// What the client remembers between runs: the activated manifest plus the
    /// folder its bundles live in. Written only after a successful activation,
    /// so a failed switch never leaves the client pointing at half content.
    /// </summary>
    [Serializable]
    public sealed class FullFeatureActiveState
    {
        public int schemaVersion;
        public string contentVersion;
        public string bundleRoot;
        public PatchManifest manifest;
    }
}
