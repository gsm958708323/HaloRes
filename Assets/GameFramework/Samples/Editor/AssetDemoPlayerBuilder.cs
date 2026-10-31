using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GameFramework.Asset.Samples.Editor
{
    public static class AssetDemoPlayerBuilder
    {
        public const string OutputPath = "Build/Player/AssetLifecycleLab.exe";

        [MenuItem("Game Framework/Assets/Build Windows Demo Player")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building.");
            AssetDemoContentBuilder.StageForTarget(BuildTarget.StandaloneWindows64);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { AssetDemoContentBuilder.LauncherPath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.StrictMode
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Player build failed: " + report.summary.result);
            Debug.Log("Asset demo player built: " + Path.GetFullPath(OutputPath));
        }
    }
}
