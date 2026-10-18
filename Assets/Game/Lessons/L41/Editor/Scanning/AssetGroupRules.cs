using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Editor.Scanning.L41
{
    [Serializable]
    public sealed class AssetKeyOverride
    {
        public string SourcePath;
        public string Key;
    }

    [Serializable]
    public sealed class AssetGroupRule
    {
        public string Name;
        public string SourcePathPrefix;
        public string KeyPrefix;
        public string BundleGroupName;
        public List<AssetKeyOverride> KeyOverrides = new List<AssetKeyOverride>();

        public string GetNormalizedSourcePathPrefix()
        {
            if (string.IsNullOrWhiteSpace(SourcePathPrefix))
            {
                return string.Empty;
            }

            var prefix = SourcePathPrefix.Replace('\\', '/');

            if (!prefix.EndsWith("/", StringComparison.Ordinal))
            {
                prefix += "/";
            }

            return prefix;
        }

        public bool TryGetOverride(string sourcePath, out string key)
        {
            key = null;

            if (KeyOverrides == null || KeyOverrides.Count == 0)
            {
                return false;
            }

            var normalizedSource = NormalizePath(sourcePath);

            for (var i = 0; i < KeyOverrides.Count; i++)
            {
                var item = KeyOverrides[i];

                if (item == null)
                {
                    continue;
                }

                if (string.Equals(
                    NormalizePath(item.SourcePath),
                    normalizedSource,
                    StringComparison.Ordinal))
                {
                    key = item.Key;
                    return true;
                }
            }

            return false;
        }

        public bool IsOverride(string sourcePath)
        {
            return TryGetOverride(sourcePath, out _);
        }

        public static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/');
        }
    }

    [Serializable]
    public sealed class AssetGroupRuleSet
    {
        public List<AssetGroupRule> Rules = new List<AssetGroupRule>();

        public bool TryMatch(string sourcePath, out AssetGroupRule matchedRule)
        {
            matchedRule = null;

            if (Rules == null || Rules.Count == 0)
            {
                return false;
            }

            var normalizedSource = AssetGroupRule.NormalizePath(sourcePath);
            var bestPrefixLength = -1;

            for (var i = 0; i < Rules.Count; i++)
            {
                var rule = Rules[i];

                if (rule == null)
                {
                    continue;
                }

                var prefix = rule.GetNormalizedSourcePathPrefix();

                if (string.IsNullOrEmpty(prefix))
                {
                    continue;
                }

                if (!normalizedSource.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (prefix.Length > bestPrefixLength)
                {
                    bestPrefixLength = prefix.Length;
                    matchedRule = rule;
                }
            }

            return matchedRule != null;
        }
    }

    public static class AssetKeyFormat
    {
        public static bool IsValid(string key, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(key))
            {
                error = "Key is null or empty.";
                return false;
            }

            if (key.Length > 200)
            {
                error = "Key is longer than 200 characters.";
                return false;
            }

            if (key[0] == '/' || key[key.Length - 1] == '/')
            {
                error = "Key must not start or end with '/'.";
                return false;
            }

            if (key.Contains("//"))
            {
                error = "Key must not contain '//'.";
                return false;
            }

            if (key.Contains(".."))
            {
                error = "Key must not contain '..'.";
                return false;
            }

            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i];

                if ((c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '/' ||
                    c == '_' ||
                    c == '-' ||
                    c == '.')
                {
                    continue;
                }

                error = "Invalid character '" + c +
                        "' in key. Only lowercase a-z, 0-9, '/', '_', '-', '.' are allowed.";
                return false;
            }

            return true;
        }

        public static bool IsValidBundleGroupName(string name, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(name))
            {
                error = "Bundle group name is null or empty.";
                return false;
            }

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];

                if ((c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '_' ||
                    c == '-')
                {
                    continue;
                }

                error = "Invalid character '" + c +
                        "' in bundle group name. Only lowercase a-z, 0-9, '_', '-' are allowed.";
                return false;
            }

            return true;
        }

        public static bool IsValidKeyPrefix(string prefix, out string error)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                error = null;
                return true;
            }

            return IsValid(prefix, out error);
        }
    }
}
