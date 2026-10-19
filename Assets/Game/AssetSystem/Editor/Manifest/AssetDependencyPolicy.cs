using System;
using System.IO;

namespace Game.AssetSystem.Editor.Manifest
{
    public static class AssetDependencyPolicy
    {
        public static bool ShouldIgnore(
            string dependencyPath,
            out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(dependencyPath))
            {
                reason = "empty path";
                return true;
            }

            var normalized = dependencyPath.Replace('\\', '/');

            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                reason = "non-asset dependency";
                return true;
            }

            var extension = Path.GetExtension(normalized).ToLowerInvariant();

            if (extension == ".cs" ||
                extension == ".asmdef" ||
                extension == ".asmref" ||
                extension == ".dll")
            {
                reason = "compiled code";
                return true;
            }

            return false;
        }
    }
}
