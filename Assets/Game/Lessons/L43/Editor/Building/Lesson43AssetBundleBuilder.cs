using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Building.L43
{
    public sealed class Lesson43AssetBundleBuilder
    {
        public const int ManifestSchemaVersion = 1;

        public Lesson43BuildResult Build(
            IReadOnlyList<Lesson43AssetBinding> bindings,
            string contentVersion,
            BuildTarget target)
        {
            var result = new Lesson43BuildResult
            {
                ContentVersion = contentVersion == null
                    ? string.Empty
                    : contentVersion.Trim(),
                TargetPlatformKey =
                    Lesson43PlatformVariantPolicy.GetPlatformKey(target)
            };

            if (!Lesson43PlatformVariantPolicy.IsSimpleName(
                result.ContentVersion))
            {
                AddError(
                    result,
                    Lesson43BuildErrorCode.InvalidContentVersion,
                    null,
                    null,
                    "Invalid content version: " +
                    (contentVersion ?? "<null>"));
                return result;
            }

            if (bindings == null || bindings.Count == 0)
            {
                AddError(
                    result,
                    Lesson43BuildErrorCode.InvalidBinding,
                    null,
                    null,
                    "Bindings are null or empty.");
                return result;
            }

            var groupToAssets =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var seenSourcePaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAssetKeys =
                new HashSet<string>(StringComparer.Ordinal);
            var sortedBindings =
                new List<Lesson43AssetBinding>();

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];

                if (binding == null)
                {
                    AddError(
                        result,
                        Lesson43BuildErrorCode.InvalidBinding,
                        null,
                        null,
                        "Binding at index " + i + " is null.");
                    continue;
                }

                var sourcePath = NormalizePath(binding.sourcePath);

                if (string.IsNullOrEmpty(sourcePath) ||
                    !sourcePath.StartsWith(
                        "Assets/",
                        StringComparison.Ordinal))
                {
                    AddError(
                        result,
                        Lesson43BuildErrorCode.InvalidBinding,
                        binding.assetKey,
                        sourcePath,
                        "Binding source path must start with Assets/.");
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    sourcePath) == null)
                {
                    AddError(
                        result,
                        Lesson43BuildErrorCode.InvalidBinding,
                        binding.assetKey,
                        sourcePath,
                        "Asset does not exist at source path.");
                    continue;
                }

                if (string.IsNullOrEmpty(binding.assetKey))
                {
                    AddError(
                        result,
                        Lesson43BuildErrorCode.InvalidBinding,
                        binding.assetKey,
                        sourcePath,
                        "AssetKey is empty.");
                    continue;
                }

                if (!seenAssetKeys.Add(binding.assetKey))
                {
                    AddError(
                        result,
                        Lesson43BuildErrorCode.DuplicateAssetKey,
                        binding.assetKey,
                        sourcePath,
                        "Duplicate AssetKey in build plan.");
                    continue;
                }

                if (!seenSourcePaths.Add(sourcePath))
                {
                    AddError(
                        result,
                        Lesson43BuildErrorCode.DuplicateSourcePath,
                        binding.assetKey,
                        sourcePath,
                        "Duplicate source path in build plan.");
                    continue;
                }

                if (!Lesson43PlatformVariantPolicy.IsSimpleName(
                    binding.bundleGroupName))
                {
                    AddError(
                        result,
                        Lesson43BuildErrorCode.InvalidBinding,
                        binding.assetKey,
                        sourcePath,
                        "Invalid bundle group name: " +
                        (binding.bundleGroupName ?? "<null>"));
                    continue;
                }

                if (!groupToAssets.TryGetValue(
                    binding.bundleGroupName,
                    out var assetList))
                {
                    assetList = new List<string>();
                    groupToAssets.Add(
                        binding.bundleGroupName,
                        assetList);
                }

                assetList.Add(sourcePath);
                sortedBindings.Add(binding);
            }

            if (result.Errors.Count > 0)
            {
                return result;
            }

            var projectRoot =
                Lesson43PlatformVariantPolicy.GetProjectRoot();

            var outputDirectory =
                Lesson43PlatformVariantPolicy.GetPlatformOutputDirectory(
                    projectRoot,
                    result.ContentVersion,
                    target);

            result.OutputDirectory = outputDirectory;

            try
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }

                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception exception)
            {
                AddError(
                    result,
                    Lesson43BuildErrorCode.IoError,
                    null,
                    null,
                    "Failed to prepare output directory. " +
                    exception.GetType().Name + ": " +
                    exception.Message);
                return result;
            }

            var groupNames =
                new List<string>(groupToAssets.Keys);

            groupNames.Sort(StringComparer.Ordinal);

            var builds = new AssetBundleBuild[groupNames.Count];

            for (var i = 0; i < groupNames.Count; i++)
            {
                var groupName = groupNames[i];

                builds[i] = new AssetBundleBuild
                {
                    assetBundleName = groupName,
                    assetBundleVariant = string.Empty,
                    assetNames = groupToAssets[groupName].ToArray()
                };
            }

            AssetBundleManifest builtManifest = null;

            try
            {
                builtManifest = BuildPipeline.BuildAssetBundles(
                    outputDirectory,
                    builds,
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    target);
            }
            catch (Exception exception)
            {
                AddError(
                    result,
                    Lesson43BuildErrorCode.BuildPipelineFailed,
                    null,
                    null,
                    exception.GetType().Name + ": " +
                    exception.Message);
                return result;
            }

            if (builtManifest == null)
            {
                AddError(
                    result,
                    Lesson43BuildErrorCode.BuildManifestNull,
                    null,
                    null,
                    "BuildPipeline returned a null AssetBundleManifest.");
                return result;
            }

            var builtBundles = new HashSet<string>(
                builtManifest.GetAllAssetBundles(),
                StringComparer.Ordinal);

            var bundleRecords =
                new List<Lesson43BundleRecord>();

            var loadedBundles =
                new Dictionary<string, AssetBundle>(StringComparer.Ordinal);

            var assetRecords =
                new List<Lesson43AssetRecord>();

            try
            {
                for (var i = 0; i < groupNames.Count; i++)
                {
                    var groupName = groupNames[i];

                    if (!builtBundles.Contains(groupName))
                    {
                        AddError(
                            result,
                            Lesson43BuildErrorCode.ExpectedBundleNotBuilt,
                            null,
                            null,
                            "Expected bundle was not built: " +
                            groupName);
                        continue;
                    }

                    var bundlePath = Path.Combine(
                        outputDirectory,
                        groupName);

                    if (!File.Exists(bundlePath))
                    {
                        AddError(
                            result,
                            Lesson43BuildErrorCode.BundleFileMissing,
                            null,
                            null,
                            "Built bundle file is missing: " +
                            bundlePath);
                        continue;
                    }

                    if (!Lesson43HashUtility.TryComputeSha256Hex(
                        bundlePath,
                        out var hashHex,
                        out var hashError))
                    {
                        AddError(
                            result,
                            Lesson43BuildErrorCode.HashComputeFailed,
                            null,
                            null,
                            "Failed to compute bundle hash. " +
                            hashError);
                        continue;
                    }

                    long sizeBytes;

                    try
                    {
                        sizeBytes = new FileInfo(bundlePath).Length;
                    }
                    catch (Exception exception)
                    {
                        AddError(
                            result,
                            Lesson43BuildErrorCode.IoError,
                            null,
                            null,
                            "Failed to read bundle size. " +
                            exception.GetType().Name + ": " +
                            exception.Message);
                        continue;
                    }

                    bundleRecords.Add(new Lesson43BundleRecord
                    {
                        bundleGroupName = groupName,
                        bundleFileName = groupName,
                        sizeBytes = sizeBytes,
                        sha256Hex = hashHex
                    });

                    try
                    {
                        var bundle = AssetBundle.LoadFromFile(bundlePath);

                        if (bundle == null)
                        {
                            AddError(
                                result,
                                Lesson43BuildErrorCode.BundleLoadFailed,
                                null,
                                null,
                                "Failed to load built bundle: " +
                                bundlePath);
                        }
                        else
                        {
                            loadedBundles.Add(groupName, bundle);
                        }
                    }
                    catch (Exception exception)
                    {
                        AddError(
                            result,
                            Lesson43BuildErrorCode.BundleLoadFailed,
                            null,
                            null,
                            "Exception loading built bundle. " +
                            exception.GetType().Name + ": " +
                            exception.Message);
                    }
                }

                sortedBindings.Sort((a, b) => string.Compare(
                    a.assetKey,
                    b.assetKey,
                    StringComparison.Ordinal));

                for (var i = 0; i < sortedBindings.Count; i++)
                {
                    var binding = sortedBindings[i];
                    var sourcePath = NormalizePath(binding.sourcePath);

                    if (!loadedBundles.TryGetValue(
                        binding.bundleGroupName,
                        out var bundle))
                    {
                        continue;
                    }

                    var assetNames = bundle.GetAllAssetNames();
                    string actualInternalPath = null;

                    for (var j = 0; j < assetNames.Length; j++)
                    {
                        if (string.Equals(
                            assetNames[j],
                            sourcePath,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            actualInternalPath = assetNames[j];
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(actualInternalPath))
                    {
                        AddError(
                            result,
                            Lesson43BuildErrorCode.InternalAssetNameMissing,
                            binding.assetKey,
                            sourcePath,
                            "Source path was not found inside bundle. group=" +
                            binding.bundleGroupName);
                        continue;
                    }

                    assetRecords.Add(new Lesson43AssetRecord
                    {
                        assetKey = binding.assetKey,
                        sourcePath = sourcePath,
                        internalAssetPath = actualInternalPath,
                        bundleGroupName = binding.bundleGroupName
                    });
                }
            }
            finally
            {
                foreach (var pair in loadedBundles)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.Unload(false);
                    }
                }

                loadedBundles.Clear();
            }

            bundleRecords.Sort((a, b) => string.Compare(
                a.bundleGroupName,
                b.bundleGroupName,
                StringComparison.Ordinal));

            assetRecords.Sort((a, b) => string.Compare(
                a.assetKey,
                b.assetKey,
                StringComparison.Ordinal));

            result.Bundles = bundleRecords;

            if (result.Errors.Count > 0)
            {
                result.Success = false;
                result.Manifest = null;
                TryWriteBuildResultReport(result);
                return result;
            }

            var manifest = new Lesson43PlatformManifest
            {
                schemaVersion = ManifestSchemaVersion,
                contentVersion = result.ContentVersion,
                targetPlatformKey = result.TargetPlatformKey,
                buildTarget = target.ToString(),
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                bundles = bundleRecords,
                assets = assetRecords
            };

            result.Manifest = manifest;
            result.Success = true;

            try
            {
                var manifestPath = Path.Combine(
                    outputDirectory,
                    "lesson43-manifest.json");

                WriteJson(manifestPath, manifest);
            }
            catch (Exception exception)
            {
                result.Success = false;
                result.Manifest = null;

                AddError(
                    result,
                    Lesson43BuildErrorCode.IoError,
                    null,
                    null,
                    "Failed to write manifest. " +
                    exception.GetType().Name + ": " +
                    exception.Message);
            }

            TryWriteBuildResultReport(result);
            return result;
        }

        private static void TryWriteBuildResultReport(
            Lesson43BuildResult result)
        {
            try
            {
                var projectRoot =
                    Lesson43PlatformVariantPolicy.GetProjectRoot();

                var directory = Path.Combine(
                    Lesson43PlatformVariantPolicy.GetBuildRoot(projectRoot),
                    "Reports");

                Directory.CreateDirectory(directory);

                var path = Path.Combine(
                    directory,
                    "lesson43-last-build-result.json");

                WriteJson(path, result);
            }
            catch
            {
                // Best effort. The console result remains authoritative.
            }
        }

        private static void WriteJson<T>(string path, T value)
        {
            var json = JsonUtility.ToJson(value, true);

            File.WriteAllText(
                path,
                json,
                new UTF8Encoding(false));
        }

        private static void AddError(
            Lesson43BuildResult result,
            Lesson43BuildErrorCode code,
            string assetKey,
            string sourcePath,
            string message)
        {
            result.Errors.Add(new Lesson43BuildError
            {
                Code = code,
                AssetKey = assetKey,
                SourcePath = sourcePath,
                Message = message
            });
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/');
        }
    }
}
