using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Building
{
    public sealed class BundleValidator
    {
        public ValidationResult Validate(
            string outputDirectory,
            PlatformManifest manifest,
            IReadOnlyList<AssetBinding> expectedBindings,
            BuildTarget requestedTarget)
        {
            var result = new ValidationResult();

            if (manifest == null)
            {
                AddError(
                    result,
                    BuildErrorCode.ManifestNull,
                    null,
                    null,
                    "Manifest is null.");
                return result;
            }

            if (!PlatformVariantPolicy.ValidateManifestPlatform(
                manifest,
                requestedTarget,
                out var platformError))
            {
                AddError(
                    result,
                    BuildErrorCode.PlatformMismatch,
                    null,
                    null,
                    platformError);
            }

            if (string.IsNullOrEmpty(outputDirectory))
            {
                AddError(
                    result,
                    BuildErrorCode.IoError,
                    null,
                    null,
                    "Output directory is null or empty.");
                return result;
            }

            if (expectedBindings == null ||
                expectedBindings.Count == 0)
            {
                AddError(
                    result,
                    BuildErrorCode.InvalidBinding,
                    null,
                    null,
                    "Expected bindings are null or empty.");
                return result;
            }

            var loadedBundles =
                new Dictionary<string, AssetBundle>(StringComparer.Ordinal);

            try
            {
                for (var i = 0; i < manifest.bundles.Count; i++)
                {
                    var bundleRecord = manifest.bundles[i];

                    if (bundleRecord == null)
                    {
                        AddError(
                            result,
                            BuildErrorCode.InvalidBinding,
                            null,
                            null,
                            "Bundle record at index " + i + " is null.");
                        continue;
                    }

                    var bundlePath = Path.Combine(
                        outputDirectory,
                        bundleRecord.bundleFileName);

                    if (!File.Exists(bundlePath))
                    {
                        AddError(
                            result,
                            BuildErrorCode.BundleFileMissing,
                            null,
                            null,
                            "Bundle file is missing: " + bundlePath);
                        continue;
                    }

                    try
                    {
                        var sizeBytes = new FileInfo(bundlePath).Length;

                        if (sizeBytes != bundleRecord.sizeBytes)
                        {
                            AddError(
                                result,
                                BuildErrorCode.SizeMismatch,
                                null,
                                null,
                                "Bundle size mismatch. file=" +
                                bundleRecord.bundleFileName +
                                ", expected=" + bundleRecord.sizeBytes +
                                ", actual=" + sizeBytes);
                        }
                    }
                    catch (Exception exception)
                    {
                        AddError(
                            result,
                            BuildErrorCode.IoError,
                            null,
                            null,
                            "Failed to read bundle size. " +
                            exception.GetType().Name + ": " +
                            exception.Message);
                    }

                    if (HashUtility.TryComputeSha256Hex(
                        bundlePath,
                        out var actualHash,
                        out var hashError))
                    {
                        if (!string.Equals(
                            actualHash,
                            bundleRecord.sha256Hex,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            AddError(
                                result,
                                BuildErrorCode.HashMismatch,
                                null,
                                null,
                                "Bundle hash mismatch. file=" +
                                bundleRecord.bundleFileName +
                                ", expected=" + bundleRecord.sha256Hex +
                                ", actual=" + actualHash);
                        }
                    }
                    else
                    {
                        AddError(
                            result,
                            BuildErrorCode.HashComputeFailed,
                            null,
                            null,
                            "Failed to compute bundle hash. " +
                            hashError);
                    }

                    try
                    {
                        var bundle = AssetBundle.LoadFromFile(bundlePath);

                        if (bundle == null)
                        {
                            AddError(
                                result,
                                BuildErrorCode.BundleLoadFailed,
                                null,
                                null,
                                "Failed to load bundle: " + bundlePath);
                        }
                        else
                        {
                            loadedBundles[bundleRecord.bundleGroupName] =
                                bundle;
                        }
                    }
                    catch (Exception exception)
                    {
                        AddError(
                            result,
                            BuildErrorCode.BundleLoadFailed,
                            null,
                            null,
                            "Exception loading bundle. " +
                            exception.GetType().Name + ": " +
                            exception.Message);
                    }
                }

                result.BundlesLoaded = loadedBundles.Count;

                var assetsByKey =
                    new Dictionary<string, AssetRecord>(
                        StringComparer.Ordinal);

                for (var i = 0; i < manifest.assets.Count; i++)
                {
                    var assetRecord = manifest.assets[i];

                    if (assetRecord == null ||
                        string.IsNullOrEmpty(assetRecord.assetKey))
                    {
                        continue;
                    }

                    assetsByKey[assetRecord.assetKey] = assetRecord;
                }

                for (var i = 0; i < expectedBindings.Count; i++)
                {
                    var binding = expectedBindings[i];

                    if (binding == null)
                    {
                        continue;
                    }

                    if (!assetsByKey.TryGetValue(
                        binding.assetKey,
                        out var assetRecord))
                    {
                        AddError(
                            result,
                            BuildErrorCode.ExpectedAssetMissing,
                            binding.assetKey,
                            binding.sourcePath,
                            "Expected AssetKey is missing from manifest.");
                        continue;
                    }

                    if (!loadedBundles.TryGetValue(
                        assetRecord.bundleGroupName,
                        out var bundle))
                    {
                        continue;
                    }

                    result.AssetsChecked++;

                    try
                    {
                        var loadedObject =
                            bundle.LoadAsset<UnityEngine.Object>(
                                assetRecord.internalAssetPath);

                        if (loadedObject == null)
                        {
                            AddError(
                                result,
                                BuildErrorCode.AssetLoadFailed,
                                assetRecord.assetKey,
                                assetRecord.sourcePath,
                                "LoadAsset returned null. internalAssetPath=" +
                                assetRecord.internalAssetPath +
                                ", group=" +
                                assetRecord.bundleGroupName);
                        }
                    }
                    catch (Exception exception)
                    {
                        AddError(
                            result,
                            BuildErrorCode.AssetLoadFailed,
                            assetRecord.assetKey,
                            assetRecord.sourcePath,
                            "LoadAsset threw. " +
                            exception.GetType().Name + ": " +
                            exception.Message);
                    }
                }
            }
            finally
            {
                foreach (var pair in loadedBundles)
                {
                    if (pair.Value != null)
                    {
                        // Validation only needs the bundle for a moment; fully
                        // unload so the editor session does not keep identical
                        // assets alive and block later loads of the same content.
                        pair.Value.Unload(true);
                    }
                }

                loadedBundles.Clear();
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        private static void AddError(
            ValidationResult result,
            BuildErrorCode code,
            string assetKey,
            string sourcePath,
            string message)
        {
            result.Errors.Add(new BuildError
            {
                Code = code,
                AssetKey = assetKey,
                SourcePath = sourcePath,
                Message = message
            });
        }
    }
}
