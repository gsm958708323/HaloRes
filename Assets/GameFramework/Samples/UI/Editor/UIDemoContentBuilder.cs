using System;
using System.IO;
using GameFramework.Asset.Editor.Build;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GameFramework.UI.Samples.Editor
{
    public static class UIDemoContentBuilder
    {
        public const string Folder = "Assets/GameFramework/Samples/UI";
        public const string ScenePath = Folder + "/UI.unity";
        public const string ProfilePath = Folder + "/Config/UIBuildProfile.asset";
        public const string BundleRoot = "Build/UIContent";

        [MenuItem("Game Framework/UI/Stage Sample Bundles")]
        public static void Stage()
        {
            StageForTarget(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void StageForTarget(BuildTarget target)
        {
            var profile = AssetDatabase.LoadAssetAtPath<AssetBuildProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("UI sample build profile is missing.");
            var platform = BundleBuilder.PlatformName(target);
            var root = BundleBuilder.Build(profile, Path.Combine(BundleRoot, platform, "v" + profile.version), target);
            var manifest = AssetManifest.FromFile(Path.Combine(root, "asset_manifest.json"));
            var destination = Path.Combine(Application.streamingAssetsPath, "GameFrameworkUI", platform);
            Directory.CreateDirectory(Path.Combine(destination, "bundles"));
            Directory.CreateDirectory(Path.Combine(destination, "manifests"));
            foreach (var bundle in manifest.Bundles)
                File.Copy(Path.Combine(root, "bundles", bundle.Hash + ".bundle"),
                    Path.Combine(destination, "bundles", bundle.Hash + ".bundle"), true);
            var version = JsonUtility.FromJson<VersionFile>(File.ReadAllText(Path.Combine(root, "version.json")));
            File.Copy(Path.Combine(root, "manifests", version.manifestHash + ".json"),
                Path.Combine(destination, "manifests", version.manifestHash + ".json"), true);
            File.Copy(Path.Combine(root, "version.json"), Path.Combine(destination, "version.json"), true);
            AssetDatabase.Refresh();
        }

        [MenuItem("Game Framework/UI/Build Windows Sample Player")]
        public static void BuildPlayer()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building the UI sample.");
            StageForTarget(BuildTarget.StandaloneWindows64);
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Build/UIPlayer/HaloFrameUI.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.Development | BuildOptions.StrictMode);
            Directory.CreateDirectory("Build/Verification");
            File.WriteAllText("Build/Verification/ui-player-build.txt", report.summary.result + "\n"
                + report.summary.buildEndedAt.ToString("o") + "\n" + report.summary.outputPath);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("UI sample player build failed: " + report.summary.result);
        }
    }
}
