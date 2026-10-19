using System;
using System.Collections.Generic;
using System.IO;
using Game.AssetSystem.Integration;
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

                    if (!string.IsNullOrEmpty(name))
                    {
                        stagingInUse.Add(name);
                    }
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

            // 非 active 版本按名字降序（语义版本号），取前 retainNonActiveVersions 个保留
            allVersions.Sort((a, b) => string.Compare(
                b,
                a,
                StringComparison.OrdinalIgnoreCase));

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
    }
}
