using System.Collections.Generic;
using Game.AssetSystem.Editor.Building.L43;
using Game.AssetSystem.Editor.Demo.L41;
using Game.AssetSystem.Editor.Demo.L42;
using Game.AssetSystem.Editor.Demo.L43;
using Game.AssetSystem.Editor.Manifest.L42;
using NUnit.Framework;
using UnityEditor;
using Scan41 = Game.AssetSystem.Editor.Scanning.L41;
using Scan42 = Game.AssetSystem.Editor.Scanning.L42;
using Storage35 = Game.AssetSystem.Storage.L35;

namespace Game.AssetSystem.Lessons.Tests
{
    /// <summary>
    /// EditMode tests for the build-time pipeline lessons:
    /// 4.1 scan + grouping rules, 4.2 dependency graph + manifest,
    /// 4.3 AssetBundle build + validation, plus the pure validation helpers
    /// used by 3.5 (file name safety) and 4.1 (asset key format).
    /// </summary>
    public sealed class LessonBuildPipelineTests
    {
        // ------------------------------------------------------------------
        // 4.1：未匹配规则 / 非法 Key / 重复 Key 都必须显式报错并阻断 Manifest
        // ------------------------------------------------------------------
        [Test]
        public void L41_ScanReportsUnmatchedInvalidAndDuplicateKeys()
        {
            Lesson41SampleContentGenerator.Generate();

            var report = new Scan41.AssetScanner().Scan(
                new[] { Lesson41SampleContentGenerator.SampleRoot },
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
                Scan41.AssetKeyFormat.IsValid("ui/shop/shopicon", out _),
                "lowercase segmented key is valid");

            Assert.IsFalse(
                Scan41.AssetKeyFormat.IsValid("ui/shop/Invalid Name", out var spaceError),
                "space in key must be rejected");
            Assert.IsNotNull(spaceError);

            Assert.IsFalse(
                Scan41.AssetKeyFormat.IsValid(string.Empty, out _),
                "empty key must be rejected");
        }

        // ------------------------------------------------------------------
        // 4.2：干净依赖图生成 Manifest（5 assets / 3 bundles / 2 edges）
        // ------------------------------------------------------------------
        [Test]
        public void L42_CleanGraphBuildsManifest()
        {
            Lesson42SampleContentGenerator.GenerateClean();

            var scanReport = new Scan42.AssetScanner().Scan(
                new[] { Lesson42SampleContentGenerator.SampleContentRoot },
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
            Lesson43SampleContentGenerator.GenerateClean();

            var bindings = Lesson43SampleContentGenerator.GetBindings();
            var target = EditorUserBuildSettings.activeBuildTarget;

            var build = new Lesson43AssetBundleBuilder().Build(
                bindings,
                "lesson43-test",
                target);

            Assert.IsTrue(build.Success, "bundle build should succeed");
            Assert.IsNotNull(build.Manifest);
            Assert.AreEqual(3, build.Bundles.Count, "bundle count");

            var validator = new Lesson43BundleValidator();

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
            Assert.IsTrue(Storage35.AssetFileStore.IsSimpleFileName("bundle_core.bundle"));
            Assert.IsTrue(Storage35.AssetFileStore.IsSimpleFileName("v1"));

            Assert.IsFalse(
                Storage35.AssetFileStore.IsSimpleFileName("../active.json"),
                "path traversal must be rejected");
            Assert.IsFalse(
                Storage35.AssetFileStore.IsSimpleFileName("content/v1"),
                "directory separators must be rejected");
            Assert.IsFalse(
                Storage35.AssetFileStore.IsSimpleFileName(string.Empty),
                "empty name must be rejected");
        }

        private static Scan41.AssetGroupRuleSet CreateRuleSet41()
        {
            var sampleRoot = Lesson41SampleContentGenerator.SampleRoot;

            var ruleSet = new Scan41.AssetGroupRuleSet();

            ruleSet.Rules = new List<Scan41.AssetGroupRule>
            {
                new Scan41.AssetGroupRule
                {
                    Name = "ui.shop",
                    SourcePathPrefix = sampleRoot + "/UI/Shop/",
                    KeyPrefix = "ui/shop",
                    BundleGroupName = "ui_shop",
                    KeyOverrides = new List<Scan41.AssetKeyOverride>()
                },
                new Scan41.AssetGroupRule
                {
                    Name = "battle.fx",
                    SourcePathPrefix = sampleRoot + "/Battle/Fx/",
                    KeyPrefix = "battle/fx",
                    BundleGroupName = "battle_fx",
                    KeyOverrides = new List<Scan41.AssetKeyOverride>
                    {
                        new Scan41.AssetKeyOverride
                        {
                            SourcePath = sampleRoot + "/Battle/Fx/FxHit.txt",
                            Key = "battle/fx/fx_hit"
                        },
                        new Scan41.AssetKeyOverride
                        {
                            SourcePath = sampleRoot + "/Battle/Fx/FxHitAlt.txt",
                            Key = "battle/fx/fx_hit"
                        }
                    }
                },
                new Scan41.AssetGroupRule
                {
                    Name = "battle.audio",
                    SourcePathPrefix = sampleRoot + "/Battle/Audio/",
                    KeyPrefix = "battle/audio",
                    BundleGroupName = "battle_audio",
                    KeyOverrides = new List<Scan41.AssetKeyOverride>()
                }
            };

            return ruleSet;
        }

        private static Scan42.AssetGroupRuleSet CreateRuleSet42()
        {
            var sampleRoot = Lesson42SampleContentGenerator.SampleContentRoot;

            var ruleSet = new Scan42.AssetGroupRuleSet();

            ruleSet.Rules = new List<Scan42.AssetGroupRule>
            {
                new Scan42.AssetGroupRule
                {
                    Name = "ui.shop",
                    SourcePathPrefix = sampleRoot + "/UI/Shop/",
                    KeyPrefix = "ui/shop",
                    BundleGroupName = "ui_shop",
                    KeyOverrides = new List<Scan42.AssetKeyOverride>()
                },
                new Scan42.AssetGroupRule
                {
                    Name = "battle.fx",
                    SourcePathPrefix = sampleRoot + "/Battle/Fx/",
                    KeyPrefix = "battle/fx",
                    BundleGroupName = "battle_fx",
                    KeyOverrides = new List<Scan42.AssetKeyOverride>()
                },
                new Scan42.AssetGroupRule
                {
                    Name = "battle.audio",
                    SourcePathPrefix = sampleRoot + "/Battle/Audio/",
                    KeyPrefix = "battle/audio",
                    BundleGroupName = "battle_audio",
                    KeyOverrides = new List<Scan42.AssetKeyOverride>()
                }
            };

            return ruleSet;
        }
    }
}
