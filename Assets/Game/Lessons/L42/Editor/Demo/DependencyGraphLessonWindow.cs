using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.AssetSystem.Editor.Manifest.L42;
using Game.AssetSystem.Editor.Scanning.L42;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Demo.L42
{
    public sealed class DependencyGraphLessonWindow : EditorWindow
    {
        private string _contentVersion = "lesson42-v1";

        private AssetScanReport _lastScanReport;
        private BundleGraphBuildResult _lastBuildResult;
        private string _manifestPath;

        private Vector2 _scroll;

        [MenuItem(
            "Tools/Game Asset System/Lesson 4.2 Dependency Graph & Manifest")]
        private static void Open()
        {
            var window =
                GetWindow<DependencyGraphLessonWindow>(
                    "4.2 Dependency & Manifest");

            window.minSize = new Vector2(640, 520);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Dependency Graph & Manifest Generation",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Generate a clean graph, inject failures, scan assets, " +
                "compute bundle dependencies, and write a runtime " +
                "manifest only when validation passes.",
                MessageType.Info);

            _contentVersion = EditorGUILayout.TextField(
                "Content Version",
                _contentVersion);

            EditorGUILayout.Space();

            if (GUILayout.Button("1. Generate Clean Sample Graph"))
            {
                TryRun(Lesson42SampleContentGenerator.GenerateClean);
            }

            if (GUILayout.Button("2. Inject Missing Dependency"))
            {
                TryRun(
                    Lesson42SampleContentGenerator.InjectMissingDependency);
            }

            if (GUILayout.Button("3. Inject Bundle Cycle"))
            {
                TryRun(Lesson42SampleContentGenerator.InjectCycle);
            }

            if (GUILayout.Button("4. Run Scan + Build Manifest"))
            {
                TryRun(RunScanAndBuild);
            }

            if (GUILayout.Button("5. Cleanup Sample Content"))
            {
                TryRun(Lesson42SampleContentGenerator.Cleanup);
            }

            EditorGUILayout.Space();

            DrawScanSummary();
            DrawBuildSummary();
            DrawDetailsIfNeeded();
        }

        private void RunScanAndBuild()
        {
            if (!IsSimpleFileName(_contentVersion))
            {
                throw new InvalidOperationException(
                    "Content version must be a simple file name. " +
                    "value=" + _contentVersion);
            }

            var ruleSet = CreateRuleSet();
            var scanner = new AssetScanner();

            var scanReport = scanner.Scan(
                new[]
                {
                    Lesson42SampleContentGenerator.SampleContentRoot
                },
                new[] { ".asset" },
                ruleSet);

            _lastScanReport = scanReport;

            var buildDirectory = GetBuildDirectory();
            Directory.CreateDirectory(buildDirectory);

            WriteJson(
                Path.Combine(buildDirectory, "scan-report.json"),
                scanReport);

            if (scanReport.HasFatalErrors)
            {
                _lastBuildResult = null;
                _manifestPath = null;

                Debug.LogError(
                    "[Lesson42] scan stage blocked manifest generation. " +
                    "errors=" + scanReport.Errors.Count);

                Repaint();
                return;
            }

            var builder = new BundleDependencyBuilder();

            var buildResult = builder.Build(
                scanReport,
                _contentVersion.Trim());

            _lastBuildResult = buildResult;

            WriteJson(
                Path.Combine(
                    buildDirectory,
                    "bundle-graph-report.json"),
                buildResult);

            if (buildResult.Success &&
                buildResult.Manifest != null)
            {
                _manifestPath = Path.Combine(
                    buildDirectory,
                    "manifest-" +
                    _contentVersion.Trim() +
                    ".json");

                WriteJson(
                    _manifestPath,
                    buildResult.Manifest);

                Debug.Log(
                    "[Lesson42] manifest generated at " +
                    _manifestPath);
            }
            else
            {
                _manifestPath = null;

                Debug.LogError(
                    "[Lesson42] manifest generation blocked. " +
                    "errors=" + buildResult.Errors.Count);
            }

            Debug.Log(
                "[Lesson42] scan assets=" +
                scanReport.ScannedAssetCount +
                " bindings=" + scanReport.Bindings.Count +
                " buildSuccess=" + buildResult.Success +
                " assets=" + buildResult.AssetCount +
                " bundles=" + buildResult.BundleCount +
                " edges=" + buildResult.Edges.Count +
                " errors=" + buildResult.Errors.Count +
                " warnings=" + buildResult.Warnings.Count);

            Repaint();
        }

        private static AssetGroupRuleSet CreateRuleSet()
        {
            var sampleRoot =
                Lesson42SampleContentGenerator.SampleContentRoot;

            var ruleSet = new AssetGroupRuleSet();

            ruleSet.Rules = new List<AssetGroupRule>
            {
                new AssetGroupRule
                {
                    Name = "ui.shop",
                    SourcePathPrefix =
                        sampleRoot + "/UI/Shop/",
                    KeyPrefix = "ui/shop",
                    BundleGroupName = "ui_shop",
                    KeyOverrides = new List<AssetKeyOverride>()
                },
                new AssetGroupRule
                {
                    Name = "battle.fx",
                    SourcePathPrefix =
                        sampleRoot + "/Battle/Fx/",
                    KeyPrefix = "battle/fx",
                    BundleGroupName = "battle_fx",
                    KeyOverrides = new List<AssetKeyOverride>()
                },
                new AssetGroupRule
                {
                    Name = "battle.audio",
                    SourcePathPrefix =
                        sampleRoot + "/Battle/Audio/",
                    KeyPrefix = "battle/audio",
                    BundleGroupName = "battle_audio",
                    KeyOverrides = new List<AssetKeyOverride>()
                }
            };

            return ruleSet;
        }

        private void DrawScanSummary()
        {
            if (_lastScanReport == null)
            {
                EditorGUILayout.LabelField(
                    "No scan has been run yet.");
                return;
            }

            EditorGUILayout.LabelField(
                "Scanned assets: " +
                _lastScanReport.ScannedAssetCount);

            EditorGUILayout.LabelField(
                "Bindings: " +
                _lastScanReport.Bindings.Count);

            EditorGUILayout.LabelField(
                "Scan errors: " +
                _lastScanReport.Errors.Count);

            EditorGUILayout.LabelField(
                "Scan warnings: " +
                _lastScanReport.Warnings.Count);

            if (_lastScanReport.HasFatalErrors)
            {
                EditorGUILayout.HelpBox(
                    "Scan stage blocked manifest generation.",
                    MessageType.Error);
            }
        }

        private void DrawBuildSummary()
        {
            if (_lastBuildResult == null)
            {
                return;
            }

            EditorGUILayout.LabelField(
                "Build success: " +
                _lastBuildResult.Success);

            EditorGUILayout.LabelField(
                "Manifest assets: " +
                _lastBuildResult.AssetCount);

            EditorGUILayout.LabelField(
                "Manifest bundles: " +
                _lastBuildResult.BundleCount);

            EditorGUILayout.LabelField(
                "Bundle edges: " +
                _lastBuildResult.Edges.Count);

            EditorGUILayout.LabelField(
                "Build errors: " +
                _lastBuildResult.Errors.Count);

            EditorGUILayout.LabelField(
                "Build warnings: " +
                _lastBuildResult.Warnings.Count);

            if (!string.IsNullOrEmpty(_manifestPath))
            {
                EditorGUILayout.LabelField(
                    "Manifest file: " + _manifestPath,
                    EditorStyles.wordWrappedLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Manifest was not written because validation failed.",
                    MessageType.Warning);
            }
        }

        private void DrawDetailsIfNeeded()
        {
            var hasScanErrors =
                _lastScanReport != null &&
                _lastScanReport.HasFatalErrors;

            if (!hasScanErrors && _lastBuildResult == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Details",
                EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll,
                GUILayout.MaxHeight(320));

            if (hasScanErrors)
            {
                EditorGUILayout.LabelField(
                    "Scan errors",
                    EditorStyles.boldLabel);

                for (var i = 0;
                     i < _lastScanReport.Errors.Count && i < 20;
                     i++)
                {
                    EditorGUILayout.HelpBox(
                        FormatScanError(
                            _lastScanReport.Errors[i]),
                        MessageType.Error);
                }
            }

            if (_lastBuildResult != null)
            {
                if (_lastBuildResult.Errors.Count > 0)
                {
                    EditorGUILayout.LabelField(
                        "Build errors",
                        EditorStyles.boldLabel);

                    for (var i = 0;
                         i < _lastBuildResult.Errors.Count && i < 20;
                         i++)
                    {
                        EditorGUILayout.HelpBox(
                            FormatBundleGraphError(
                                _lastBuildResult.Errors[i]),
                            MessageType.Error);
                    }
                }

                if (_lastBuildResult.Warnings.Count > 0)
                {
                    EditorGUILayout.LabelField(
                        "Build warnings",
                        EditorStyles.boldLabel);

                    for (var i = 0;
                         i < _lastBuildResult.Warnings.Count && i < 20;
                         i++)
                    {
                        EditorGUILayout.HelpBox(
                            FormatBundleGraphWarning(
                                _lastBuildResult.Warnings[i]),
                            MessageType.Warning);
                    }
                }

                if (_lastBuildResult.Edges.Count > 0)
                {
                    EditorGUILayout.LabelField(
                        "Bundle edges",
                        EditorStyles.boldLabel);

                    for (var i = 0;
                         i < _lastBuildResult.Edges.Count;
                         i++)
                    {
                        EditorGUILayout.LabelField(
                            FormatEdge(
                                _lastBuildResult.Edges[i]));
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static string FormatScanError(
            AssetScanError error)
        {
            var builder = new StringBuilder();
            builder.Append(error.Code);

            if (!string.IsNullOrEmpty(error.AssetKey))
            {
                builder.Append(" key=");
                builder.Append(error.AssetKey);
            }

            if (!string.IsNullOrEmpty(error.SourcePath))
            {
                builder.Append(" path=");
                builder.Append(error.SourcePath);
            }

            builder.Append('\n');
            builder.Append(error.Message);

            return builder.ToString();
        }

        private static string FormatBundleGraphError(
            BundleGraphError error)
        {
            var builder = new StringBuilder();
            builder.Append(error.Code);

            if (!string.IsNullOrEmpty(error.AssetKey))
            {
                builder.Append(" key=");
                builder.Append(error.AssetKey);
            }

            if (!string.IsNullOrEmpty(error.SourcePath))
            {
                builder.Append(" source=");
                builder.Append(error.SourcePath);
            }

            if (!string.IsNullOrEmpty(error.DependencyPath))
            {
                builder.Append(" dependency=");
                builder.Append(error.DependencyPath);
            }

            builder.Append('\n');
            builder.Append(error.Message);

            return builder.ToString();
        }

        private static string FormatBundleGraphWarning(
            BundleGraphWarning warning)
        {
            return warning.Code + "\n" + warning.Message;
        }

        private static string FormatEdge(
            BundleDependencyEdgeRecord edge)
        {
            return edge.FromBundleGroupName +
                   "(" + edge.FromBundleId + ")" +
                   " -> " +
                   edge.ToBundleGroupName +
                   "(" + edge.ToBundleId + ")" +
                   " via " + edge.DependencyPath;
        }

        private static string GetBuildDirectory()
        {
            var projectRoot =
                Directory.GetParent(Application.dataPath).FullName;

            return Path.Combine(
                projectRoot,
                "GameAssetSystemBuild",
                "Lesson42");
        }

        private static void WriteJson<T>(
            string path,
            T value)
        {
            var json = JsonUtility.ToJson(value, true);

            File.WriteAllText(
                path,
                json,
                new UTF8Encoding(false));
        }

        private static bool IsSimpleFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value == "." || value == "..")
            {
                return false;
            }

            if (value.IndexOf('/') >= 0 ||
                value.IndexOf('\\') >= 0)
            {
                return false;
            }

            return value.IndexOfAny(
                Path.GetInvalidFileNameChars()) < 0;
        }

        private static void TryRun(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                EditorUtility.DisplayDialog(
                    "Lesson 4.2",
                    exception.Message,
                    "OK");
            }
        }
    }
}
