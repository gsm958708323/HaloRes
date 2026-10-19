using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.AssetSystem.Editor.Building
{
    public sealed class AssetBundleBuilder
    {
        public const int ManifestSchemaVersion = 1;

        public BuildResult Build(
            IReadOnlyList<AssetBinding> bindings,
            string contentVersion,
            BuildTarget target,
            string outputRootOverride = null)
        {
            var result = new BuildResult
            {
                ContentVersion = contentVersion == null
                    ? string.Empty
                    : contentVersion.Trim(),
                TargetPlatformKey =
                    PlatformVariantPolicy.GetPlatformKey(target)
            };

            if (!PlatformVariantPolicy.IsSimpleName(
                result.ContentVersion))
            {
                AddError(
                    result,
                    BuildErrorCode.InvalidContentVersion,
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
                    BuildErrorCode.InvalidBinding,
                    null,
                    null,
                    "Bindings are null or empty.");
                return result;
            }

            var groupToAssets =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var groupDependencies =
                new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var seenSourcePaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAssetKeys =
                new HashSet<string>(StringComparer.Ordinal);
            var sortedBindings =
                new List<AssetBinding>();

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];

                if (binding == null)
                {
                    AddError(
                        result,
                        BuildErrorCode.InvalidBinding,
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
                        BuildErrorCode.InvalidBinding,
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
                        BuildErrorCode.InvalidBinding,
                        binding.assetKey,
                        sourcePath,
                        "Asset does not exist at source path.");
                    continue;
                }

                if (string.IsNullOrEmpty(binding.assetKey))
                {
                    AddError(
                        result,
                        BuildErrorCode.InvalidBinding,
                        binding.assetKey,
                        sourcePath,
                        "AssetKey is empty.");
                    continue;
                }

                if (!seenAssetKeys.Add(binding.assetKey))
                {
                    AddError(
                        result,
                        BuildErrorCode.DuplicateAssetKey,
                        binding.assetKey,
                        sourcePath,
                        "Duplicate AssetKey in build plan.");
                    continue;
                }

                if (!seenSourcePaths.Add(sourcePath))
                {
                    AddError(
                        result,
                        BuildErrorCode.DuplicateSourcePath,
                        binding.assetKey,
                        sourcePath,
                        "Duplicate source path in build plan.");
                    continue;
                }

                if (!PlatformVariantPolicy.IsSimpleName(
                    binding.bundleGroupName))
                {
                    AddError(
                        result,
                        BuildErrorCode.InvalidBinding,
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

                if (!groupDependencies.TryGetValue(
                    binding.bundleGroupName,
                    out var declaredDependencies))
                {
                    declaredDependencies =
                        new SortedSet<string>(StringComparer.Ordinal);
                    groupDependencies.Add(
                        binding.bundleGroupName,
                        declaredDependencies);
                }

                if (binding.dependencies == null)
                {
                    continue;
                }

                for (var d = 0; d < binding.dependencies.Length; d++)
                {
                    var dependency = binding.dependencies[d];

                    if (string.IsNullOrEmpty(dependency))
                    {
                        continue;
                    }

                    if (!PlatformVariantPolicy.IsSimpleName(dependency))
                    {
                        AddError(
                            result,
                            BuildErrorCode.InvalidBinding,
                            binding.assetKey,
                            sourcePath,
                            "Invalid dependency group name: " + dependency);
                        continue;
                    }

                    if (string.Equals(
                        dependency,
                        binding.bundleGroupName,
                        StringComparison.Ordinal))
                    {
                        AddError(
                            result,
                            BuildErrorCode.InvalidBinding,
                            binding.assetKey,
                            sourcePath,
                            "Bundle group cannot depend on itself: " +
                            dependency);
                        continue;
                    }

                    declaredDependencies.Add(dependency);
                }
            }

            // Dependencies must resolve inside this build plan: a manifest that
            // names a group the build never produced could not be activated.
            foreach (var pair in groupDependencies)
            {
                foreach (var dependency in pair.Value)
                {
                    if (!groupToAssets.ContainsKey(dependency))
                    {
                        AddError(
                            result,
                            BuildErrorCode.InvalidBinding,
                            null,
                            null,
                            "Bundle group '" + pair.Key +
                            "' depends on '" + dependency +
                            "', which is not part of this build plan.");
                    }
                }
            }

            if (result.Errors.Count > 0)
            {
                return result;
            }

            var projectRoot =
                PlatformVariantPolicy.GetProjectRoot();

            var outputDirectory = string.IsNullOrEmpty(outputRootOverride)
                ? PlatformVariantPolicy.GetPlatformOutputDirectory(
                    projectRoot,
                    result.ContentVersion,
                    target)
                : Path.Combine(
                    Path.GetFullPath(outputRootOverride),
                    result.ContentVersion);

            result.OutputDirectory = outputDirectory;

            // Build into a staging folder and publish only after the manifest
            // is written. A failed build must not delete the last good output.
            // The staging folder keeps the final folder name (BuildPipeline
            // names the bundle manifest after the output folder), so it lives
            // one level down inside a throwaway parent.
            var stagingRoot = Path.Combine(
                Path.GetDirectoryName(outputDirectory) ?? outputDirectory,
                ".building-" + Guid.NewGuid().ToString("N"));

            var stagingDirectory = Path.Combine(
                stagingRoot,
                Path.GetFileName(outputDirectory));

            try
            {
                Directory.CreateDirectory(stagingDirectory);
            }
            catch (Exception exception)
            {
                AddError(
                    result,
                    BuildErrorCode.IoError,
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
                    stagingDirectory,
                    builds,
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    target);
            }
            catch (Exception exception)
            {
                AddError(
                    result,
                    BuildErrorCode.BuildPipelineFailed,
                    null,
                    null,
                    exception.GetType().Name + ": " +
                    exception.Message);

                TryDeleteDirectory(stagingRoot);
                return result;
            }

            if (builtManifest == null)
            {
                AddError(
                    result,
                    BuildErrorCode.BuildManifestNull,
                    null,
                    null,
                    "BuildPipeline returned a null AssetBundleManifest.");

                TryDeleteDirectory(stagingRoot);
                return result;
            }

            var builtBundles = new HashSet<string>(
                builtManifest.GetAllAssetBundles(),
                StringComparer.Ordinal);

            var bundleRecords =
                new List<BundleRecord>();

            var loadedBundles =
                new Dictionary<string, AssetBundle>(StringComparer.Ordinal);

            var assetRecords =
                new List<AssetRecord>();

            try
            {
                for (var i = 0; i < groupNames.Count; i++)
                {
                    var groupName = groupNames[i];

                    if (!builtBundles.Contains(groupName))
                    {
                        AddError(
                            result,
                            BuildErrorCode.ExpectedBundleNotBuilt,
                            null,
                            null,
                            "Expected bundle was not built: " +
                            groupName);
                        continue;
                    }

                    var bundlePath = Path.Combine(
                        stagingDirectory,
                        groupName);

                    if (!File.Exists(bundlePath))
                    {
                        AddError(
                            result,
                            BuildErrorCode.BundleFileMissing,
                            null,
                            null,
                            "Built bundle file is missing: " +
                            bundlePath);
                        continue;
                    }

                    if (!HashUtility.TryComputeSha256Hex(
                        bundlePath,
                        out var hashHex,
                        out var hashError))
                    {
                        AddError(
                            result,
                            BuildErrorCode.HashComputeFailed,
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
                            BuildErrorCode.IoError,
                            null,
                            null,
                            "Failed to read bundle size. " +
                            exception.GetType().Name + ": " +
                            exception.Message);
                        continue;
                    }

                    bundleRecords.Add(new BundleRecord
                    {
                        bundleGroupName = groupName,
                        bundleFileName = groupName,
                        sizeBytes = sizeBytes,
                        sha256Hex = hashHex,
                        dependencies = groupDependencies.TryGetValue(
                            groupName,
                            out var declared)
                            ? new List<string>(declared).ToArray()
                            : Array.Empty<string>()
                    });

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
                            BuildErrorCode.BundleLoadFailed,
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
                            BuildErrorCode.InternalAssetNameMissing,
                            binding.assetKey,
                            sourcePath,
                            "Source path was not found inside bundle. group=" +
                            binding.bundleGroupName);
                        continue;
                    }

                    assetRecords.Add(new AssetRecord
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
                TryDeleteDirectory(stagingRoot);
                TryWriteBuildResultReport(result);
                return result;
            }

            var manifest = new PlatformManifest
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

            try
            {
                var manifestPath = Path.Combine(
                    stagingDirectory,
                    "lesson43-manifest.json");

                WriteJson(manifestPath, manifest);
            }
            catch (Exception exception)
            {
                result.Manifest = null;

                AddError(
                    result,
                    BuildErrorCode.IoError,
                    null,
                    null,
                    "Failed to write manifest. " +
                    exception.GetType().Name + ": " +
                    exception.Message);

                TryDeleteDirectory(stagingRoot);
                TryWriteBuildResultReport(result);
                return result;
            }

            if (!TryPublishDirectory(
                stagingDirectory,
                outputDirectory,
                out var publishError))
            {
                result.Manifest = null;

                AddError(
                    result,
                    BuildErrorCode.IoError,
                    null,
                    null,
                    "Failed to publish build output. " + publishError);

                TryDeleteDirectory(stagingRoot);
                TryWriteBuildResultReport(result);
                return result;
            }

            result.Success = true;

            TryDeleteDirectory(stagingRoot);

            TryWriteBuildResultReport(result);
            return result;
        }

        /// <summary>
        /// Swaps the freshly built staging folder into place. The previous
        /// output is kept as <c>.previous</c> until the swap succeeded, so a
        /// failed publish restores it instead of losing both.
        /// </summary>
        private static bool TryPublishDirectory(
            string stagingDirectory,
            string targetDirectory,
            out string error)
        {
            error = null;

            var previousDirectory = targetDirectory + ".previous";

            try
            {
                if (Directory.Exists(previousDirectory))
                {
                    Directory.Delete(previousDirectory, true);
                }

                if (Directory.Exists(targetDirectory))
                {
                    Directory.Move(targetDirectory, previousDirectory);
                }

                try
                {
                    Directory.Move(stagingDirectory, targetDirectory);
                }
                catch
                {
                    if (!Directory.Exists(targetDirectory) &&
                        Directory.Exists(previousDirectory))
                    {
                        Directory.Move(previousDirectory, targetDirectory);
                    }

                    throw;
                }

                TryDeleteDirectory(previousDirectory);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Best effort. A leftover staging folder is harmless: the next
                // build deletes it before starting.
            }
        }

        private static void TryWriteBuildResultReport(
            BuildResult result)
        {
            try
            {
                var projectRoot =
                    PlatformVariantPolicy.GetProjectRoot();

                var directory = Path.Combine(
                    PlatformVariantPolicy.GetBuildRoot(projectRoot),
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
            BuildResult result,
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

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/');
        }
    }
}
