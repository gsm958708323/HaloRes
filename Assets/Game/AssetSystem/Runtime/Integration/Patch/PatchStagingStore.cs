using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.AssetSystem.Integration
{
    [Serializable]
    internal sealed class StagingPartMeta
    {
        public int SchemaVersion;
        public string ContentVersion;
        public string FileName;
        public long SizeBytes;
        public string Sha256Hex;
    }

    public sealed class PatchStagingStore
    {
        private const int MetaSchemaVersion = 1;

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
            Directory.CreateDirectory(Root);
        }

        public string GetPartPath(string contentVersion, string fileName)
        {
            return Path.Combine(
                GetVersionDirectory(contentVersion),
                ValidateFileName(fileName) + ".part");
        }

        public string GetMetaPath(string contentVersion, string fileName)
        {
            return Path.Combine(
                GetVersionDirectory(contentVersion),
                ValidateFileName(fileName) + ".part.meta");
        }

        /// <summary>
        /// Manifest values are data, not paths: a version or file name that
        /// contains separators must never reach Path.Combine.
        /// </summary>
        private string GetVersionDirectory(string contentVersion)
        {
            if (!Game.AssetSystem.Storage.PathSafety.IsSimpleFileName(
                contentVersion))
            {
                throw new ArgumentException(
                    "Invalid contentVersion: " + (contentVersion ?? "<null>"),
                    nameof(contentVersion));
            }

            return Path.Combine(Root, contentVersion);
        }

        private static string ValidateFileName(string fileName)
        {
            if (!Game.AssetSystem.Storage.PathSafety.IsSimpleFileName(fileName))
            {
                throw new ArgumentException(
                    "Invalid bundle file name: " + (fileName ?? "<null>"),
                    nameof(fileName));
            }

            return fileName;
        }

        public bool PrepareForResume(
            PatchManifest manifest,
            PatchBundleDescriptor bundle,
            out long offset,
            out string error)
        {
            offset = 0;
            error = null;

            try
            {
                var versionDir = GetVersionDirectory(manifest.ContentVersion);
                Directory.CreateDirectory(versionDir);

                var partPath = GetPartPath(
                    manifest.ContentVersion,
                    bundle.FileName);

                var metaPath = GetMetaPath(
                    manifest.ContentVersion,
                    bundle.FileName);

                if (File.Exists(partPath))
                {
                    if (!TryReadMeta(metaPath, out var meta) ||
                        !MetaMatches(meta, manifest, bundle))
                    {
                        TryDelete(partPath);
                        TryDelete(metaPath);
                    }
                }

                if (!File.Exists(partPath) || !File.Exists(metaPath))
                {
                    WriteMeta(manifest, bundle);
                }

                if (File.Exists(partPath))
                {
                    var length = new FileInfo(partPath).Length;

                    if (length > bundle.SizeBytes)
                    {
                        TryDelete(partPath);
                        WriteMeta(manifest, bundle);
                        offset = 0;
                        return true;
                    }

                    offset = length;
                    return true;
                }

                offset = 0;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public void DeletePart(string contentVersion, string fileName)
        {
            TryDelete(GetPartPath(contentVersion, fileName));
            TryDelete(GetMetaPath(contentVersion, fileName));
        }

        private void WriteMeta(
            PatchManifest manifest,
            PatchBundleDescriptor bundle)
        {
            var meta = new StagingPartMeta
            {
                SchemaVersion = MetaSchemaVersion,
                ContentVersion = manifest.ContentVersion,
                FileName = bundle.FileName,
                SizeBytes = bundle.SizeBytes,
                Sha256Hex = bundle.Sha256Hex
            };

            var json = JsonUtility.ToJson(meta, true);
            var path = GetMetaPath(
                manifest.ContentVersion,
                bundle.FileName);

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllText(
                path,
                json,
                new UTF8Encoding(false));
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
            PatchManifest manifest,
            PatchBundleDescriptor bundle)
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
                       meta.FileName,
                       bundle.FileName,
                       StringComparison.Ordinal) &&
                   meta.SizeBytes == bundle.SizeBytes &&
                   string.Equals(
                       meta.Sha256Hex,
                       bundle.Sha256Hex,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDelete(string path)
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
