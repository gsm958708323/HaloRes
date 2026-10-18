using System;
using System.Text;
using Game.AssetSystem.Demo.L43;
using Game.AssetSystem.Editor.Building.L43;
using Game.AssetSystem.Editor.Demo.L43;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.FullChain.Editor
{
    /// <summary>
    /// Generates the assets the full-chain demo needs:
    /// sample ScriptableObject content -> two real AssetBundle sets (v1 / v2)
    /// with manifests, under GameAssetSystemBuild/Lesson43/&lt;platform&gt;/.
    /// </summary>
    public static class FullChainContentBuilder
    {
        private const string ShopRootAssetPath =
            Lesson43SampleContentGenerator.SampleContentRoot + "/UI/Shop/ShopRoot.asset";

        [MenuItem("Tools/Game Asset System/Build Full-Chain Demo Content")]
        public static void BuildAll()
        {
            Lesson43SampleContentGenerator.GenerateClean();

            var bindings = Lesson43SampleContentGenerator.GetBindings();
            var target = EditorUserBuildSettings.activeBuildTarget;
            var builder = new Lesson43AssetBundleBuilder();

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

            ApplyV2Marker();

            var v2 = builder.Build(bindings, FullChainLayout.V2Version, target);

            RestoreV1Content();

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
                "[FullChain] content ready. platform=" + FullChainLayout.PlatformKey +
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
            Lesson43SampleContentGenerator.Cleanup();
            Debug.Log("[FullChain] content cleaned.");
        }

        private static void ApplyV2Marker()
        {
            var node = AssetDatabase.LoadAssetAtPath<Lesson43GraphNode>(ShopRootAssetPath);

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
            var node = AssetDatabase.LoadAssetAtPath<Lesson43GraphNode>(ShopRootAssetPath);

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

        private static string Describe(Lesson43BuildResult result)
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
