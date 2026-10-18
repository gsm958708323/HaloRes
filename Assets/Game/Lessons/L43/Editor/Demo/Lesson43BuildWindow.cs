using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.AssetSystem.Editor.Building.L43;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Demo.L43
{
    public sealed class Lesson43BuildWindow : EditorWindow
    {
        private string _contentVersion = "lesson43-v1";

        private Lesson43BuildResult _lastBuild;
        private Lesson43ValidationResult _lastValidation;

        private Vector2 _scroll;

        [MenuItem(
            "Tools/Game Asset System/Lesson 4.3 AssetBundle Build & Platform Variant")]
        private static void Open()
        {
            var window =
                GetWindow<Lesson43BuildWindow>(
                    "4.3 Bundle Build");

            window.minSize = new Vector2(680, 560);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "AssetBundle Build & Platform Variant",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Generate sample runtime assets, build asset bundles " +
                "for the current active build target, backfill real " +
                "internal asset paths, and validate bundles by loading " +
                "them in the Editor.",
                MessageType.Info);

            _contentVersion = EditorGUILayout.TextField(
                "Content Version",
                _contentVersion);

            EditorGUILayout.LabelField(
                "Active Build Target",
                EditorUserBuildSettings.activeBuildTarget.ToString());

            EditorGUILayout.Space();

            if (GUILayout.Button("1. Generate Clean Sample Content"))
            {
                TryRun(Lesson43SampleContentGenerator.GenerateClean);
            }

            if (GUILayout.Button("2. Build Bundles For Current Target"))
            {
                TryRun(BuildForCurrentTarget);
            }

            if (GUILayout.Button("3. Validate Built Bundles"))
            {
                TryRun(ValidateBuiltBundles);
            }

            if (GUILayout.Button("4. Simulate Wrong Platform Validation"))
            {
                TryRun(SimulateWrongPlatformValidation);
            }

            if (GUILayout.Button("5. Corrupt First Built Bundle"))
            {
                TryRun(CorruptFirstBuiltBundle);
            }

            if (GUILayout.Button("6. Cleanup Sample And Build Output"))
            {
                TryRun(CleanupAll);
            }

            EditorGUILayout.Space();

            DrawBuildSummary();
            DrawValidationSummary();
            DrawErrors();
        }

        private void BuildForCurrentTarget()
        {
            if (!Lesson43PlatformVariantPolicy.IsSimpleName(
                _contentVersion))
            {
                throw new InvalidOperationException(
                    "Content version must be a simple file name. " +
                    "value=" + _contentVersion);
            }

            var bindings = Lesson43SampleContentGenerator.GetBindings();
            var target = EditorUserBuildSettings.activeBuildTarget;

            var builder = new Lesson43AssetBundleBuilder();

            _lastBuild = builder.Build(
                bindings,
                _contentVersion.Trim(),
                target);

            _lastValidation = null;

            if (_lastBuild.Success)
            {
                Debug.Log(
                    "[Lesson43] build succeeded. target=" +
                    _lastBuild.TargetPlatformKey +
                    " assets=" +
                    (_lastBuild.Manifest == null
                        ? 0
                        : _lastBuild.Manifest.assets.Count) +
                    " bundles=" +
                    _lastBuild.Bundles.Count +
                    " output=" +
                    _lastBuild.OutputDirectory);
            }
            else
            {
                Debug.LogError(
                    "[Lesson43] build failed. errors=" +
                    _lastBuild.Errors.Count);
            }

            Repaint();
        }

        private void ValidateBuiltBundles()
        {
            if (_lastBuild == null ||
                _lastBuild.Manifest == null ||
                string.IsNullOrEmpty(_lastBuild.OutputDirectory))
            {
                throw new InvalidOperationException(
                    "Build a valid manifest first.");
            }

            var bindings = Lesson43SampleContentGenerator.GetBindings();
            var target = EditorUserBuildSettings.activeBuildTarget;

            var validator = new Lesson43BundleValidator();

            _lastValidation = validator.Validate(
                _lastBuild.OutputDirectory,
                _lastBuild.Manifest,
                bindings,
                target);

            if (_lastValidation.Success)
            {
                Debug.Log(
                    "[Lesson43] validation succeeded. assets=" +
                    _lastValidation.AssetsChecked +
                    " bundlesLoaded=" +
                    _lastValidation.BundlesLoaded);
            }
            else
            {
                Debug.LogError(
                    "[Lesson43] validation failed. errors=" +
                    _lastValidation.Errors.Count);
            }

            Repaint();
        }

        private void SimulateWrongPlatformValidation()
        {
            if (_lastBuild == null ||
                _lastBuild.Manifest == null ||
                string.IsNullOrEmpty(_lastBuild.OutputDirectory))
            {
                throw new InvalidOperationException(
                    "Build a valid manifest first.");
            }

            var current = EditorUserBuildSettings.activeBuildTarget;
            var wrongTarget = current == BuildTarget.Android
                ? BuildTarget.StandaloneWindows64
                : BuildTarget.Android;

            var bindings = Lesson43SampleContentGenerator.GetBindings();

            var validator = new Lesson43BundleValidator();

            _lastValidation = validator.Validate(
                _lastBuild.OutputDirectory,
                _lastBuild.Manifest,
                bindings,
                wrongTarget);

            Debug.Log(
                "[Lesson43] wrong-platform validation executed. " +
                "actual=" + _lastBuild.TargetPlatformKey +
                " requested=" +
                Lesson43PlatformVariantPolicy.GetPlatformKey(
                    wrongTarget) +
                " success=" + _lastValidation.Success);

            Repaint();
        }

        private void CorruptFirstBuiltBundle()
        {
            if (_lastBuild == null ||
                _lastBuild.Manifest == null ||
                _lastBuild.Manifest.bundles.Count == 0)
            {
                throw new InvalidOperationException(
                    "Build a valid manifest first.");
            }

            var record = _lastBuild.Manifest.bundles[0];
            var path = Path.Combine(
                _lastBuild.OutputDirectory,
                record.bundleFileName);

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    "Bundle file does not exist: " + path);
            }

            File.AppendAllText(
                path,
                "lesson43-corruption",
                new UTF8Encoding(false));

            Debug.LogWarning(
                "[Lesson43] corrupted first built bundle: " + path);

            Repaint();
        }

        private void CleanupAll()
        {
            Lesson43SampleContentGenerator.Cleanup();

            var projectRoot =
                Lesson43PlatformVariantPolicy.GetProjectRoot();

            var buildRoot =
                Lesson43PlatformVariantPolicy.GetBuildRoot(projectRoot);

            if (Directory.Exists(buildRoot))
            {
                Directory.Delete(buildRoot, true);
            }

            _lastBuild = null;
            _lastValidation = null;

            Debug.Log("[Lesson43] sample and build output cleaned up.");

            Repaint();
        }

        private void DrawBuildSummary()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Build Summary",
                EditorStyles.boldLabel);

            if (_lastBuild == null)
            {
                EditorGUILayout.LabelField(
                    "No build has been run yet.");
                return;
            }

            EditorGUILayout.LabelField(
                "Success: " + _lastBuild.Success);

            EditorGUILayout.LabelField(
                "Target: " + _lastBuild.TargetPlatformKey);

            EditorGUILayout.LabelField(
                "Content Version: " + _lastBuild.ContentVersion);

            EditorGUILayout.LabelField(
                "Output: " + _lastBuild.OutputDirectory,
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.LabelField(
                "Bundles: " + _lastBuild.Bundles.Count);

            EditorGUILayout.LabelField(
                "Build errors: " + _lastBuild.Errors.Count);

            if (_lastBuild.Manifest != null)
            {
                EditorGUILayout.LabelField(
                    "Manifest assets: " +
                    _lastBuild.Manifest.assets.Count);
            }
        }

        private void DrawValidationSummary()
        {
            if (_lastValidation == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Validation Summary",
                EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                "Success: " + _lastValidation.Success);

            EditorGUILayout.LabelField(
                "Assets checked: " +
                _lastValidation.AssetsChecked);

            EditorGUILayout.LabelField(
                "Bundles loaded: " +
                _lastValidation.BundlesLoaded);

            EditorGUILayout.LabelField(
                "Validation errors: " +
                _lastValidation.Errors.Count);
        }

        private void DrawErrors()
        {
            var hasBuildErrors =
                _lastBuild != null &&
                _lastBuild.Errors.Count > 0;

            var hasValidationErrors =
                _lastValidation != null &&
                _lastValidation.Errors.Count > 0;

            if (!hasBuildErrors && !hasValidationErrors)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Errors",
                EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll,
                GUILayout.MaxHeight(320));

            if (hasBuildErrors)
            {
                for (var i = 0;
                     i < _lastBuild.Errors.Count && i < 50;
                     i++)
                {
                    EditorGUILayout.HelpBox(
                        FormatError(_lastBuild.Errors[i]),
                        MessageType.Error);
                }
            }

            if (hasValidationErrors)
            {
                for (var i = 0;
                     i < _lastValidation.Errors.Count && i < 50;
                     i++)
                {
                    EditorGUILayout.HelpBox(
                        FormatError(_lastValidation.Errors[i]),
                        MessageType.Error);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static string FormatError(
            Lesson43BuildError error)
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

            builder.Append('\n');
            builder.Append(error.Message);

            return builder.ToString();
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
                    "Lesson 4.3",
                    exception.Message,
                    "OK");
            }
        }
    }
}
