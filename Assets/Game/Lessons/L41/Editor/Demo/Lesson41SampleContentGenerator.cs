using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Demo.L41
{
    public static class Lesson41SampleContentGenerator
    {
        public const string SampleRoot =
            "Assets/Game/AssetSystem/Lesson41Sample/Content";

        private const string SampleAssetRoot =
            "Assets/Game/AssetSystem/Lesson41Sample";

        public static void Generate()
        {
            EnsureDirectory(SampleAssetRoot);
            EnsureDirectory(SampleRoot);

            WriteText(
                "UI/Shop/ShopPanel.txt",
                "shop panel sample");

            WriteText(
                "UI/Shop/ShopIcon.txt",
                "shop icon sample");

            WriteText(
                "UI/Shop/Invalid Name.txt",
                "this file intentionally generates an invalid key");

            WriteText(
                "Battle/Fx/FxHit.txt",
                "fx hit sample");

            WriteText(
                "Battle/Fx/FxHitAlt.txt",
                "fx hit alt sample");

            WriteText(
                "Battle/Audio/HitSfx.txt",
                "hit sfx sample");

            WriteText(
                "Battle/Unmatched/Unmatched.txt",
                "no group rule matches this file");

            AssetDatabase.Refresh();

            Debug.Log(
                "[Lesson41] sample content generated at " +
                SampleRoot);
        }

        public static void Cleanup()
        {
            if (!AssetDatabase.IsValidFolder(SampleAssetRoot))
            {
                Debug.Log(
                    "[Lesson41] sample content does not exist.");
                return;
            }

            AssetDatabase.DeleteAsset(SampleAssetRoot);
            AssetDatabase.Refresh();

            Debug.Log(
                "[Lesson41] sample content cleaned up.");
        }

        private static void WriteText(
            string relativePath,
            string content)
        {
            var path = Path.Combine(SampleRoot, relativePath);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                EnsureDirectory(directory);
            }

            File.WriteAllText(
                path,
                content,
                new UTF8Encoding(false));
        }

        private static void EnsureDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(path);
        }
    }
}
