using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Building
{
    public static class PlatformVariantPolicy
    {
        public static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        public static string GetPlatformKey(BuildTarget target)
        {
            return target.ToString();
        }

        public static string GetBuildRoot(string projectRoot)
        {
            return Path.Combine(
                projectRoot,
                "GameAssetSystemBuild",
                "Lesson43");
        }

        public static string GetPlatformOutputDirectory(
            string projectRoot,
            string contentVersion,
            BuildTarget target)
        {
            return Path.Combine(
                GetBuildRoot(projectRoot),
                GetPlatformKey(target),
                contentVersion);
        }

        public static bool ValidateManifestPlatform(
            PlatformManifest manifest,
            BuildTarget requestedTarget,
            out string error)
        {
            error = null;

            if (manifest == null)
            {
                error = "Manifest is null.";
                return false;
            }

            var requestedPlatformKey =
                GetPlatformKey(requestedTarget);

            if (string.IsNullOrEmpty(manifest.targetPlatformKey))
            {
                error = "Manifest targetPlatformKey is empty.";
                return false;
            }

            if (!string.Equals(
                manifest.targetPlatformKey,
                requestedPlatformKey,
                StringComparison.Ordinal))
            {
                error = "Platform mismatch. manifest=" +
                        manifest.targetPlatformKey +
                        ", requested=" + requestedPlatformKey;
                return false;
            }

            if (!string.IsNullOrEmpty(manifest.buildTarget) &&
                !string.Equals(
                    manifest.buildTarget,
                    requestedTarget.ToString(),
                    StringComparison.Ordinal))
            {
                error = "Manifest buildTarget mismatch. manifest=" +
                        manifest.buildTarget +
                        ", requested=" + requestedTarget;
                return false;
            }

            return true;
        }

        public static bool IsSimpleName(string value)
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
    }
}
