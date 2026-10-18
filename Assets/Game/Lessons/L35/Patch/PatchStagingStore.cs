using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.AssetSystem.Patch.L35
{
    [Serializable]
    internal sealed class StagingPartMeta
    {
        public int schemaVersion;
        public string contentVersion;
        public string bundleFileName;
        public string url;
        public long sizeBytes;
        public string sha256Hex;
    }

    public sealed class PatchStagingStore
    {
        public const int MetaSchemaVersion = 1;

        public string StagingDirectory { get; }

        public PatchStagingStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException(
                    "rootDirectory must not be empty.",
                    nameof(rootDirectory));
            }

            StagingDirectory = Path.Combine(rootDirectory, "staging");
        }

        public void CleanupStaleParts(PatchManifest manifest)
        {
            var versionDirectory = GetVersionDirectory(manifest.contentVersion);

            if (!Directory.Exists(versionDirectory))
            {
                return;
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.bundles.Length; i++)
            {
                allowed.Add(manifest.bundles[i].bundleFileName);
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
                        manifest.contentVersion,
                        bundleFileName));
                    continue;
                }

                var bundle = FindBundle(manifest, bundleFileName);

                if (bundle == null ||
                    !TryReadMeta(
                        GetMetaPath(manifest.contentVersion, bundleFileName),
                        out var meta) ||
                    !MetaMatches(meta, manifest, bundle))
                {
                    TryDeleteFile(partFile);
                    TryDeleteFile(GetMetaPath(
                        manifest.contentVersion,
                        bundleFileName));
                }
            }
        }

        public bool PreparePartForResume(
            PatchManifest manifest,
            PatchBundleDescriptor bundle,
            out long offsetBytes,
            out string error)
        {
            offsetBytes = 0;
            error = null;

            try
            {
                EnsureVersionDirectory(manifest.contentVersion);

                var partPath = GetPartPath(
                    manifest.contentVersion,
                    bundle.bundleFileName);

                var metaPath = GetMetaPath(
                    manifest.contentVersion,
                    bundle.bundleFileName);

                if (File.Exists(partPath))
                {
                    if (!TryReadMeta(metaPath, out var existingMeta) ||
                        !MetaMatches(existingMeta, manifest, bundle))
                    {
                        DeletePartAndMeta(
                            manifest.contentVersion,
                            bundle.bundleFileName);
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

                if (length > bundle.sizeBytes)
                {
                    DeletePartAndMeta(
                        manifest.contentVersion,
                        bundle.bundleFileName);

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
                bundleFileName + ".part");
        }

        public string GetMetaPath(
            string contentVersion,
            string bundleFileName)
        {
            return Path.Combine(
                GetVersionDirectory(contentVersion),
                bundleFileName + ".part.meta");
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

        private string GetVersionDirectory(string contentVersion)
        {
            if (!Game.AssetSystem.Storage.L35.AssetFileStore.IsSimpleFileName(contentVersion))
            {
                throw new ArgumentException(
                    "Invalid contentVersion: " + contentVersion,
                    nameof(contentVersion));
            }

            return Path.Combine(StagingDirectory, contentVersion);
        }

        private void EnsureVersionDirectory(string contentVersion)
        {
            Directory.CreateDirectory(GetVersionDirectory(contentVersion));
        }

        private void WriteMeta(
            PatchManifest manifest,
            PatchBundleDescriptor bundle)
        {
            var meta = new StagingPartMeta
            {
                schemaVersion = MetaSchemaVersion,
                contentVersion = manifest.contentVersion,
                bundleFileName = bundle.bundleFileName,
                url = bundle.url,
                sizeBytes = bundle.sizeBytes,
                sha256Hex = bundle.sha256Hex
            };

            var json = JsonUtility.ToJson(meta, true);
            var path = GetMetaPath(
                manifest.contentVersion,
                bundle.bundleFileName);

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
                       meta.schemaVersion == MetaSchemaVersion;
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
                       meta.contentVersion,
                       manifest.contentVersion,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       meta.bundleFileName,
                       bundle.bundleFileName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       meta.url,
                       bundle.url,
                       StringComparison.Ordinal) &&
                   meta.sizeBytes == bundle.sizeBytes &&
                   string.Equals(
                       meta.sha256Hex,
                       bundle.sha256Hex,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static PatchBundleDescriptor FindBundle(
            PatchManifest manifest,
            string bundleFileName)
        {
            for (var i = 0; i < manifest.bundles.Length; i++)
            {
                if (string.Equals(
                    manifest.bundles[i].bundleFileName,
                    bundleFileName,
                    StringComparison.Ordinal))
                {
                    return manifest.bundles[i];
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
