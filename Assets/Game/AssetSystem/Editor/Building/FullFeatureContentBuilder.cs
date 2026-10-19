using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.AssetSystem.Demos.FullFeature;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Building
{
    /// <summary>
    /// Produces everything <c>Demo_FullFeature.unity</c> consumes: three
    /// versions of the shared / panel / hit sample content, each packed into
    /// three real AssetBundles (<c>ui_shop</c>, <c>battle_fx</c>, <c>shared</c>)
    /// with the dependency edges declared in the manifest.
    ///
    /// Output: <c>GameAssetSystemBuild/FullFeature/&lt;platform&gt;/&lt;version&gt;/</c>.
    /// The runtime treats that folder as the patch server.
    /// </summary>
    public static class FullFeatureContentBuilder
    {
        public const string SampleRoot =
            "Assets/Game/AssetSystem/FullFeatureSample";

        [MenuItem("Tools/Game Asset System/Build Full-Feature Demo Content")]
        public static void BuildAll()
        {
            GenerateSampleContent();

            var target = EditorUserBuildSettings.activeBuildTarget;
            var targetKey = PlatformVariantPolicy.GetPlatformKey(target);

            if (!string.Equals(
                targetKey,
                FullFeatureLayout.PlatformKey,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Active build target is " + targetKey +
                    " but the demo reads " + FullFeatureLayout.PlatformKey +
                    ". Switch the active build target before building.");
            }

            var outputRoot = Path.Combine(
                PlatformVariantPolicy.GetProjectRoot(),
                "GameAssetSystemBuild",
                FullFeatureLayout.BuildFolderName,
                targetKey);

            var builder = new AssetBundleBuilder();

            for (var i = 0; i < FullFeatureLayout.Versions.Length; i++)
            {
                var version = FullFeatureLayout.Versions[i];
                var result = builder.Build(
                    BuildBindings(version),
                    version,
                    target,
                    outputRoot);

                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        "Full-feature " + version + " build failed: " +
                        Describe(result));
                }

                Debug.Log(
                    "[FullFeature] " + version + " built. bundles=" +
                    result.Bundles.Count + " assets=" +
                    (result.Manifest == null ? 0 : result.Manifest.assets.Count) +
                    " output=" + result.OutputDirectory);
            }

            Debug.Log(
                "[FullFeature] content ready. platform=" + targetKey +
                " root=" + outputRoot);
        }

        [MenuItem("Tools/Game Asset System/Clean Full-Feature Demo Content")]
        public static void CleanAll()
        {
            for (var i = 0; i < FullFeatureLayout.Versions.Length; i++)
            {
                DeleteDirectory(Path.Combine(
                    FullFeatureLayout.BuildRoot,
                    FullFeatureLayout.Versions[i]));
            }

            CleanupSampleContent();

            Debug.Log("[FullFeature] content cleaned.");
        }

        /// <summary>One click: build content, then play the demo scene.</summary>
        [MenuItem("Tools/Game Asset System/Build Full-Feature Content And Play")]
        public static void BuildAndPlay()
        {
            try
            {
                BuildAll();
            }
            catch (Exception exception)
            {
                Debug.LogError("[FullFeature] build failed: " + exception.Message);
                return;
            }

            var scene = "Assets/Scenes/Demo_FullFeature.unity";

            if (File.Exists(scene))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene);
            }

            EditorApplication.isPlaying = true;
        }

        // ------------------------------------------------------------------

        private static IReadOnlyList<AssetBinding> BuildBindings(string version)
        {
            var root = SampleRoot + "/" + version;

            return new List<AssetBinding>
            {
                new AssetBinding
                {
                    sourcePath = root + "/Shared/Shared.asset",
                    assetKey = FullFeatureContent.SharedKey,
                    bundleGroupName = FullFeatureContent.SharedGroup
                },
                new AssetBinding
                {
                    sourcePath = root + "/UI/Panel.asset",
                    assetKey = FullFeatureContent.PanelKey,
                    bundleGroupName = FullFeatureContent.UiShopGroup,
                    dependencies = new[] { FullFeatureContent.SharedGroup }
                },
                new AssetBinding
                {
                    sourcePath = root + "/Fx/Hit.asset",
                    assetKey = FullFeatureContent.HitKey,
                    bundleGroupName = FullFeatureContent.BattleFxGroup,
                    dependencies = new[] { FullFeatureContent.SharedGroup }
                }
            };
        }

        private static void GenerateSampleContent()
        {
            CleanupSampleContent();

            for (var i = 0; i < FullFeatureLayout.Versions.Length; i++)
            {
                var version = FullFeatureLayout.Versions[i];
                var root = SampleRoot + "/" + version;

                EnsureFolder(root + "/Shared");
                EnsureFolder(root + "/UI");
                EnsureFolder(root + "/Fx");

                var shared = CreateShared(
                    root + "/Shared/Shared.asset",
                    version,
                    ColorFor(version));

                CreateAssetInfo(
                    root + "/UI/Panel.asset",
                    version + "/UI/Panel",
                    shared);

                CreateAssetInfo(
                    root + "/Fx/Hit.asset",
                    version + "/Fx/Hit",
                    shared);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static Color ColorFor(string version)
        {
            switch (version)
            {
                case "v1":
                    return new Color(0.90f, 0.32f, 0.32f);
                case "v2":
                    return new Color(0.30f, 0.80f, 0.48f);
                default:
                    return new Color(0.34f, 0.56f, 0.94f);
            }
        }

        private static SharedInfo CreateShared(
            string path,
            string version,
            Color color)
        {
            var shared = ScriptableObject.CreateInstance<SharedInfo>();
            shared.version = version;
            shared.color = color;
            shared.label = version + "/Shared";

            AssetDatabase.CreateAsset(shared, path);
            EditorUtility.SetDirty(shared);
            return shared;
        }

        private static void CreateAssetInfo(
            string path,
            string label,
            SharedInfo shared)
        {
            var info = ScriptableObject.CreateInstance<AssetInfo>();
            info.label = label;
            info.shared = shared;

            AssetDatabase.CreateAsset(info, path);
            EditorUtility.SetDirty(info);
        }

        private static void CleanupSampleContent()
        {
            if (!AssetDatabase.IsValidFolder(SampleRoot))
            {
                return;
            }

            AssetDatabase.DeleteAsset(SampleRoot);
            AssetDatabase.Refresh();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];

                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FullFeature] failed to delete " + path + ": " +
                    exception.Message);
            }
        }

        private static string Describe(BuildResult result)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < result.Errors.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(result.Errors[i].Code)
                    .Append(": ")
                    .Append(result.Errors[i].Message);
            }

            return builder.ToString();
        }
    }
}
