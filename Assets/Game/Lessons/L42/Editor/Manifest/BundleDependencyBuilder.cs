using System;
using System.Collections.Generic;
using Game.AssetSystem.Editor.Scanning.L42;
using UnityEditor;

namespace Game.AssetSystem.Editor.Manifest.L42
{
    public sealed class BundleDependencyBuilder
    {
        public const int ManifestSchemaVersion = 1;

        public BundleGraphBuildResult Build(
            AssetScanReport scanReport,
            string contentVersion)
        {
            var result = new BundleGraphBuildResult();

            if (scanReport == null)
            {
                AddError(
                    result,
                    BundleGraphErrorCode.ScanReportNull,
                    null,
                    null,
                    null,
                    "Scan report is null.");
                return result;
            }

            if (scanReport.HasFatalErrors)
            {
                AddError(
                    result,
                    BundleGraphErrorCode.ScanReportHasErrors,
                    null,
                    null,
                    null,
                    "Scan report has " + scanReport.Errors.Count +
                    " fatal error(s). Manifest generation is blocked.");
                return result;
            }

            if (scanReport.Bindings == null ||
                scanReport.Bindings.Count == 0)
            {
                AddError(
                    result,
                    BundleGraphErrorCode.NoBindings,
                    null,
                    null,
                    null,
                    "Scan report has no bindings.");
                return result;
            }

            if (!IsValidContentVersion(contentVersion))
            {
                AddError(
                    result,
                    BundleGraphErrorCode.InvalidContentVersion,
                    null,
                    null,
                    null,
                    "Invalid content version: " +
                    (contentVersion ?? "<null>"));
                return result;
            }

            var sourceToBinding =
                new Dictionary<string, AssetBindingRecord>(StringComparer.Ordinal);
            var keyToBinding =
                new Dictionary<string, AssetBindingRecord>(StringComparer.Ordinal);
            var groupNames =
                new SortedSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < scanReport.Bindings.Count; i++)
            {
                var binding = scanReport.Bindings[i];

                if (binding == null)
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        null,
                        null,
                        null,
                        "Binding at index " + i + " is null.");
                    continue;
                }

                var sourcePath = NormalizePath(binding.SourcePath);

                if (string.IsNullOrEmpty(sourcePath) ||
                    string.IsNullOrEmpty(binding.AssetKey) ||
                    string.IsNullOrEmpty(binding.BundleGroupName))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        binding.AssetKey,
                        sourcePath,
                        null,
                        "Binding has empty source path, asset key, or bundle group.");
                    continue;
                }

                if (sourceToBinding.ContainsKey(sourcePath))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.DuplicateSourcePath,
                        binding.AssetKey,
                        sourcePath,
                        null,
                        "Duplicate source path in scan report.");
                    continue;
                }

                sourceToBinding.Add(sourcePath, binding);

                if (keyToBinding.ContainsKey(binding.AssetKey))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.DuplicateAssetKey,
                        binding.AssetKey,
                        sourcePath,
                        null,
                        "Duplicate asset key in scan report.");
                }
                else
                {
                    keyToBinding.Add(binding.AssetKey, binding);
                }

                groupNames.Add(binding.BundleGroupName);
            }

            if (result.Errors.Count > 0)
            {
                return result;
            }

            var groupNameList = new List<string>(groupNames);
            var groupToId =
                new Dictionary<string, int>(StringComparer.Ordinal);
            var bundleRecords = new List<ContentManifestBundleRecord>();
            var dependencySets = new List<HashSet<int>>();

            for (var i = 0; i < groupNameList.Count; i++)
            {
                groupToId.Add(groupNameList[i], i);

                bundleRecords.Add(new ContentManifestBundleRecord
                {
                    bundleId = i,
                    bundleGroupName = groupNameList[i],
                    directDependencies = new List<int>()
                });

                dependencySets.Add(new HashSet<int>());
            }

            var sortedBindings =
                new List<AssetBindingRecord>(scanReport.Bindings);

            sortedBindings.Sort((a, b) => string.Compare(
                a.AssetKey,
                b.AssetKey,
                StringComparison.Ordinal));

            var edgeKeys =
                new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < sortedBindings.Count; i++)
            {
                var binding = sortedBindings[i];
                var sourcePath = NormalizePath(binding.SourcePath);

                if (!sourceToBinding.ContainsKey(sourcePath))
                {
                    continue;
                }

                var fromBundleId =
                    groupToId[binding.BundleGroupName];

                string[] dependencies;

                try
                {
                    dependencies = AssetDatabase.GetDependencies(
                        sourcePath,
                        false);
                }
                catch (Exception exception)
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.DependencyQueryFailed,
                        binding.AssetKey,
                        sourcePath,
                        null,
                        exception.GetType().Name + ": " +
                        exception.Message);
                    continue;
                }

                for (var j = 0; j < dependencies.Length; j++)
                {
                    var dependencyPath =
                        NormalizePath(dependencies[j]);

                    if (string.IsNullOrEmpty(dependencyPath))
                    {
                        continue;
                    }

                    if (string.Equals(
                        dependencyPath,
                        sourcePath,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (AssetDependencyPolicy.ShouldIgnore(
                        dependencyPath,
                        out _))
                    {
                        continue;
                    }

                    if (!sourceToBinding.TryGetValue(
                        dependencyPath,
                        out var dependencyBinding))
                    {
                        AddError(
                            result,
                            BundleGraphErrorCode.UnmappedDependency,
                            binding.AssetKey,
                            sourcePath,
                            dependencyPath,
                            "Dependency is not present in scan bindings. dependency=" +
                            dependencyPath);
                        continue;
                    }

                    var toBundleId =
                        groupToId[dependencyBinding.BundleGroupName];

                    if (toBundleId == fromBundleId)
                    {
                        continue;
                    }

                    if (dependencySets[fromBundleId].Add(toBundleId))
                    {
                        var edgeKey =
                            fromBundleId + "->" + toBundleId;

                        if (edgeKeys.Add(edgeKey))
                        {
                            result.Edges.Add(
                                new BundleDependencyEdgeRecord
                                {
                                    FromBundleId = fromBundleId,
                                    FromBundleGroupName =
                                        binding.BundleGroupName,
                                    ToBundleId = toBundleId,
                                    ToBundleGroupName =
                                        dependencyBinding.BundleGroupName,
                                    FromAssetKey = binding.AssetKey,
                                    DependencyPath = dependencyPath
                                });

                            result.Warnings.Add(
                                new BundleGraphWarning
                                {
                                    Code =
                                        BundleGraphWarningCode.CrossGroupDependency,
                                    AssetKey = binding.AssetKey,
                                    Message =
                                        "Cross-group dependency: " +
                                        binding.BundleGroupName +
                                        " -> " +
                                        dependencyBinding.BundleGroupName +
                                        " via " + dependencyPath
                                });
                        }
                    }
                }
            }

            result.Edges.Sort((a, b) =>
            {
                var compare = a.FromBundleId.CompareTo(b.FromBundleId);

                return compare != 0
                    ? compare
                    : a.ToBundleId.CompareTo(b.ToBundleId);
            });

            for (var i = 0; i < bundleRecords.Count; i++)
            {
                var direct = new List<int>(dependencySets[i]);
                direct.Sort();
                bundleRecords[i].directDependencies = direct;
            }

            if (TryFindCycle(
                bundleRecords,
                out var cyclePath))
            {
                AddError(
                    result,
                    BundleGraphErrorCode.CircularBundleDependency,
                    null,
                    null,
                    null,
                    "Circular bundle dependency: " +
                    FormatCycle(cyclePath, bundleRecords));
            }

            var manifest = new ContentManifestRecord
            {
                schemaVersion = ManifestSchemaVersion,
                contentVersion = contentVersion.Trim(),
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                assets = new List<ContentManifestAssetRecord>(),
                bundles = bundleRecords
            };

            for (var i = 0; i < sortedBindings.Count; i++)
            {
                var binding = sortedBindings[i];
                var groupId = groupToId[binding.BundleGroupName];

                manifest.assets.Add(
                    new ContentManifestAssetRecord
                    {
                        assetKey = binding.AssetKey,
                        bundleId = groupId,
                        bundleGroupName = binding.BundleGroupName,
                        internalAssetPath = NormalizePath(binding.SourcePath),
                        sourcePath = NormalizePath(binding.SourcePath)
                    });
            }

            ValidateManifest(manifest, result);

            result.AssetCount = manifest.assets.Count;
            result.BundleCount = manifest.bundles.Count;
            result.Success = result.Errors.Count == 0;
            result.Manifest = result.Success ? manifest : null;

            return result;
        }

        private static void ValidateManifest(
            ContentManifestRecord manifest,
            BundleGraphBuildResult result)
        {
            if (manifest.assets == null ||
                manifest.assets.Count == 0)
            {
                AddError(
                    result,
                    BundleGraphErrorCode.InvalidManifest,
                    null,
                    null,
                    null,
                    "Manifest has no assets.");
            }

            if (manifest.bundles == null ||
                manifest.bundles.Count == 0)
            {
                AddError(
                    result,
                    BundleGraphErrorCode.InvalidManifest,
                    null,
                    null,
                    null,
                    "Manifest has no bundles.");
            }

            var bundleIds = new HashSet<int>();
            var bundleNames =
                new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.bundles.Count; i++)
            {
                var bundle = manifest.bundles[i];

                if (bundle == null)
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        null,
                        null,
                        null,
                        "Bundle at index " + i + " is null.");
                    continue;
                }

                if (!bundleIds.Add(bundle.bundleId))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        null,
                        null,
                        null,
                        "Duplicate bundle id: " + bundle.bundleId);
                }

                if (!bundleNames.Add(bundle.bundleGroupName))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.DuplicateBundleGroup,
                        null,
                        null,
                        null,
                        "Duplicate bundle group: " +
                        bundle.bundleGroupName);
                }

                if (bundle.directDependencies == null)
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        null,
                        null,
                        null,
                        "Bundle has null dependency list: " +
                        bundle.bundleGroupName);
                    continue;
                }

                var seenDeps = new HashSet<int>();

                for (var j = 0;
                     j < bundle.directDependencies.Count;
                     j++)
                {
                    var dep = bundle.directDependencies[j];

                    if (!seenDeps.Add(dep))
                    {
                        AddError(
                            result,
                            BundleGraphErrorCode.InvalidManifest,
                            null,
                            null,
                            null,
                            "Duplicate dependency id " + dep +
                            " in bundle " +
                            bundle.bundleGroupName);
                    }

                    if (dep == bundle.bundleId)
                    {
                        AddError(
                            result,
                            BundleGraphErrorCode.CircularBundleDependency,
                            null,
                            null,
                            null,
                            "Bundle depends on itself: " +
                            bundle.bundleGroupName);
                    }
                }
            }

            for (var i = 0; i < manifest.bundles.Count; i++)
            {
                var bundle = manifest.bundles[i];

                if (bundle == null ||
                    bundle.directDependencies == null)
                {
                    continue;
                }

                for (var j = 0;
                     j < bundle.directDependencies.Count;
                     j++)
                {
                    var dep = bundle.directDependencies[j];

                    if (!bundleIds.Contains(dep))
                    {
                        AddError(
                            result,
                            BundleGraphErrorCode.InvalidManifest,
                            null,
                            null,
                            null,
                            "Bundle " +
                            bundle.bundleGroupName +
                            " references unknown dependency id " +
                            dep);
                    }
                }
            }

            var assetKeys =
                new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.assets.Count; i++)
            {
                var asset = manifest.assets[i];

                if (asset == null)
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        null,
                        null,
                        null,
                        "Asset at index " + i + " is null.");
                    continue;
                }

                if (!assetKeys.Add(asset.assetKey))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.DuplicateAssetKey,
                        asset.assetKey,
                        asset.sourcePath,
                        null,
                        "Duplicate asset key in manifest: " +
                        asset.assetKey);
                }

                if (!bundleIds.Contains(asset.bundleId))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        asset.assetKey,
                        asset.sourcePath,
                        null,
                        "Asset references unknown bundle id " +
                        asset.bundleId);
                }

                if (string.IsNullOrEmpty(asset.internalAssetPath))
                {
                    AddError(
                        result,
                        BundleGraphErrorCode.InvalidManifest,
                        asset.assetKey,
                        asset.sourcePath,
                        null,
                        "Asset has empty internal path.");
                }
            }
        }

        private static bool TryFindCycle(
            List<ContentManifestBundleRecord> bundles,
            out List<int> cyclePath)
        {
            var state = new int[bundles.Count];
            var stack = new List<int>();

            for (var i = 0; i < bundles.Count; i++)
            {
                if (state[i] != 0)
                {
                    continue;
                }

                if (Visit(
                    i,
                    bundles,
                    state,
                    stack,
                    out cyclePath))
                {
                    return true;
                }
            }

            cyclePath = null;
            return false;
        }

        private static bool Visit(
            int bundleId,
            List<ContentManifestBundleRecord> bundles,
            int[] state,
            List<int> stack,
            out List<int> cyclePath)
        {
            state[bundleId] = 1;
            stack.Add(bundleId);

            var bundle = bundles[bundleId];

            if (bundle.directDependencies != null)
            {
                for (var i = 0;
                     i < bundle.directDependencies.Count;
                     i++)
                {
                    var dep = bundle.directDependencies[i];

                    if (dep < 0 || dep >= bundles.Count)
                    {
                        continue;
                    }

                    if (state[dep] == 0)
                    {
                        if (Visit(
                            dep,
                            bundles,
                            state,
                            stack,
                            out cyclePath))
                        {
                            return true;
                        }
                    }
                    else if (state[dep] == 1)
                    {
                        var startIndex = stack.IndexOf(dep);

                        cyclePath = stack.GetRange(
                            startIndex,
                            stack.Count - startIndex);

                        cyclePath.Add(dep);
                        return true;
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[bundleId] = 2;
            cyclePath = null;
            return false;
        }

        private static string FormatCycle(
            List<int> cyclePath,
            List<ContentManifestBundleRecord> bundles)
        {
            var parts = new List<string>();

            for (var i = 0; i < cyclePath.Count; i++)
            {
                var id = cyclePath[i];
                var name = id >= 0 && id < bundles.Count
                    ? bundles[id].bundleGroupName
                    : "?";

                parts.Add(name + "(" + id + ")");
            }

            return string.Join(" -> ", parts);
        }

        private static void AddError(
            BundleGraphBuildResult result,
            BundleGraphErrorCode code,
            string assetKey,
            string sourcePath,
            string dependencyPath,
            string message)
        {
            result.Errors.Add(new BundleGraphError
            {
                Code = code,
                AssetKey = assetKey,
                SourcePath = sourcePath,
                DependencyPath = dependencyPath,
                Message = message
            });
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/');
        }

        private static bool IsValidContentVersion(
            string contentVersion)
        {
            if (string.IsNullOrWhiteSpace(contentVersion))
            {
                return false;
            }

            if (contentVersion == "." ||
                contentVersion == "..")
            {
                return false;
            }

            if (contentVersion.IndexOf('/') >= 0 ||
                contentVersion.IndexOf('\\') >= 0)
            {
                return false;
            }

            return contentVersion.IndexOfAny(
                System.IO.Path.GetInvalidFileNameChars()) < 0;
        }
    }
}
