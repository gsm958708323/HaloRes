using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameFramework.Asset.Tests.PlayMode
{
    public sealed class PatchTests
    {
        private string _root;
        private string _remote;
        private string _installed;
        private LocalContentServer _server;
        private PatchManager _patch;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Application.temporaryCachePath, "PatchTests", Guid.NewGuid().ToString("N"));
            _remote = Path.Combine(_root, "remote"); _installed = Path.Combine(_root, "installed");
            Directory.CreateDirectory(_remote);
            _server = new LocalContentServer(_remote);
            _patch = new PatchManager(_installed, "Windows");
        }

        [TearDown]
        public void Cleanup() { _server.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        [UnityTest]
        public IEnumerator HttpInstallVerifiesContentAndSkipsUnchangedBundles() => UniTask.ToCoroutine(async () =>
        {
            var hash = Publish(1, "first");
            var manifest = await _patch.RunAsync(_server.RootUrl);
            Assert.That(manifest.Version, Is.EqualTo(1));
            Assert.That((await _patch.LoadInstalledAsync()).Version, Is.EqualTo(1));
            var requestPath = "/bundles/" + hash + ".bundle";
            Assert.That(_server.Count(requestPath), Is.EqualTo(1));
            await _patch.RunAsync(_server.RootUrl);
            Assert.That(_server.Count(requestPath), Is.EqualTo(1));
            Assert.That(Directory.GetFiles(_installed, "*.part", SearchOption.AllDirectories), Is.Empty);
        });

        [UnityTest]
        public IEnumerator CorruptDownloadNeverActivatesNewVersion() => UniTask.ToCoroutine(async () =>
        {
            Publish(1, "first"); await _patch.RunAsync(_server.RootUrl);
            var secondHash = Publish(2, "second");
            File.WriteAllText(Path.Combine(_remote, "bundles", secondHash + ".bundle"), "broken");
            await LifecycleTests.ExpectAssetFailure(_patch.RunAsync(_server.RootUrl).AsTask(), AssetErrorCode.HashMismatch);
            Assert.That((await _patch.LoadInstalledAsync()).Version, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(_installed, "*.part", SearchOption.AllDirectories), Is.Empty);
            Publish(2, "second");
            Assert.That((await _patch.RunAsync(_server.RootUrl)).Version, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator MissingFileAndCorruptManifestCannotActivate() => UniTask.ToCoroutine(async () =>
        {
            var hash = Publish(1, "first"); File.Delete(Path.Combine(_remote, "bundles", hash + ".bundle"));
            await LifecycleTests.ExpectAssetFailure(_patch.RunAsync(_server.RootUrl).AsTask(), AssetErrorCode.DownloadFailed);
            Assert.That(File.Exists(Path.Combine(_installed, "version.json")), Is.False);
            Publish(1, "first");
            var version = VersionFile.Parse(File.ReadAllText(Path.Combine(_remote, "version.json")), "Windows");
            File.WriteAllText(Path.Combine(_remote, "manifests", version.manifestHash + ".json"), "{}");
            await LifecycleTests.ExpectAssetFailure(_patch.RunAsync(_server.RootUrl).AsTask(), AssetErrorCode.HashMismatch);
            Assert.That(File.Exists(Path.Combine(_installed, "version.json")), Is.False);
        });

        [UnityTest]
        public IEnumerator CancelledPatchLeavesPreviousVersionActive() => UniTask.ToCoroutine(async () =>
        {
            Publish(1, "first"); await _patch.RunAsync(_server.RootUrl);
            Publish(2, "second");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await LifecycleTests.ExpectCancelled(_patch.RunAsync(_server.RootUrl, ct: cts.Token).AsTask());
            }
            Assert.That((await _patch.LoadInstalledAsync()).Version, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator DowngradeAndMutablePublishedVersionAreRejected() => UniTask.ToCoroutine(async () =>
        {
            Publish(2, "first"); await _patch.RunAsync(_server.RootUrl);
            Publish(1, "old");
            await LifecycleTests.ExpectAssetFailure(_patch.RunAsync(_server.RootUrl).AsTask(), AssetErrorCode.VersionInvalid);
            Publish(2, "different");
            await LifecycleTests.ExpectAssetFailure(_patch.RunAsync(_server.RootUrl).AsTask(), AssetErrorCode.VersionInvalid);
        });

        [UnityTest]
        public IEnumerator CorruptLocalBundleIsRepairedEvenWhenVersionMatches() => UniTask.ToCoroutine(async () =>
        {
            var hash = Publish(1, "first"); await _patch.RunAsync(_server.RootUrl);
            File.WriteAllText(Path.Combine(_installed, "bundles", hash + ".bundle"), "bad");
            await _patch.RunAsync(_server.RootUrl);
            Assert.That((await _patch.LoadInstalledAsync()).Version, Is.EqualTo(1));
            Assert.That(_server.Count("/bundles/" + hash + ".bundle"), Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator DownloadTimeoutIsReportedWithoutActivation() => UniTask.ToCoroutine(async () =>
        {
            Publish(1, "first");
            _server.ResponseDelayMilliseconds = 2500;
            var patch = new PatchManager(_installed, "Windows", new FileDownloader(1));
            await LifecycleTests.ExpectAssetFailure(patch.RunAsync(_server.RootUrl).AsTask(), AssetErrorCode.DownloadFailed);
            Assert.That(File.Exists(Path.Combine(_installed, "version.json")), Is.False);
        });

        [UnityTest]
        public IEnumerator CancellationDuringDownloadDoesNotLeavePartialVersion() => UniTask.ToCoroutine(async () =>
        {
            Publish(1, "first");
            _server.ResponseDelayMilliseconds = 2000;
            using (var cts = new CancellationTokenSource())
            {
                var request = _patch.RunAsync(_server.RootUrl, ct: cts.Token).AsTask();
                await LifecycleTests.Until(() => _server.Count("/version.json") == 1);
                cts.Cancel();
                await LifecycleTests.ExpectCancelled(request);
            }
            Assert.That(File.Exists(Path.Combine(_installed, "version.json")), Is.False);
            Assert.That(Directory.GetFiles(_installed, "*.part", SearchOption.AllDirectories), Is.Empty);
        });

        private string Publish(int version, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = FileIntegrity.Hash(bytes);
            Directory.CreateDirectory(Path.Combine(_remote, "bundles"));
            Directory.CreateDirectory(Path.Combine(_remote, "manifests"));
            File.WriteAllBytes(Path.Combine(_remote, "bundles", hash + ".bundle"), bytes);
            var file = new AssetManifestFile { version = version, bundles = new[] { new ManifestBundleData { name = "content", hash = hash, size = bytes.Length } },
                assets = new[] { new ManifestAssetData { address = "Content", assetPath = "Assets/Content.prefab" } } };
            var manifestBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(file));
            var manifestHash = FileIntegrity.Hash(manifestBytes);
            File.WriteAllBytes(Path.Combine(_remote, "manifests", manifestHash + ".json"), manifestBytes);
            var descriptor = new VersionFile { version = version, platform = "Windows", manifestHash = manifestHash, manifestSize = manifestBytes.Length };
            File.WriteAllText(Path.Combine(_remote, "version.json"), descriptor.ToJson(), new UTF8Encoding(false));
            return hash;
        }
    }
}
