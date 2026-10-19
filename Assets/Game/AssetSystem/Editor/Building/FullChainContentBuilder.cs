using System;
using System.Text;
using Game.AssetSystem.Editor.SampleContent;
using Game.AssetSystem.FullChain;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Building
{
    /// <summary>
    /// Generates the assets the full-chain demo needs:
    /// sample ScriptableObject content -> two real AssetBundle sets (v1 / v2)
    /// with manifests, under GameAssetSystemBuild/Lesson43/&lt;platform&gt;/.
    /// The output folder keeps its historical name so already-built content
    /// stays valid.
    /// </summary>
    public static class FullChainContentBuilder
    {
        private const string ShopRootAssetPath =
            BundleSampleContent.SampleContentRoot + "/UI/Shop/ShopRoot.asset";

        [MenuItem("Tools/Game Asset System/Build Full-Chain Demo Content")]
        public static void BuildAll()
        {
            BundleSampleContent.GenerateClean();

            var bindings = BundleSampleContent.GetBindings();
            var target = EditorUserBuildSettings.activeBuildTarget;
            var targetKey = PlatformVariantPolicy.GetPlatformKey(target);

            // The builder writes under <platform key>/ while the runtime demo
            // always reads FullChainLayout.PlatformKey. Building for a different
            // active target would silently produce content the demo cannot see.
            if (!string.Equals(
                targetKey,
                FullChainLayout.PlatformKey,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Active build target is " + targetKey +
                    " but the demo reads " + FullChainLayout.PlatformKey +
                    ". Switch the active build target before building.");
            }

            var builder = new AssetBundleBuilder();

            var v1 = builder.Build(bindings, FullChainLayout.V1Version, target);

            if (!v1.Success)
            {
                throw new InvalidOperationException(
                    "Full-chain v1 build failed: " + Describe(v1));
            }

            Debug.Log(
                "[FullChain] v1 built. bundles=" + v1.Bundles.Count +
                " assets=" + (v1.Manifest == null ? 0 : v1.Manifest.assets.Count) +
                " output=" + v1.OutputDirectory);

            BuildResult v2;

            ApplyV2Marker();

            try
            {
                v2 = builder.Build(bindings, FullChainLayout.V2Version, target);
            }
            finally
            {
                // Always undo the v2 marker, even when the build throws, or the
                // sample asset stays dirty in the editor session.
                RestoreV1Content();
            }

            if (!v2.Success)
            {
                throw new InvalidOperationException(
                    "Full-chain v2 build failed: " + Describe(v2));
            }

            Debug.Log(
                "[FullChain] v2 built. bundles=" + v2.Bundles.Count +
                " assets=" + (v2.Manifest == null ? 0 : v2.Manifest.assets.Count) +
                " output=" + v2.OutputDirectory);

            Debug.Log(
                "[FullChain] content ready. platform=" + targetKey +
                " root=" + FullChainLayout.BuildRoot);
        }

        /// <summary>One click: build v1/v2 bundles, then enter play mode.</summary>
        [MenuItem("Tools/Game Asset System/Build Content And Play Demo")]
        public static void BuildAndPlay()
        {
            try
            {
                BuildAll();
            }
            catch (Exception exception)
            {
                Debug.LogError("[FullChain] build failed: " + exception.Message);
                return;
            }

            EditorApplication.isPlaying = true;
        }

        /// <summary>Clean both versions and the generated sample content.</summary>
        [MenuItem("Tools/Game Asset System/Clean Full-Chain Demo Content")]
        public static void CleanAll()
        {
            DeleteDirectory(FullChainLayout.ContentDir(FullChainLayout.V1Version));
            DeleteDirectory(FullChainLayout.ContentDir(FullChainLayout.V2Version));
            BundleSampleContent.Cleanup();
            Debug.Log("[FullChain] content cleaned.");
        }

        private static void ApplyV2Marker()
        {
            var node = AssetDatabase.LoadAssetAtPath<DemoGraphNode>(ShopRootAssetPath);

            if (node == null)
            {
                throw new InvalidOperationException(
                    "ShopRoot sample asset not found: " + ShopRootAssetPath);
            }

            if (!node.label.EndsWith(FullChainLayout.V2LabelSuffix, StringComparison.Ordinal))
            {
                node.label += FullChainLayout.V2LabelSuffix;
            }

            EditorUtility.SetDirty(node);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void RestoreV1Content()
        {
            var node = AssetDatabase.LoadAssetAtPath<DemoGraphNode>(ShopRootAssetPath);

            if (node == null)
            {
                return;
            }

            var suffix = FullChainLayout.V2LabelSuffix;

            if (node.label.EndsWith(suffix, StringComparison.Ordinal))
            {
                node.label = node.label.Substring(0, node.label.Length - suffix.Length);
                EditorUtility.SetDirty(node);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.Delete(path, true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FullChain] failed to delete " + path + ": " + exception.Message);
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
