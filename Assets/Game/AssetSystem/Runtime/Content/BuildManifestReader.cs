using System;
using System.IO;
using Game.AssetSystem.Patch;
using UnityEngine;

namespace Game.AssetSystem.Content
{
    /// <summary>
    /// Reads the manifest the build pipeline writes
    /// (<c>AssetBundleBuilder</c> output) and converts it into the
    /// <see cref="PatchManifest"/> the runtime activates.
    ///
    /// This used to exist twice — once in the full-chain layout, once in the
    /// full-feature layout — and only one copy carried
    /// <c>bundles[].dependencies</c>. One reader means a manifest is either
    /// understood completely or not at all.
    /// </summary>
    public static class BuildManifestReader
    {
        public static PatchManifest Read(string manifestPath)
        {
            if (string.IsNullOrEmpty(manifestPath))
            {
                throw new ArgumentException(
                    "manifestPath must not be empty.",
                    nameof(manifestPath));
            }

            return Parse(File.ReadAllText(manifestPath), manifestPath);
        }

        public static PatchManifest Parse(string json, string sourceLabel)
        {
            var dto = JsonUtility.FromJson<BuildManifestDto>(json);

            if (dto == null)
            {
                throw new InvalidOperationException(
                    "Failed to parse manifest: " + sourceLabel);
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
}
