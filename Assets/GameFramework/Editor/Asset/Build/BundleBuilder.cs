using System;
using System.IO;
using System.Linq;
using System.Text;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using UnityEditor;
using UnityEngine;

namespace GameFramework.Asset.Editor.Build
{
    public static class BundleBuilder
    {
        public static string Build(AssetBuildProfile profile, string outputRoot, BuildTarget target)
        {
            var names = ManifestGenerator.BundleNames(profile);
            var assets = ManifestGenerator.CollectAssets(profile, names);
            var buildMap = names.Select((name, index) => new AssetBundleBuild {
                assetBundleName = name,
                assetNames = assets.Where(asset => asset.bundleIndex == index).Select(asset => asset.assetPath).ToArray()
            }).ToArray();
            foreach (var group in buildMap)
            {
                var sceneCount = group.assetNames.Count(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
                if (sceneCount > 0 && sceneCount != group.assetNames.Length)
                    throw new ArgumentException("Scene and non-scene assets cannot share a bundle: " + group.assetBundleName);
            }
            var root = Path.GetFullPath(outputRoot);
            var raw = Path.Combine(root, "raw");
            Directory.CreateDirectory(raw);
            var unityManifest = BuildPipeline.BuildAssetBundles(raw, buildMap,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (unityManifest == null) throw new InvalidOperationException("Unity AssetBundle build failed.");
            var indices = names.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
            var direct = names.Select(name => unityManifest.GetDirectDependencies(name).Select(dependency => indices[dependency]).ToArray()).ToArray();
            var bundleDirectory = Path.Combine(root, "bundles");
            Directory.CreateDirectory(bundleDirectory);
            var bundles = new ManifestBundleData[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var path = Path.Combine(raw, names[i]);
                var hash = FileIntegrity.HashFile(path);
                bundles[i] = new ManifestBundleData { name = names[i], hash = hash, size = new FileInfo(path).Length,
                    dependencies = ManifestGenerator.FlattenDependencies(i, direct) };
                File.Copy(path, Path.Combine(bundleDirectory, hash + ".bundle"), true);
            }
            var json = JsonUtility.ToJson(new AssetManifestFile { version = profile.version, bundles = bundles, assets = assets }, true);
            AssetManifest.FromJson(json);
            var bytes = new UTF8Encoding(false).GetBytes(json);
            var manifestHash = FileIntegrity.Hash(bytes);
            Directory.CreateDirectory(Path.Combine(root, "manifests"));
            File.WriteAllBytes(Path.Combine(root, "manifests", manifestHash + ".json"), bytes);
            File.WriteAllBytes(Path.Combine(root, "asset_manifest.json"), bytes);
            var version = new VersionFile { version = profile.version, platform = PlatformName(target), manifestHash = manifestHash, manifestSize = bytes.LongLength };
            File.WriteAllText(Path.Combine(root, "version.json"), version.ToJson(), new UTF8Encoding(false));
            Debug.Log("Asset content v" + profile.version + " built: " + root);
            return root;
        }

        public static string PlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return "Windows";
                case BuildTarget.StandaloneOSX: return "OSX";
                case BuildTarget.StandaloneLinux64: return "Linux";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                default: throw new PlatformNotSupportedException("Supported targets: Windows, OSX, Linux, Android and iOS.");
            }
        }

        [MenuItem("Game Framework/Assets/Build Selected Profile")]
        private static void BuildSelected()
        {
            if (!(Selection.activeObject is AssetBuildProfile profile)) throw new InvalidOperationException("Select an AssetBuildProfile.");
            Build(profile, Path.Combine("Build", "AssetContent", EditorUserBuildSettings.activeBuildTarget.ToString(), "v" + profile.version), EditorUserBuildSettings.activeBuildTarget);
        }
    }
}
