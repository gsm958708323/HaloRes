using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Demo.L42
{
    public static class Lesson42SampleContentGenerator
    {
        public const string SampleAssetRoot =
            "Assets/Game/AssetSystem/Lesson42Sample";

        public const string SampleContentRoot =
            SampleAssetRoot + "/Content";

        public const string ExternalRoot =
            SampleAssetRoot + "/External";

        private const string ShopRootPath =
            SampleContentRoot + "/UI/Shop/ShopRoot.asset";

        private const string ShopIconPath =
            SampleContentRoot + "/UI/Shop/ShopIcon.asset";

        private const string FxAPath =
            SampleContentRoot + "/Battle/Fx/FxA.asset";

        private const string FxBPath =
            SampleContentRoot + "/Battle/Fx/FxB.asset";

        private const string HitSfxPath =
            SampleContentRoot + "/Battle/Audio/HitSfx.asset";

        private const string ExternalPath =
            ExternalRoot + "/UnmappedDependency.asset";

        public static void GenerateClean()
        {
            Cleanup();

            EnsureFolder(SampleContentRoot + "/UI/Shop");
            EnsureFolder(SampleContentRoot + "/Battle/Fx");
            EnsureFolder(SampleContentRoot + "/Battle/Audio");
            EnsureFolder(ExternalRoot);

            var shopRoot = CreateNode(ShopRootPath, "ShopRoot");
            var shopIcon = CreateNode(ShopIconPath, "ShopIcon");
            var fxA = CreateNode(FxAPath, "FxA");
            var fxB = CreateNode(FxBPath, "FxB");
            var hitSfx = CreateNode(HitSfxPath, "HitSfx");
            var external = CreateNode(ExternalPath, "UnmappedDependency");

            AssetDatabase.SaveAssets();

            SetChildren(shopRoot, new[] { shopIcon, fxA });
            SetChildren(fxA, new[] { hitSfx, fxB });
            SetChildren(fxB, new Lesson42GraphNode[0]);
            SetChildren(shopIcon, new Lesson42GraphNode[0]);
            SetChildren(hitSfx, new Lesson42GraphNode[0]);
            SetChildren(external, new Lesson42GraphNode[0]);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[Lesson42] clean sample graph generated at " +
                SampleAssetRoot);
        }

        public static void InjectMissingDependency()
        {
            EnsureCleanSampleExists();

            var shopRoot = LoadNode(ShopRootPath);
            var external =
                AssetDatabase.LoadAssetAtPath<Lesson42GraphNode>(
                    ExternalPath);

            if (external == null)
            {
                external = CreateNode(
                    ExternalPath,
                    "UnmappedDependency");
                AssetDatabase.SaveAssets();
            }

            if (!ContainsNode(shopRoot.children, external))
            {
                shopRoot.children.Add(external);
                EditorUtility.SetDirty(shopRoot);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log(
                "[Lesson42] injected missing dependency: " +
                ExternalPath);
        }

        public static void InjectCycle()
        {
            EnsureCleanSampleExists();

            var fxB = LoadNode(FxBPath);
            var shopRoot = LoadNode(ShopRootPath);

            if (!ContainsNode(fxB.children, shopRoot))
            {
                fxB.children.Add(shopRoot);
                EditorUtility.SetDirty(fxB);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log(
                "[Lesson42] injected bundle cycle: " +
                "FxB(battle_fx) -> ShopRoot(ui_shop)");
        }

        public static void Cleanup()
        {
            if (!AssetDatabase.IsValidFolder(SampleAssetRoot))
            {
                return;
            }

            AssetDatabase.DeleteAsset(SampleAssetRoot);
            AssetDatabase.Refresh();

            Debug.Log("[Lesson42] sample content cleaned up.");
        }

        private static void EnsureCleanSampleExists()
        {
            if (!AssetDatabase.IsValidFolder(SampleContentRoot))
            {
                throw new InvalidOperationException(
                    "Generate clean sample graph first.");
            }
        }

        private static Lesson42GraphNode CreateNode(
            string path,
            string label)
        {
            var node =
                ScriptableObject.CreateInstance<Lesson42GraphNode>();

            node.label = label;
            AssetDatabase.CreateAsset(node, path);
            return node;
        }

        private static Lesson42GraphNode LoadNode(string path)
        {
            var node =
                AssetDatabase.LoadAssetAtPath<Lesson42GraphNode>(path);

            if (node == null)
            {
                throw new InvalidOperationException(
                    "Sample node not found: " + path);
            }

            return node;
        }

        private static void SetChildren(
            Lesson42GraphNode node,
            Lesson42GraphNode[] children)
        {
            node.children.Clear();

            for (var i = 0; i < children.Length; i++)
            {
                node.children.Add(children[i]);
            }

            EditorUtility.SetDirty(node);
        }

        private static bool ContainsNode(
            List<Lesson42GraphNode> list,
            Lesson42GraphNode node)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], node))
                {
                    return true;
                }
            }

            return false;
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
    }
}
