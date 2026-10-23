using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Content;
using Game.AssetSystem.Core;

namespace Game.AssetSystem.Patch
{
    public sealed class PatchActivationService
    {
        public ActiveContentContext ActiveContext { get; private set; }
        public int ActivationCount { get; private set; }

        public async Task<PatchActivationResult> ActivateAsync(
            PatchManifest manifest,
            string bundleRoot,
            string expectedPlatform,
            int liveHandles,
            bool safePoint,
            CancellationToken cancellationToken = default,
            Func<int> liveHandlesProvider = null,
            Func<bool> safePointProvider = null)
        {
            var before = ActiveContext == null
                ? null
                : ActiveContext.ContentVersion;

            var result = new PatchActivationResult
            {
                ActiveVersionBefore = before,
                ActiveVersionAfter = before
            };

            if (manifest == null ||
                manifest.Bundles == null ||
                manifest.Bundles.Length == 0 ||
                manifest.Assets == null ||
                manifest.Assets.Length == 0)
            {
                result.Status = PatchActivationStatus.InvalidManifest;
                result.Message = "Invalid manifest shape.";
                return result;
            }

            if (!Game.AssetSystem.Storage.PathSafety.IsSimpleFileName(
                manifest.ContentVersion))
            {
                result.Status = PatchActivationStatus.InvalidManifest;
                result.Message = "Invalid content version: " +
                                 (manifest.ContentVersion ?? "<null>");
                return result;
            }

            if (!string.Equals(
                manifest.PlatformKey,
                expectedPlatform,
                StringComparison.Ordinal))
            {
                result.Status = PatchActivationStatus.PlatformMismatch;
                result.Message = "Manifest platform " +
                                 manifest.PlatformKey +
                                 " != expected platform " +
                                 expectedPlatform;
                return result;
            }

            var validation = await Task.Run(
                () => ValidateFiles(
                    manifest,
                    bundleRoot,
                    cancellationToken));

            result.BundlesChecked = validation.BundlesChecked;

            if (validation.Canceled)
            {
                result.Status = PatchActivationStatus.Canceled;
                result.Message = "Canceled during validation.";
                return result;
            }

            if (validation.ErrorStatus !=
                PatchActivationStatus.Succeeded)
            {
                result.Status = validation.ErrorStatus;
                result.Message = validation.Message;
                return result;
            }

            if (ActiveContext != null &&
                string.Equals(
                    ActiveContext.ContentVersion,
                    manifest.ContentVersion,
                    StringComparison.Ordinal))
            {
                result.Status = PatchActivationStatus.AlreadyActive;
                result.Message = "Version " +
                                 manifest.ContentVersion +
                                 " is already active and valid.";
                return result;
            }

            var handlesNow = liveHandlesProvider != null
                ? liveHandlesProvider()
                : liveHandles;

            if (handlesNow > 0)
            {
                result.Status = PatchActivationStatus.RejectedActiveHandles;
                result.Message = "Cannot activate while " +
                                 handlesNow +
                                 " live handle(s) exist.";
                return result;
            }

            var safeNow = safePointProvider != null
                ? safePointProvider()
                : safePoint;

            if (!safeNow)
            {
                result.Status = PatchActivationStatus.RejectedUnsafePoint;
                result.Message = "Cannot activate outside a safe point.";
                return result;
            }

            if (!MainThreadPump.IsMainThread)
            {
                var tcs = new TaskCompletionSource<PatchActivationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                MainThreadPump.PostTerminal(
                    () => tcs.TrySetResult(
                        Swap(
                            manifest,
                            bundleRoot,
                            result,
                            liveHandles,
                            safePoint,
                            liveHandlesProvider,
                            safePointProvider)));

                return await tcs.Task;
            }

            return Swap(
                manifest,
                bundleRoot,
                result,
                liveHandles,
                safePoint,
                liveHandlesProvider,
                safePointProvider);
        }

        private PatchActivationResult Swap(
            PatchManifest manifest,
            string bundleRoot,
            PatchActivationResult result,
            int liveHandles,
            bool safePoint,
            Func<int> liveHandlesProvider,
            Func<bool> safePointProvider)
        {
            MainThreadPump.EnsureMainThread(nameof(Swap));

            // Re-check with authoritative state inside the swap itself. The
            // checks above may have happened before a Post, and the main thread
            // could have handed out a new handle in between.
            var handlesNow = liveHandlesProvider != null
                ? liveHandlesProvider()
                : liveHandles;

            if (handlesNow > 0)
            {
                result.Status = PatchActivationStatus.RejectedActiveHandles;
                result.Message = "Cannot activate while " +
                                 handlesNow +
                                 " live handle(s) exist.";
                return result;
            }

            var safeNow = safePointProvider != null
                ? safePointProvider()
                : safePoint;

            if (!safeNow)
            {
                result.Status = PatchActivationStatus.RejectedUnsafePoint;
                result.Message = "Cannot activate outside a safe point.";
                return result;
            }

            var bundles = new List<RuntimeBundle>();
            var groupToId =
                new Dictionary<string, BundleId>(StringComparer.Ordinal);

            var ids = new BundleId[manifest.Bundles.Length];
            var bundlePaths = new string[manifest.Bundles.Length];

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var b = manifest.Bundles[i];

                if (groupToId.ContainsKey(b.BundleGroupName))
                {
                    result.Status = PatchActivationStatus.InvalidManifest;
                    result.Message = "Duplicate bundle group: " +
                                     b.BundleGroupName;
                    return result;
                }

                if (!Game.AssetSystem.Storage.PathSafety.TryResolveChild(
                    bundleRoot,
                    b.FileName,
                    out var bundlePath,
                    out var pathError))
                {
                    result.Status = PatchActivationStatus.InvalidManifest;
                    result.Message = "Invalid bundle file name '" +
                                     b.FileName + "': " + pathError;
                    return result;
                }

                var id = new BundleId(i);
                groupToId[b.BundleGroupName] = id;
                ids[i] = id;
                bundlePaths[i] = bundlePath;
            }

            // Dependency group names are data: resolve them against the groups
            // this manifest actually declares, and refuse self references and
            // cycles here rather than deadlocking the runtime later.
            var dependencies = new BundleId[manifest.Bundles.Length][];

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var declared = manifest.Bundles[i].Dependencies;

                if (declared == null || declared.Length == 0)
                {
                    dependencies[i] = Array.Empty<BundleId>();
                    continue;
                }

                var resolved = new List<BundleId>(declared.Length);

                for (var d = 0; d < declared.Length; d++)
                {
                    var name = declared[d];

                    if (!groupToId.TryGetValue(name ?? string.Empty, out var dependencyId))
                    {
                        result.Status = PatchActivationStatus.InvalidManifest;
                        result.Message = "Unknown dependency group '" +
                                         name + "' declared by bundle " +
                                         manifest.Bundles[i].BundleGroupName;
                        return result;
                    }

                    if (dependencyId == ids[i])
                    {
                        result.Status = PatchActivationStatus.InvalidManifest;
                        result.Message = "Bundle " +
                                         manifest.Bundles[i].BundleGroupName +
                                         " depends on itself.";
                        return result;
                    }

                    if (!resolved.Contains(dependencyId))
                    {
                        resolved.Add(dependencyId);
                    }
                }

                dependencies[i] = resolved.ToArray();
            }

            if (HasDependencyCycle(ids, dependencies))
            {
                result.Status = PatchActivationStatus.InvalidManifest;
                result.Message = "Circular bundle dependency detected.";
                return result;
            }

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var b = manifest.Bundles[i];

                bundles.Add(new RuntimeBundle(
                    ids[i],
                    b.BundleGroupName,
                    b.FileName,
                    bundlePaths[i],
                    b.SizeBytes,
                    b.Sha256Hex,
                    dependencies[i]));
            }

            var assets = new List<RuntimeAsset>();

            for (var i = 0; i < manifest.Assets.Length; i++)
            {
                var a = manifest.Assets[i];

                if (!groupToId.TryGetValue(
                    a.BundleGroupName,
                    out var bundleId))
                {
                    result.Status = PatchActivationStatus.InvalidManifest;
                    result.Message = "Unknown bundle group " +
                                     a.BundleGroupName +
                                     " for asset " + a.AssetKey;
                    return result;
                }

                assets.Add(new RuntimeAsset(
                    new AssetKey(a.AssetKey),
                    bundleId,
                    a.InternalPath));
            }

            ActiveContext = new ActiveContentContext(
                manifest.ContentVersion,
                manifest.PlatformKey,
                bundleRoot,
                assets,
                bundles);

            ActivationCount++;

            result.Status = PatchActivationStatus.Succeeded;
            result.ActiveVersionAfter = manifest.ContentVersion;
            result.Message = "Activated " +
                             manifest.ContentVersion +
                             " (" + bundles.Count + " bundles, " +
                             assets.Count + " assets).";

            Debug.Log(
                "[Activation] " +
                (result.ActiveVersionBefore ?? "<none>") +
                " -> " +
                result.ActiveVersionAfter);

            return result;
        }

        /// <summary>
        /// Iterative DFS colouring. Bundle ids are positional (id == index in
        /// the manifest), so the adjacency list is index based.
        /// </summary>
        private static bool HasDependencyCycle(
            BundleId[] ids,
            BundleId[][] dependencies)
        {
            var state = new int[ids.Length];
            var stack = new Stack<int>();

            for (var root = 0; root < ids.Length; root++)
            {
                if (state[root] != 0)
                {
                    continue;
                }

                stack.Push(root);

                while (stack.Count > 0)
                {
                    var index = stack.Peek();

                    if (state[index] == 0)
                    {
                        state[index] = 1;
                    }

                    var advanced = false;
                    var direct = dependencies[index];

                    for (var d = 0; d < direct.Length; d++)
                    {
                        var next = direct[d].Value;

                        if (next < 0 || next >= state.Length)
                        {
                            continue;
                        }

                        if (state[next] == 1)
                        {
                            return true;
                        }

                        if (state[next] == 0)
                        {
                            stack.Push(next);
                            advanced = true;
                            break;
                        }
                    }

                    if (!advanced)
                    {
                        state[index] = 2;
                        stack.Pop();
                    }
                }
            }

            return false;
        }

        private struct ValidationReport
        {
            public int BundlesChecked;
            public bool Canceled;
            public PatchActivationStatus ErrorStatus;
            public string Message;
        }

        private ValidationReport ValidateFiles(
            PatchManifest manifest,
            string bundleRoot,
            CancellationToken cancellationToken)
        {
            var report = new ValidationReport
            {
                ErrorStatus = PatchActivationStatus.Succeeded
            };

            if (string.IsNullOrEmpty(bundleRoot) ||
                !Directory.Exists(bundleRoot))
            {
                report.ErrorStatus = PatchActivationStatus.BundleMissing;
                report.Message = "Bundle root missing: " + bundleRoot;
                return report;
            }

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    report.Canceled = true;
                    return report;
                }

                var b = manifest.Bundles[i];
                report.BundlesChecked++;

                if (!Game.AssetSystem.Storage.PathSafety.TryResolveChild(
                    bundleRoot,
                    b.FileName,
                    out var path,
                    out var pathError))
                {
                    report.ErrorStatus = PatchActivationStatus.InvalidManifest;
                    report.Message = "Invalid bundle file name '" +
                                     b.FileName + "': " + pathError;
                    return report;
                }

                if (!File.Exists(path))
                {
                    report.ErrorStatus = PatchActivationStatus.BundleMissing;
                    report.Message = "Missing bundle: " + path;
                    return report;
                }

                var size = new FileInfo(path).Length;

                if (size != b.SizeBytes)
                {
                    report.ErrorStatus = PatchActivationStatus.BundleSizeMismatch;
                    report.Message = "Size mismatch for " +
                                     b.FileName +
                                     ": expected " + b.SizeBytes +
                                     ", actual " + size;
                    return report;
                }

                string hash;

                try
                {
                    using (var sha = SHA256.Create())
                    using (var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        64 * 1024,
                        FileOptions.SequentialScan))
                    {
                        var bytes = sha.ComputeHash(stream);
                        var sb = new StringBuilder(bytes.Length * 2);

                        for (var k = 0; k < bytes.Length; k++)
                        {
                            sb.Append(bytes[k].ToString("x2"));
                        }

                        hash = sb.ToString();
                    }
                }
                catch (Exception exception)
                {
                    report.ErrorStatus =
                        PatchActivationStatus.InvalidManifest;
                    report.Message = "Failed to hash " +
                                     b.FileName + ": " +
                                     exception.Message;
                    return report;
                }

                if (!string.Equals(
                    hash,
                    b.Sha256Hex,
                    StringComparison.OrdinalIgnoreCase))
                {
                    report.ErrorStatus = PatchActivationStatus.HashMismatch;
                    report.Message = "Hash mismatch for " + b.FileName;
                    return report;
                }
            }

            return report;
        }
    }
}
