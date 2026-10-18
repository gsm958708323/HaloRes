using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Game.AssetSystem.Editor.Scanning.L42
{
    public sealed class AssetScanner
    {
        public AssetScanReport Scan(
            string[] scanRoots,
            string[] extensions,
            AssetGroupRuleSet ruleSet)
        {
            var report = new AssetScanReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                ScanRoots = new List<string>(
                    scanRoots ?? Array.Empty<string>())
            };

            if (ruleSet == null)
            {
                report.Errors.Add(new AssetScanError
                {
                    Code = AssetScanErrorCode.RuleSetNull,
                    Message = "Rule set is null."
                });
                return report;
            }

            var ruleErrors = AssetScanValidator.ValidateRuleSet(ruleSet);

            if (ruleErrors.Count > 0)
            {
                report.Errors.AddRange(ruleErrors);
                return report;
            }

            if (scanRoots == null || scanRoots.Length == 0)
            {
                report.Errors.Add(new AssetScanError
                {
                    Code = AssetScanErrorCode.InvalidSourcePathPrefix,
                    Message = "No scan roots provided."
                });
                return report;
            }

            for (var i = 0; i < scanRoots.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(scanRoots[i]))
                {
                    report.Errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.ScanRootMissing,
                        Message = "Scan root does not exist: " + scanRoots[i]
                    });
                }
            }

            if (report.Errors.Count > 0)
            {
                return report;
            }

            var guids = AssetDatabase.FindAssets(string.Empty, scanRoots);
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            var keyToBindings =
                new Dictionary<string, List<AssetBindingRecord>>(StringComparer.Ordinal);
            var ruleMatched = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < guids.Length; i++)
            {
                var guid = guids[i];
                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(path))
                {
                    report.Warnings.Add(new AssetScanWarning
                    {
                        Code = AssetScanWarningCode.AssetGuidMissing,
                        Message = "GUID could not be resolved: " + guid
                    });
                    continue;
                }

                if (AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                if (!IsExtensionAllowed(path, extensions))
                {
                    continue;
                }

                if (!seenPaths.Add(path))
                {
                    continue;
                }

                report.ScannedAssetCount++;

                if (!ruleSet.TryMatch(path, out var rule))
                {
                    report.Errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.NoRuleMatched,
                        SourcePath = path,
                        Message = "No group rule matched this asset."
                    });
                    continue;
                }

                ruleMatched.Add(rule.Name);

                if (!AssetKeyGenerator.TryGenerate(
                    rule,
                    path,
                    out var key,
                    out var generationError))
                {
                    report.Errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.KeyGenerationFailed,
                        SourcePath = path,
                        Message = generationError
                    });
                    continue;
                }

                var binding = new AssetBindingRecord
                {
                    SourcePath = path,
                    AssetGuid = guid,
                    AssetKey = key,
                    BundleGroupName = rule.BundleGroupName,
                    RuleName = rule.Name,
                    IsExplicitKey = rule.IsOverride(path)
                };

                report.Bindings.Add(binding);

                if (!keyToBindings.TryGetValue(key, out var bindingsForKey))
                {
                    bindingsForKey = new List<AssetBindingRecord>();
                    keyToBindings.Add(key, bindingsForKey);
                }

                bindingsForKey.Add(binding);
            }

            foreach (var pair in keyToBindings)
            {
                if (pair.Value.Count <= 1)
                {
                    continue;
                }

                var conflictingPaths = string.Join(
                    ", ",
                    pair.Value.Select(b => b.SourcePath));

                for (var i = 0; i < pair.Value.Count; i++)
                {
                    var binding = pair.Value[i];

                    report.Errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.DuplicateKey,
                        SourcePath = binding.SourcePath,
                        AssetKey = binding.AssetKey,
                        Message = "Duplicate AssetKey. Conflicting source paths: " +
                                  conflictingPaths
                    });
                }
            }

            for (var i = 0; i < report.Bindings.Count; i++)
            {
                var binding = report.Bindings[i];

                if (!AssetKeyFormat.IsValid(binding.AssetKey, out var keyError))
                {
                    report.Errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.InvalidKeyFormat,
                        SourcePath = binding.SourcePath,
                        AssetKey = binding.AssetKey,
                        Message = keyError
                    });
                }
            }

            var groupMap =
                new Dictionary<string, AssetBundleGroupRecord>(StringComparer.Ordinal);

            for (var i = 0; i < report.Bindings.Count; i++)
            {
                var binding = report.Bindings[i];

                if (!groupMap.TryGetValue(binding.BundleGroupName, out var group))
                {
                    group = new AssetBundleGroupRecord
                    {
                        BundleGroupName = binding.BundleGroupName
                    };
                    groupMap.Add(binding.BundleGroupName, group);
                }

                if (!group.AssetKeys.Contains(binding.AssetKey))
                {
                    group.AssetKeys.Add(binding.AssetKey);
                }

                group.SourcePaths.Add(binding.SourcePath);
            }

            report.Groups.AddRange(groupMap.Values);

            report.Bindings.Sort((a, b) => string.Compare(
                a.AssetKey,
                b.AssetKey,
                StringComparison.Ordinal));

            report.Groups.Sort((a, b) => string.Compare(
                a.BundleGroupName,
                b.BundleGroupName,
                StringComparison.Ordinal));

            for (var i = 0; i < report.Groups.Count; i++)
            {
                report.Groups[i].AssetKeys.Sort(StringComparer.Ordinal);
            }

            for (var i = 0; i < ruleSet.Rules.Count; i++)
            {
                var rule = ruleSet.Rules[i];

                if (rule != null && !ruleMatched.Contains(rule.Name))
                {
                    report.Warnings.Add(new AssetScanWarning
                    {
                        Code = AssetScanWarningCode.RuleMatchedNothing,
                        Message = "Rule matched no assets: " + rule.Name
                    });
                }
            }

            return report;
        }

        private static bool IsExtensionAllowed(
            string path,
            string[] extensions)
        {
            if (extensions == null || extensions.Length == 0)
            {
                return true;
            }

            var extension = Path.GetExtension(path);

            for (var i = 0; i < extensions.Length; i++)
            {
                if (string.Equals(
                    extension,
                    extensions[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class AssetKeyGenerator
    {
        public static bool TryGenerate(
            AssetGroupRule rule,
            string sourcePath,
            out string key,
            out string error)
        {
            key = null;
            error = null;

            if (rule == null)
            {
                error = "Rule is null.";
                return false;
            }

            var normalizedSource =
                AssetGroupRule.NormalizePath(sourcePath);
            var prefix = rule.GetNormalizedSourcePathPrefix();

            if (string.IsNullOrEmpty(prefix) ||
                !normalizedSource.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                error = "Source path does not start with rule prefix. path=" +
                        sourcePath + ", prefix=" + prefix;
                return false;
            }

            if (rule.TryGetOverride(sourcePath, out var overrideKey))
            {
                if (string.IsNullOrEmpty(overrideKey))
                {
                    error = "Override key is null or empty for path: " +
                            sourcePath;
                    return false;
                }

                key = overrideKey;
                return true;
            }

            var relative = normalizedSource.Substring(prefix.Length);
            var extension = Path.GetExtension(relative);

            if (!string.IsNullOrEmpty(extension))
            {
                relative = relative.Substring(
                    0,
                    relative.Length - extension.Length);
            }

            relative = relative.ToLowerInvariant();

            if (string.IsNullOrEmpty(relative))
            {
                error = "Generated key has an empty relative part. path=" +
                        sourcePath;
                return false;
            }

            key = string.IsNullOrEmpty(rule.KeyPrefix)
                ? relative
                : rule.KeyPrefix.TrimEnd('/') + "/" + relative;

            return true;
        }
    }

    public static class AssetScanValidator
    {
        public static List<AssetScanError> ValidateRuleSet(
            AssetGroupRuleSet ruleSet)
        {
            var errors = new List<AssetScanError>();

            if (ruleSet == null)
            {
                errors.Add(new AssetScanError
                {
                    Code = AssetScanErrorCode.RuleSetNull,
                    Message = "Rule set is null."
                });
                return errors;
            }

            if (ruleSet.Rules == null)
            {
                errors.Add(new AssetScanError
                {
                    Code = AssetScanErrorCode.RuleSetNull,
                    Message = "Rule set has a null Rules list."
                });
                return errors;
            }

            var ruleNames = new HashSet<string>(StringComparer.Ordinal);
            var normalizedPrefixes =
                new Dictionary<string, string>(StringComparer.Ordinal);

            for (var i = 0; i < ruleSet.Rules.Count; i++)
            {
                var rule = ruleSet.Rules[i];

                if (rule == null)
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.RuleNull,
                        Message = "Rule at index " + i + " is null."
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.Name))
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.RuleNameEmpty,
                        Message = "Rule at index " + i +
                                  " has an empty name."
                    });
                }
                else if (!ruleNames.Add(rule.Name))
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.DuplicateRuleName,
                        Message = "Duplicate rule name: " + rule.Name
                    });
                }

                var prefix = rule.GetNormalizedSourcePathPrefix();

                if (string.IsNullOrEmpty(prefix))
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.InvalidSourcePathPrefix,
                        Message = "Rule '" + rule.Name +
                                  "' has an empty source path prefix."
                    });
                }
                else if (normalizedPrefixes.TryGetValue(
                    prefix,
                    out var existingRuleName))
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.DuplicateRulePrefix,
                        Message = "Rules '" + existingRuleName +
                                  "' and '" + rule.Name +
                                  "' have the same normalized source path prefix: " +
                                  prefix
                    });
                }
                else
                {
                    normalizedPrefixes.Add(prefix, rule.Name);
                }

                if (!AssetKeyFormat.IsValidKeyPrefix(
                    rule.KeyPrefix,
                    out var keyPrefixError))
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.InvalidKeyPrefix,
                        Message = "Rule '" + rule.Name +
                                  "' has invalid KeyPrefix. " +
                                  keyPrefixError
                    });
                }

                if (string.IsNullOrWhiteSpace(rule.BundleGroupName))
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.BundleGroupNameEmpty,
                        Message = "Rule '" + rule.Name +
                                  "' has an empty bundle group name."
                    });
                }
                else if (!AssetKeyFormat.IsValidBundleGroupName(
                    rule.BundleGroupName,
                    out var groupNameError))
                {
                    errors.Add(new AssetScanError
                    {
                        Code = AssetScanErrorCode.InvalidBundleGroupName,
                        Message = "Rule '" + rule.Name +
                                  "' has invalid BundleGroupName. " +
                                  groupNameError
                    });
                }

                var overridePaths =
                    new HashSet<string>(StringComparer.Ordinal);

                if (rule.KeyOverrides != null)
                {
                    for (var j = 0; j < rule.KeyOverrides.Count; j++)
                    {
                        var item = rule.KeyOverrides[j];

                        if (item == null)
                        {
                            errors.Add(new AssetScanError
                            {
                                Code = AssetScanErrorCode.OverrideNull,
                                Message = "Rule '" + rule.Name +
                                          "' has a null override at index " +
                                          j + "."
                            });
                            continue;
                        }

                        var normalizedSource =
                            AssetGroupRule.NormalizePath(item.SourcePath);

                        if (string.IsNullOrEmpty(normalizedSource))
                        {
                            errors.Add(new AssetScanError
                            {
                                Code = AssetScanErrorCode.OverrideOutsideRule,
                                Message = "Rule '" + rule.Name +
                                          "' has an override with empty SourcePath."
                            });
                        }
                        else if (!string.IsNullOrEmpty(prefix) &&
                                 !normalizedSource.StartsWith(
                                     prefix,
                                     StringComparison.Ordinal))
                        {
                            errors.Add(new AssetScanError
                            {
                                Code = AssetScanErrorCode.OverrideOutsideRule,
                                Message = "Rule '" + rule.Name +
                                          "' has an override outside its source prefix: " +
                                          item.SourcePath
                            });
                        }

                        if (!string.IsNullOrEmpty(normalizedSource) &&
                            !overridePaths.Add(normalizedSource))
                        {
                            errors.Add(new AssetScanError
                            {
                                Code = AssetScanErrorCode.DuplicateOverride,
                                Message = "Rule '" + rule.Name +
                                          "' has duplicate override for: " +
                                          item.SourcePath
                            });
                        }

                        if (!AssetKeyFormat.IsValid(
                            item.Key,
                            out var overrideKeyError))
                        {
                            errors.Add(new AssetScanError
                            {
                                Code = AssetScanErrorCode.InvalidOverrideKey,
                                Message = "Rule '" + rule.Name +
                                          "' has invalid override key for " +
                                          item.SourcePath + ". " +
                                          overrideKeyError
                            });
                        }
                    }
                }
            }

            return errors;
        }
    }
}
