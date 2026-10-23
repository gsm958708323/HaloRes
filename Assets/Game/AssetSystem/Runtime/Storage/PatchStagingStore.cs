using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.AssetSystem.Storage
{
    [Serializable]
    internal sealed class StagingPartMeta
    {
        public int SchemaVersion;
        public string ContentVersion;
        public string BundleFileName;
        public string Url;
        public long SizeBytes;
        public string Sha256Hex;
    }

    /// <summary>
    /// The one staging area implementation.
    ///
    /// Staging is a file-layout concern, not a patch-policy concern, which is
    /// why it lives in Storage. The project used to have two of these with
    /// different roots, different meta field names, and different path
    /// validators; a `.part` written by one could not be resumed by the other.
    ///
    /// <see cref="Root"/> is the staging root itself (the caller decides
    /// whether it sits next to content or under a temp directory).
    /// </summary>
    public sealed class PatchStagingStore
    {
        public const int MetaSchemaVersion = 2;

        public string Root { get; }

        public PatchStagingStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException(
                    "root must not be empty.",
                    nameof(root));
            }

            Root = Path.GetFullPath(root);
        }

        public void CleanupStaleParts(Patch.PatchManifest manifest)
        {
            var versionDirectory = GetVersionDirectory(manifest.ContentVersion);

            if (!Directory.Exists(versionDirectory))
            {
                return;
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                allowed.Add(manifest.Bundles[i].FileName);
            }

            foreach (var partFile in Directory.EnumerateFiles(
                versionDirectory,
                "*.part",
                SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(partFile);
                var bundleFileName = fileName.Substring(
                    0,
                    fileName.Length - ".part".Length);

                if (!allowed.Contains(bundleFileName))
                {
                    TryDeleteFile(partFile);
                    TryDeleteFile(GetMetaPath(
                        manifest.ContentVersion,
                        bundleFileName));
                    continue;
                }

                var bundle = FindBundle(manifest, bundleFileName);

                if (bundle == null ||
                    !TryReadMeta(
                        GetMetaPath(manifest.ContentVersion, bundleFileName),
                        out var meta) ||
                    !MetaMatches(meta, manifest, bundle))
                {
                    TryDeleteFile(partFile);
                    TryDeleteFile(GetMetaPath(
                        manifest.ContentVersion,
                        bundleFileName));
                }
            }
        }

        public bool PreparePartForResume(
            Patch.PatchManifest manifest,
            Patch.PatchBundleDescriptor bundle,
            out long offsetBytes,
            out string error)
        {
            offsetBytes = 0;
            error = null;

            try
            {
                EnsureVersionDirectory(manifest.ContentVersion);

                var partPath = GetPartPath(
                    manifest.ContentVersion,
                    bundle.FileName);

                var metaPath = GetMetaPath(
                    manifest.ContentVersion,
                    bundle.FileName);

                if (File.Exists(partPath))
                {
                    if (!TryReadMeta(metaPath, out var existingMeta) ||
                        !MetaMatches(existingMeta, manifest, bundle))
                    {
                        DeletePartAndMeta(
                            manifest.ContentVersion,
                            bundle.FileName);
                    }
                }

                if (!File.Exists(partPath) || !File.Exists(metaPath))
                {
                    WriteMeta(manifest, bundle);
                }

                if (!File.Exists(partPath))
                {
                    offsetBytes = 0;
                    return true;
                }

                var length = new FileInfo(partPath).Length;

                if (length > bundle.SizeBytes)
                {
                    DeletePartAndMeta(
                        manifest.ContentVersion,
                        bundle.FileName);

                    WriteMeta(manifest, bundle);
                    offsetBytes = 0;
                    return true;
                }

                offsetBytes = length;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public long GetPartLength(
            string contentVersion,
            string bundleFileName)
        {
            try
            {
                var path = GetPartPath(contentVersion, bundleFileName);

                if (!File.Exists(path))
                {
                    return 0;
                }

                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        public string GetPartPath(
            string contentVersion,
            string bundleFileName)
        {
            return Path.Combine(
                GetVersionDirectory(contentVersion),
                ValidateFileName(bundleFileName) + ".part");
        }

        public string GetMetaPath(
            string contentVersion,
            string bundleFileName)
        {
            return Path.Combine(
                GetVersionDirectory(contentVersion),
                ValidateFileName(bundleFileName) + ".part.meta");
        }

        public void DeleteMeta(
            string contentVersion,
            string bundleFileName)
        {
            TryDeleteFile(GetMetaPath(contentVersion, bundleFileName));
        }

        public void DeletePartAndMeta(
            string contentVersion,
            string bundleFileName)
        {
            TryDeleteFile(GetPartPath(contentVersion, bundleFileName));
            TryDeleteFile(GetMetaPath(contentVersion, bundleFileName));
        }

        /// <summary>
        /// Manifest values are data, not paths: a version or file name that
        /// contains separators must never reach Path.Combine.
        /// </summary>
        private string GetVersionDirectory(string contentVersion)
        {
            if (!PathSafety.IsSimpleFileName(contentVersion))
            {
                throw new ArgumentException(
                    "Invalid contentVersion: " + (contentVersion ?? "<null>"),
                    nameof(contentVersion));
            }

            return Path.Combine(Root, contentVersion);
        }

        private static string ValidateFileName(string fileName)
        {
            if (!PathSafety.IsSimpleFileName(fileName))
            {
                throw new ArgumentException(
                    "Invalid bundle file name: " + (fileName ?? "<null>"),
                    nameof(fileName));
            }

            return fileName;
        }

        private void EnsureVersionDirectory(string contentVersion)
        {
            Directory.CreateDirectory(GetVersionDirectory(contentVersion));
        }

        private void WriteMeta(
            Patch.PatchManifest manifest,
            Patch.PatchBundleDescriptor bundle)
        {
            var meta = new StagingPartMeta
            {
                SchemaVersion = MetaSchemaVersion,
                ContentVersion = manifest.ContentVersion,
                BundleFileName = bundle.FileName,
                Url = bundle.Url,
                SizeBytes = bundle.SizeBytes,
                Sha256Hex = bundle.Sha256Hex
            };

            var json = JsonUtility.ToJson(meta, true);
            var path = GetMetaPath(
                manifest.ContentVersion,
                bundle.FileName);

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static bool TryReadMeta(
            string path,
            out StagingPartMeta meta)
        {
            meta = null;

            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                meta = JsonUtility.FromJson<StagingPartMeta>(json);

                return meta != null &&
                       meta.SchemaVersion == MetaSchemaVersion;
            }
            catch
            {
                return false;
            }
        }

        private static bool MetaMatches(
            StagingPartMeta meta,
            Patch.PatchManifest manifest,
            Patch.PatchBundleDescriptor bundle)
        {
            if (meta == null)
            {
                return false;
            }

            return string.Equals(
                       meta.ContentVersion,
                       manifest.ContentVersion,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       meta.BundleFileName,
                       bundle.FileName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       meta.Url,
                       bundle.Url,
                       StringComparison.Ordinal) &&
                   meta.SizeBytes == bundle.SizeBytes &&
                   string.Equals(
                       meta.Sha256Hex,
                       bundle.Sha256Hex,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static Patch.PatchBundleDescriptor FindBundle(
            Patch.PatchManifest manifest,
            string bundleFileName)
        {
            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                if (string.Equals(
                    manifest.Bundles[i].FileName,
                    bundleFileName,
                    StringComparison.Ordinal))
                {
                    return manifest.Bundles[i];
                }
            }

            return null;
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
