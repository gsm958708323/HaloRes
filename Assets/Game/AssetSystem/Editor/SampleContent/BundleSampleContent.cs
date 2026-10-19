using System;
using System.Collections.Generic;
using Game.AssetSystem.FullChain;
using Game.AssetSystem.Editor.Building;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.SampleContent
{
    public static class BundleSampleContent
    {
        public const string SampleAssetRoot =
            "Assets/Game/AssetSystem/Lesson43Sample";

        public const string SampleContentRoot =
            SampleAssetRoot + "/Content";

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

        public static void GenerateClean()
        {
            Cleanup();

            EnsureFolder(SampleContentRoot + "/UI/Shop");
            EnsureFolder(SampleContentRoot + "/Battle/Fx");
            EnsureFolder(SampleContentRoot + "/Battle/Audio");

            var shopRoot = CreateNode(ShopRootPath, "ShopRoot");
            var shopIcon = CreateNode(ShopIconPath, "ShopIcon");
            var fxA = CreateNode(FxAPath, "FxA");
            var fxB = CreateNode(FxBPath, "FxB");
            var hitSfx = CreateNode(HitSfxPath, "HitSfx");

            AssetDatabase.SaveAssets();

            SetChildren(shopRoot, new[] { shopIcon, fxA });
            SetChildren(fxA, new[] { hitSfx, fxB });
            SetChildren(fxB, Array.Empty<DemoGraphNode>());
            SetChildren(shopIcon, Array.Empty<DemoGraphNode>());
            SetChildren(hitSfx, Array.Empty<DemoGraphNode>());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[Lesson43] clean sample content generated at " +
                SampleContentRoot);
        }

        public static IReadOnlyList<AssetBinding> GetBindings()
        {
            return new List<AssetBinding>
            {
                new AssetBinding
                {
                    sourcePath = ShopRootPath,
                    assetKey = "ui/shop/shoproot",
                    bundleGroupName = "ui_shop"
                },
                new AssetBinding
                {
                    sourcePath = ShopIconPath,
                    assetKey = "ui/shop/shopicon",
                    bundleGroupName = "ui_shop"
                },
                new AssetBinding
                {
                    sourcePath = FxAPath,
                    assetKey = "battle/fx/fxa",
                    bundleGroupName = "battle_fx"
                },
                new AssetBinding
                {
                    sourcePath = FxBPath,
                    assetKey = "battle/fx/fxb",
                    bundleGroupName = "battle_fx"
                },
                new AssetBinding
                {
                    sourcePath = HitSfxPath,
                    assetKey = "battle/audio/hitsfx",
                    bundleGroupName = "battle_audio"
                }
            };
        }

        public static void Cleanup()
        {
            if (!AssetDatabase.IsValidFolder(SampleAssetRoot))
            {
                return;
            }

            AssetDatabase.DeleteAsset(SampleAssetRoot);
            AssetDatabase.Refresh();

            Debug.Log("[Lesson43] sample content cleaned up.");
        }

        private static DemoGraphNode CreateNode(
            string path,
            string label)
        {
            var node =
                ScriptableObject.CreateInstance<DemoGraphNode>();

            node.label = label;
            AssetDatabase.CreateAsset(node, path);
            return node;
        }

        private static void SetChildren(
            DemoGraphNode node,
            DemoGraphNode[] children)
        {
            node.children.Clear();

            for (var i = 0; i < children.Length; i++)
            {
                node.children.Add(children[i]);
            }

            EditorUtility.SetDirty(node);
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
