using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.AssetSystem.Bundles.L44;
using Game.AssetSystem.Infrastructure.L44;
using UnityEngine;

namespace Game.AssetSystem.Demo.L44
{
    public sealed class BundleRuntimeLessonBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing =
                UnityEngine.Object.FindFirstObjectByType<BundleRuntimeLessonBootstrap>();

            if (existing != null)
            {
                return;
            }

            var go = new GameObject("[BundleRuntimeLessonBootstrap]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<BundleRuntimeLessonBootstrap>();
        }

        private async void Start()
        {
            Debug.Log(
                "[Lesson44] start mainThreadId=" +
                UnityMainThreadPump.MainThreadId +
                " isMainThread=" +
                UnityMainThreadPump.IsMainThread);

            var audio = new BundleId(0);
            var fx = new BundleId(1);
            var shop = new BundleId(2);
            var broken = new BundleId(3);

            var catalog = new BundleCatalog(
                new[]
                {
                    new BundleInfo
                    {
                        Id = audio,
                        Name = "battle_audio",
                        DirectDependencies = Array.Empty<BundleId>()
                    },
                    new BundleInfo
                    {
                        Id = fx,
                        Name = "battle_fx",
                        DirectDependencies = new[] { audio }
                    },
                    new BundleInfo
                    {
                        Id = shop,
                        Name = "ui_shop",
                        DirectDependencies = new[] { fx }
                    },
                    new BundleInfo
                    {
                        Id = broken,
                        Name = "broken_bundle",
                        DirectDependencies = new[]
                        {
                            new BundleId(99)
                        }
                    }
                });

            var loader = new SimulatedBundleLoader(150);
            var runtime = new BundleRuntime(catalog, loader);

            await RunDependencyAndIdleScenario(
                runtime,
                loader,
                shop,
                fx,
                audio);

            await RunConcurrentCancelScenario(
                runtime,
                loader,
                fx);

            await RunMissingDependencyScenario(
                runtime,
                loader,
                broken);

            Debug.Log(
                "[Lesson44] Finished. " +
                "Experiment: remove dependency lease disposal in TryUnloadIdle " +
                "and observe dependency refs staying above zero.");
        }

        private static async Task RunDependencyAndIdleScenario(
            BundleRuntime runtime,
            SimulatedBundleLoader loader,
            BundleId shop,
            BundleId fx,
            BundleId audio)
        {
            Debug.Log("[Scenario1] acquire ui_shop twice");

            var task1 = runtime.AcquireAsync(
                shop,
                "UI.Shop.1");

            var task2 = runtime.AcquireAsync(
                shop,
                "UI.Shop.2");

            var result1 = await task1;
            var result2 = await task2;

            Debug.Log(
                "[Scenario1] results success1=" +
                result1.Success +
                " success2=" +
                result2.Success);

            LogSnapshot("Scenario1.Acquired", runtime);

            Debug.Log(
                "[Scenario1] loader started shop=" +
                loader.GetStartedCount(shop) +
                " fx=" +
                loader.GetStartedCount(fx) +
                " audio=" +
                loader.GetStartedCount(audio));

            result1.Lease.Dispose();
            result2.Lease.Dispose();

            LogSnapshot("Scenario1.Released", runtime);

            Debug.Log(
                "[Scenario1] unload idle shop -> " +
                runtime.TryUnloadIdle(shop));

            LogSnapshot("Scenario1.UnloadedShop", runtime);

            Debug.Log(
                "[Scenario1] unload idle fx -> " +
                runtime.TryUnloadIdle(fx));

            LogSnapshot("Scenario1.UnloadedFx", runtime);

            Debug.Log(
                "[Scenario1] unload idle audio -> " +
                runtime.TryUnloadIdle(audio));

            LogSnapshot("Scenario1.UnloadedAudio", runtime);

            Debug.Log(
                "[Scenario1] loader released shop=" +
                loader.GetReleasedCount(shop) +
                " fx=" +
                loader.GetReleasedCount(fx) +
                " audio=" +
                loader.GetReleasedCount(audio));

            await Task.Delay(50);
        }

        private static async Task RunConcurrentCancelScenario(
            BundleRuntime runtime,
            SimulatedBundleLoader loader,
            BundleId fx)
        {
            var startedBefore = loader.GetStartedCount(fx);

            Debug.Log(
                "[Scenario2] acquire battle_fx twice, cancel one waiter");

            var task1 = runtime.AcquireAsync(
                fx,
                "Battle.Effect");

            var task2 = runtime.AcquireAsync(
                fx,
                "Battle.Canceled");

            await Task.Delay(50);

            task2.Cancel();

            var result1 = await task1;
            var result2 = await task2;

            Debug.Log(
                "[Scenario2] result1 success=" +
                result1.Success +
                " result2 success=" +
                result2.Success +
                " error2=" +
                result2.Error);

            LogSnapshot("Scenario2.Acquired", runtime);

            Debug.Log(
                "[Scenario2] loader started delta=" +
                (loader.GetStartedCount(fx) - startedBefore));

            if (result1.Success && result1.Lease != null)
            {
                result1.Lease.Dispose();
            }

            if (result2.Success && result2.Lease != null)
            {
                result2.Lease.Dispose();
            }

            LogSnapshot("Scenario2.Released", runtime);

            await Task.Delay(50);
        }

        private static async Task RunMissingDependencyScenario(
            BundleRuntime runtime,
            SimulatedBundleLoader loader,
            BundleId broken)
        {
            Debug.Log(
                "[Scenario3] acquire broken_bundle with missing dependency");

            var result = await runtime.AcquireAsync(
                broken,
                "Broken.Owner");

            Debug.Log(
                "[Scenario3] success=" +
                result.Success +
                " error=" +
                result.Error);

            LogSnapshot("Scenario3.AfterFailure", runtime);

            await Task.Delay(50);
        }

        private static void LogSnapshot(
            string label,
            BundleRuntime runtime)
        {
            var snapshot = runtime.CaptureSnapshot();

            Debug.Log(
                "[Snapshot][" + label + "] totalUserRefs=" +
                snapshot.TotalUserRefs +
                " loadedPayloads=" +
                snapshot.LoadedPayloadCount +
                " loading=" +
                snapshot.LoadingCount +
                " idle=" +
                snapshot.IdleCount);

            for (var i = 0; i < snapshot.Entries.Count; i++)
            {
                var entry = snapshot.Entries[i];

                Debug.Log(
                    "[Snapshot][" + label + "] " +
                    entry.Name +
                    " state=" +
                    entry.State +
                    " userRefs=" +
                    entry.UserRefCount +
                    " waiting=" +
                    entry.WaitingCount +
                    " hasPayload=" +
                    entry.HasPayload +
                    " lastError=" +
                    entry.LastError);
            }
        }
    }
}
