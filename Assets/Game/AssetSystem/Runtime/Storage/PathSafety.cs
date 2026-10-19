using System;
using System.IO;

namespace Game.AssetSystem.Storage
{
    /// <summary>
    /// Guards every place that joins a manifest-supplied file name onto a
    /// directory. A manifest is data, not code: <c>..</c>, <c>/</c> and
    /// <c>\</c> must never be able to address a path outside the content root.
    /// </summary>
    public static class PathSafety
    {
        /// <summary>A name with no directory part and no invalid characters.</summary>
        public static bool IsSimpleFileName(string value)
        {
            return AssetFileStore.IsSimpleFileName(value);
        }

        /// <summary>
        /// Resolves <paramref name="fileName"/> under
        /// <paramref name="rootDirectory"/> and verifies the result really is a
        /// direct child of that root.
        /// </summary>
        public static bool TryResolveChild(
            string rootDirectory,
            string fileName,
            out string fullPath,
            out string error)
        {
            fullPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                error = "Root directory is empty.";
                return false;
            }

            if (!AssetFileStore.IsSimpleFileName(fileName))
            {
                error = "Invalid file name: " + (fileName ?? "<null>");
                return false;
            }

            string rootFull;
            string candidate;

            try
            {
                rootFull = Path.GetFullPath(rootDirectory);
                candidate = Path.GetFullPath(Path.Combine(rootFull, fileName));
            }
            catch (Exception exception)
            {
                error = "Failed to resolve path: " +
                        exception.GetType().Name + ": " + exception.Message;
                return false;
            }

            var prefix = rootFull.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "Path escapes the root directory: " + candidate;
                return false;
            }

            fullPath = candidate;
            return true;
        }
    }
}
