using System.Collections.Generic;
using Game.AssetSystem.Editor.Building;
using Game.AssetSystem.Editor.Manifest;
using Game.AssetSystem.Editor.SampleContent;
using Game.AssetSystem.Editor.Scanning;
using Scanning = Game.AssetSystem.Editor.Scanning;
using Game.AssetSystem.Storage;
using NUnit.Framework;
using UnityEditor;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// EditMode tests for the build-time pipeline:
    /// scan + grouping rules, dependency graph + manifest, AssetBundle build +
    /// validation, plus the pure validation helpers used by the storage layer
    /// (file name safety) and the scanner (asset key format).
    /// </summary>
    public sealed class BuildPipelineTests
    {
        // ------------------------------------------------------------------
        // 4.1：未匹配规则 / 非法 Key / 重复 Key 都必须显式报错并阻断 Manifest
        // ------------------------------------------------------------------
        [Test]
        public void L41_ScanReportsUnmatchedInvalidAndDuplicateKeys()
        {
            ScanSampleContent.Generate();

            var report = new Scanning.AssetScanner().Scan(
                new[] { ScanSampleContent.SampleRoot },
                new[] { ".txt" },
                CreateRuleSet41());

            Assert.AreEqual(7, report.ScannedAssetCount, "scanned assets");
            Assert.AreEqual(6, report.Bindings.Count, "bindings");
            Assert.AreEqual(3, report.Groups.Count, "groups");
            Assert.AreEqual(4, report.Errors.Count, "errors");
            Assert.AreEqual(0, report.Warnings.Count, "warnings");
            Assert.IsTrue(report.HasFatalErrors, "errors must block manifest generation");

            var codes = new List<string>();

            for (var i = 0; i < report.Errors.Count; i++)
            {
                codes.Add(report.Errors[i].Code.ToString());
            }

            Assert.Contains("NoRuleMatched", codes);
            Assert.Contains("InvalidKeyFormat", codes);
            Assert.Contains("DuplicateKey", codes);
        }

        // ------------------------------------------------------------------
        // 4.1：Key 格式校验（合法小写分段，非法空格/大写）
        // ------------------------------------------------------------------
        [Test]
        public void L41_AssetKeyFormatRejectsInvalidKeys()
        {
            Assert.IsTrue(
                Scanning.AssetKeyFormat.IsValid("ui/shop/shopicon", out _),
                "lowercase segmented key is valid");

            Assert.IsFalse(
                Scanning.AssetKeyFormat.IsValid("ui/shop/Invalid Name", out var spaceError),
                "space in key must be rejected");
            Assert.IsNotNull(spaceError);

            Assert.IsFalse(
                Scanning.AssetKeyFormat.IsValid(string.Empty, out _),
                "empty key must be rejected");
        }

        // ------------------------------------------------------------------
        // 4.2：干净依赖图生成 Manifest（5 assets / 3 bundles / 2 edges）
        // ------------------------------------------------------------------
        [Test]
        public void L42_CleanGraphBuildsManifest()
        {
            GraphSampleContent.GenerateClean();

            var scanReport = new Scanning.AssetScanner().Scan(
                new[] { GraphSampleContent.SampleContentRoot },
                new[] { ".asset" },
                CreateRuleSet42());

            Assert.AreEqual(5, scanReport.ScannedAssetCount);
            Assert.AreEqual(5, scanReport.Bindings.Count);
            Assert.AreEqual(0, scanReport.Errors.Count, "clean sample must scan clean");

            var buildResult = new BundleDependencyBuilder().Build(scanReport, "lesson42-test");

            Assert.IsTrue(buildResult.Success, "manifest build should succeed");
            Assert.IsNotNull(buildResult.Manifest);
            Assert.AreEqual(5, buildResult.AssetCount, "manifest assets");
            Assert.AreEqual(3, buildResult.BundleCount, "manifest bundles");
            Assert.AreEqual(2, buildResult.Edges.Count, "bundle edges");
            Assert.AreEqual(0, buildResult.Errors.Count, "build errors");
        }

        // ------------------------------------------------------------------
        // 4.3：真实构建 AssetBundle 并逐个 LoadAsset 校验；平台不匹配必须失败
        // ------------------------------------------------------------------
        [Test]
        public void L43_BuildsRealBundlesAndRejectsWrongPlatform()
        {
            BundleSampleContent.GenerateClean();

            var bindings = BundleSampleContent.GetBindings();
            var target = EditorUserBuildSettings.activeBuildTarget;

            var build = new AssetBundleBuilder().Build(
                bindings,
                "lesson43-test",
                target);

            Assert.IsTrue(build.Success, "bundle build should succeed");
            Assert.IsNotNull(build.Manifest);
            Assert.AreEqual(3, build.Bundles.Count, "bundle count");

            var validator = new BundleValidator();

            var validation = validator.Validate(
                build.OutputDirectory,
                build.Manifest,
                bindings,
                target);

            Assert.IsTrue(validation.Success, "validation should succeed");
            Assert.AreEqual(5, validation.AssetsChecked, "assets checked");
            Assert.AreEqual(3, validation.BundlesLoaded, "bundles loaded");

            var wrongTarget = target == BuildTarget.Android
                ? BuildTarget.StandaloneWindows64
                : BuildTarget.Android;

            var wrongValidation = validator.Validate(
                build.OutputDirectory,
                build.Manifest,
                bindings,
                wrongTarget);

            Assert.IsFalse(
                wrongValidation.Success,
                "platform key mismatch must fail validation");
        }

        // ------------------------------------------------------------------
        // 3.5：文件名安全（拒绝路径穿越 / 分隔符）
        // ------------------------------------------------------------------
        [Test]
        public void L35_StorageRejectsUnsafeFileNames()
        {
            Assert.IsTrue(Storage.AssetFileStore.IsSimpleFileName("bundle_core.bundle"));
            Assert.IsTrue(Storage.AssetFileStore.IsSimpleFileName("v1"));

            Assert.IsFalse(
                Storage.AssetFileStore.IsSimpleFileName("../active.json"),
                "path traversal must be rejected");
            Assert.IsFalse(
                Storage.AssetFileStore.IsSimpleFileName("content/v1"),
                "directory separators must be rejected");
            Assert.IsFalse(
                Storage.AssetFileStore.IsSimpleFileName(string.Empty),
                "empty name must be rejected");
        }

        private static Scanning.AssetGroupRuleSet CreateRuleSet41()
        {
            var sampleRoot = ScanSampleContent.SampleRoot;

            var ruleSet = new Scanning.AssetGroupRuleSet();

            ruleSet.Rules = new List<Scanning.AssetGroupRule>
            {
                new Scanning.AssetGroupRule
                {
                    Name = "ui.shop",
                    SourcePathPrefix = sampleRoot + "/UI/Shop/",
                    KeyPrefix = "ui/shop",
                    BundleGroupName = "ui_shop",
                    KeyOverrides = new List<Scanning.AssetKeyOverride>()
                },
                new Scanning.AssetGroupRule
                {
                    Name = "battle.fx",
                    SourcePathPrefix = sampleRoot + "/Battle/Fx/",
                    KeyPrefix = "battle/fx",
                    BundleGroupName = "battle_fx",
                    KeyOverrides = new List<Scanning.AssetKeyOverride>
                    {
                        new Scanning.AssetKeyOverride
                        {
                            SourcePath = sampleRoot + "/Battle/Fx/FxHit.txt",
                            Key = "battle/fx/fx_hit"
                        },
                        new Scanning.AssetKeyOverride
                        {
                            SourcePath = sampleRoot + "/Battle/Fx/FxHitAlt.txt",
                            Key = "battle/fx/fx_hit"
                        }
                    }
                },
                new Scanning.AssetGroupRule
                {
                    Name = "battle.audio",
                    SourcePathPrefix = sampleRoot + "/Battle/Audio/",
                    KeyPrefix = "battle/audio",
                    BundleGroupName = "battle_audio",
                    KeyOverrides = new List<Scanning.AssetKeyOverride>()
                }
            };

            return ruleSet;
        }

        private static Scanning.AssetGroupRuleSet CreateRuleSet42()
        {
            var sampleRoot = GraphSampleContent.SampleContentRoot;

            var ruleSet = new Scanning.AssetGroupRuleSet();

            ruleSet.Rules = new List<Scanning.AssetGroupRule>
            {
                new Scanning.AssetGroupRule
                {
                    Name = "ui.shop",
                    SourcePathPrefix = sampleRoot + "/UI/Shop/",
                    KeyPrefix = "ui/shop",
                    BundleGroupName = "ui_shop",
                    KeyOverrides = new List<Scanning.AssetKeyOverride>()
                },
                new Scanning.AssetGroupRule
                {
                    Name = "battle.fx",
                    SourcePathPrefix = sampleRoot + "/Battle/Fx/",
                    KeyPrefix = "battle/fx",
                    BundleGroupName = "battle_fx",
                    KeyOverrides = new List<Scanning.AssetKeyOverride>()
                },
                new Scanning.AssetGroupRule
                {
                    Name = "battle.audio",
                    SourcePathPrefix = sampleRoot + "/Battle/Audio/",
                    KeyPrefix = "battle/audio",
                    BundleGroupName = "battle_audio",
                    KeyOverrides = new List<Scanning.AssetKeyOverride>()
                }
            };

            return ruleSet;
        }
    }
}
