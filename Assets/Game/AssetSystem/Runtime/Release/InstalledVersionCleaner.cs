using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

using Game.AssetSystem.Core;

namespace Game.AssetSystem.Release
{
    public sealed class CleanupReport
    {
        public string ActiveVersion;
        public int RetainNonActiveVersions;
        public int Deleted;
        public int Retained;
        public int SkippedBecauseStaging;
        public List<string> DeletedVersions = new List<string>();
        public List<string> RetainedVersions = new List<string>();
        public List<string> ErrorMessages = new List<string>();

        public override string ToString()
        {
            return "active=" + ActiveVersion +
                   " retain=" + RetainNonActiveVersions +
                   " deleted=" + Deleted +
                   " retained=" + Retained +
                   " stagingSkipped=" + SkippedBecauseStaging +
                   " errors=" + ErrorMessages.Count;
        }
    }

    public sealed class InstalledVersionCleaner
    {
        public CleanupReport Clean(
            string contentRoot,
            string stagingRoot,
            string activeVersion,
            int retainNonActiveVersions)
        {
            MainThreadPump.EnsureMainThread(nameof(Clean));

            var report = new CleanupReport
            {
                ActiveVersion = activeVersion,
                RetainNonActiveVersions = Math.Max(0, retainNonActiveVersions)
            };

            if (string.IsNullOrWhiteSpace(contentRoot) ||
                !Directory.Exists(contentRoot))
            {
                report.ErrorMessages.Add(
                    "contentRoot does not exist: " + contentRoot);
                return report;
            }

            if (string.IsNullOrWhiteSpace(activeVersion))
            {
                report.ErrorMessages.Add(
                    "activeVersion is null/empty; refusing to clean.");
                return report;
            }

            if (!Directory.Exists(Path.Combine(contentRoot, activeVersion)))
            {
                report.ErrorMessages.Add(
                    "activeVersion directory missing: " +
                    activeVersion +
                    "; refusing to clean.");
                return report;
            }

            var stagingInUse = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(stagingRoot) &&
                Directory.Exists(stagingRoot))
            {
                foreach (var dir in Directory.GetDirectories(
                    stagingRoot,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(dir);

                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    // Only a staging folder that still holds files (a .part or
                    // its .meta) means an unfinished download. An empty folder
                    // is a leftover and must not pin a version forever.
                    if (!HasAnyFile(dir))
                    {
                        try
                        {
                            Directory.Delete(dir, false);
                        }
                        catch
                        {
                            // Best effort.
                        }

                        continue;
                    }

                    stagingInUse.Add(name);
                }
            }

            var allVersions = new List<string>();

            foreach (var dir in Directory.GetDirectories(
                contentRoot,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);

                if (!string.IsNullOrEmpty(name))
                {
                    allVersions.Add(name);
                }
            }

            // 非 active 版本按版本号降序，取前 retainNonActiveVersions 个保留。
            // 必须按数字比较：ordinal 会把 v10 排在 v9 前面，导致新的被删、旧的被留。
            allVersions.Sort((a, b) => CompareVersionNames(b, a));

            var retained = new HashSet<string>(StringComparer.Ordinal);
            retained.Add(activeVersion);

            var retainedNonActive = 0;

            for (var i = 0; i < allVersions.Count; i++)
            {
                var v = allVersions[i];

                if (string.Equals(v, activeVersion, StringComparison.Ordinal))
                {
                    continue;
                }

                if (retainedNonActive < retainNonActiveVersions)
                {
                    retained.Add(v);
                    retainedNonActive++;
                    continue;
                }

                if (stagingInUse.Contains(v))
                {
                    retained.Add(v);
                    report.SkippedBecauseStaging++;
                }
            }

            for (var i = 0; i < allVersions.Count; i++)
            {
                var v = allVersions[i];

                if (retained.Contains(v))
                {
                    report.Retained++;
                    report.RetainedVersions.Add(v);
                    continue;
                }

                var path = Path.Combine(contentRoot, v);

                try
                {
                    Directory.Delete(path, true);
                    report.Deleted++;
                    report.DeletedVersions.Add(v);

                    Debug.Log("[Cleaner] deleted version " + v);
                }
                catch (Exception exception)
                {
                    report.ErrorMessages.Add(
                        "Failed to delete " + v + ": " + exception.Message);
                }
            }

            return report;
        }

        private static bool HasAnyFile(string directory)
        {
            try
            {
                using (var entries = Directory.EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly).GetEnumerator())
                {
                    return entries.MoveNext();
                }
            }
            catch
            {
                // Unreadable staging folder: assume it is in use.
                return true;
            }
        }

        /// <summary>
        /// Compares two version folder names, treating the trailing digits as a
        /// number so v10 sorts above v9. Names without a numeric tail fall back
        /// to an ordinal comparison.
        /// </summary>
        private static int CompareVersionNames(string a, string b)
        {
            var aNumber = TrailingNumber(a, out var aPrefix);
            var bNumber = TrailingNumber(b, out var bPrefix);

            if (aNumber.HasValue && bNumber.HasValue)
            {
                var byPrefix = string.Compare(
                    aPrefix,
                    bPrefix,
                    StringComparison.OrdinalIgnoreCase);

                if (byPrefix != 0)
                {
                    return byPrefix;
                }

                if (aNumber.Value != bNumber.Value)
                {
                    return aNumber.Value.CompareTo(bNumber.Value);
                }
            }

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static long? TrailingNumber(string value, out string prefix)
        {
            prefix = value ?? string.Empty;

            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var index = value.Length;

            while (index > 0 && char.IsDigit(value[index - 1]))
            {
                index--;
            }

            if (index == value.Length)
            {
                return null;
            }

            prefix = value.Substring(0, index);

            return long.TryParse(value.Substring(index), out var number)
                ? number
                : (long?)null;
        }
    }
}
