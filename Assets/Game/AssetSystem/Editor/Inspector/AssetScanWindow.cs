using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.AssetSystem.Editor.Scanning;
using UnityEditor;
using UnityEngine;
using Game.AssetSystem.Editor.SampleContent;

namespace Game.AssetSystem.Editor.Inspector
{
    public sealed class AssetScanWindow : EditorWindow
    {
        private AssetScanReport _lastReport;
        private string _lastReportPath;
        private Vector2 _errorScroll;

        [MenuItem("Tools/Game Asset System/Inspect/1. Scan & Group")]
        private static void Open()
        {
            var window = GetWindow<AssetScanWindow>(
                "Asset Scan & Group");

            window.minSize = new Vector2(560, 420);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Asset Scan & Group",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Scan sample content, generate candidate AssetKeys, " +
                "assign bundle groups, and validate duplicate keys, " +
                "invalid key formats, and unmatched assets.",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("1. Generate Sample Content"))
            {
                try
                {
                    ScanSampleContent.Generate();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog(
                        "Generate Failed",
                        exception.Message,
                        "OK");
                }
            }

            if (GUILayout.Button("2. Run Scan and Validate"))
            {
                try
                {
                    RunScanAndValidate();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog(
                        "Scan Failed",
                        exception.Message,
                        "OK");
                }
            }

            if (GUILayout.Button("3. Cleanup Sample Content"))
            {
                try
                {
                    ScanSampleContent.Cleanup();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog(
                        "Cleanup Failed",
                        exception.Message,
                        "OK");
                }
            }

            EditorGUILayout.Space();

            if (_lastReport == null)
            {
                EditorGUILayout.LabelField(
                    "No scan has been run yet.");
                return;
            }

            EditorGUILayout.LabelField(
                "Scanned assets: " +
                _lastReport.ScannedAssetCount);

            EditorGUILayout.LabelField(
                "Bindings: " +
                _lastReport.Bindings.Count);

            EditorGUILayout.LabelField(
                "Groups: " +
                _lastReport.Groups.Count);

            EditorGUILayout.LabelField(
                "Errors: " +
                _lastReport.Errors.Count);

            EditorGUILayout.LabelField(
                "Warnings: " +
                _lastReport.Warnings.Count);

            EditorGUILayout.LabelField(
                "Fatal errors block manifest generation: " +
                _lastReport.HasFatalErrors,
                EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_lastReportPath))
            {
                EditorGUILayout.LabelField(
                    "Report file: " + _lastReportPath,
                    EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Errors",
                EditorStyles.boldLabel);

            _errorScroll = EditorGUILayout.BeginScrollView(
                _errorScroll,
                GUILayout.MaxHeight(220));

            if (_lastReport.Errors.Count == 0)
            {
                EditorGUILayout.LabelField("No errors.");
            }
            else
            {
                for (var i = 0;
                     i < _lastReport.Errors.Count && i < 50;
                     i++)
                {
                    EditorGUILayout.HelpBox(
                        FormatError(_lastReport.Errors[i]),
                        MessageType.Error);
                }

                if (_lastReport.Errors.Count > 50)
                {
                    EditorGUILayout.HelpBox(
                        "Only first 50 errors are shown. " +
                        "See the report file for the rest.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Warnings",
                EditorStyles.boldLabel);

            if (_lastReport.Warnings.Count == 0)
            {
                EditorGUILayout.LabelField("No warnings.");
            }
            else
            {
                for (var i = 0;
                     i < _lastReport.Warnings.Count && i < 10;
                     i++)
                {
                    EditorGUILayout.HelpBox(
                        FormatWarning(_lastReport.Warnings[i]),
                        MessageType.Warning);
                }
            }
        }

        private void RunScanAndValidate()
        {
            var ruleSet = CreateRuleSet();
            var scanner = new AssetScanner();

            var report = scanner.Scan(
                new[] { ScanSampleContent.SampleRoot },
                new[] { ".txt" },
                ruleSet);

            _lastReport = report;
            _lastReportPath = WriteReport(report);

            Debug.Log(
                "[Scan] scan finished. scanned=" +
                report.ScannedAssetCount +
                " bindings=" + report.Bindings.Count +
                " groups=" + report.Groups.Count +
                " errors=" + report.Errors.Count +
                " warnings=" + report.Warnings.Count +
                " report=" + _lastReportPath);

            if (report.HasFatalErrors)
            {
                Debug.LogError(
                    "[Scan] fatal errors found. " +
                    "Manifest generation must be blocked.");
            }

            Repaint();
        }

        private static AssetGroupRuleSet CreateRuleSet()
        {
            var sampleRoot = ScanSampleContent.SampleRoot;

            var ruleSet = new AssetGroupRuleSet();
            ruleSet.Rules = new List<AssetGroupRule>
            {
                new AssetGroupRule
                {
                    Name = "ui.shop",
                    SourcePathPrefix = sampleRoot + "/UI/Shop/",
                    KeyPrefix = "ui/shop",
                    BundleGroupName = "ui_shop",
                    KeyOverrides = new List<AssetKeyOverride>()
                },
                new AssetGroupRule
                {
                    Name = "battle.fx",
                    SourcePathPrefix = sampleRoot + "/Battle/Fx/",
                    KeyPrefix = "battle/fx",
                    BundleGroupName = "battle_fx",
                    KeyOverrides = new List<AssetKeyOverride>
                    {
                        new AssetKeyOverride
                        {
                            SourcePath = sampleRoot + "/Battle/Fx/FxHit.txt",
                            Key = "battle/fx/fx_hit"
                        },
                        new AssetKeyOverride
                        {
                            SourcePath = sampleRoot + "/Battle/Fx/FxHitAlt.txt",
                            Key = "battle/fx/fx_hit"
                        }
                    }
                },
                new AssetGroupRule
                {
                    Name = "battle.audio",
                    SourcePathPrefix = sampleRoot + "/Battle/Audio/",
                    KeyPrefix = "battle/audio",
                    BundleGroupName = "battle_audio",
                    KeyOverrides = new List<AssetKeyOverride>()
                }
            };

            return ruleSet;
        }

        private static string WriteReport(AssetScanReport report)
        {
            var projectRoot =
                Directory.GetParent(Application.dataPath).FullName;

            var directory = Path.Combine(
                projectRoot,
                "GameAssetSystemBuild",
                "Lesson41");

            Directory.CreateDirectory(directory);

            var path = Path.Combine(
                directory,
                "scan-report.json");

            var json = JsonUtility.ToJson(report, true);

            File.WriteAllText(
                path,
                json,
                new UTF8Encoding(false));

            return path;
        }

        private static string FormatError(AssetScanError error)
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

        private static string FormatWarning(AssetScanWarning warning)
        {
            var builder = new StringBuilder();
            builder.Append(warning.Code);

            if (!string.IsNullOrEmpty(warning.SourcePath))
            {
                builder.Append(" path=");
                builder.Append(warning.SourcePath);
            }

            builder.Append('\n');
            builder.Append(warning.Message);

            return builder.ToString();
        }
    }
}
