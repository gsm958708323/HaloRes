using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameFramework.UI.Tests
{
    public sealed class UIBoundaryTests : UIRuntimeTestBase
    {
        [UnityTest] public IEnumerator LastCloseDoesNotCaptureStateAndThrowingCaptureCannotTrapRecovery() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var a = UI.Open<A>(); await a.Opened;
            Probe.CaptureAction = _ => throw new InvalidOperationException("Injected snapshot failure");
            await UI.CloseAsync(a); Assert.That(UI.InstanceCount, Is.Zero);
            var b = UI.Open<B>(); await b.Opened;
            var c = UI.Open<C>(); var cv = await c.Opened;
            Assert.That(b.IsClosed, Is.True); Assert.That(cv.IsVisible, Is.True);
            Probe.CaptureAction = null;
        });

        [UnityTest] public IEnumerator ChildEnableFailureKeepsPriorSelectionAndRetryWorks() => UniTask.ToCoroutine(async () =>
        {
            Config.Views[0].ChildList = new[] { Binding<Child>(), Binding<ThrowingChild>() }; StartUI();
            var a = await UI.OpenAsync<A>(); var prior = await a.OpenOneChildUIAsync<Child>();
            ThrowingChild.FailEnable = true; await Failed(a.OpenOneChildUIAsync<ThrowingChild>());
            Assert.That(prior.IsVisible, Is.True); Assert.That(a.FindChild<ThrowingChild>(), Is.Null);
            ThrowingChild.FailEnable = false;
            Assert.That((await a.OpenOneChildUIAsync<ThrowingChild>()).IsVisible, Is.True);
            Assert.That(prior.IsVisible, Is.False);
        });

        [UnityTest] public IEnumerator SharedChildLoadSurvivesOneCancelledRequest() => UniTask.ToCoroutine(async () =>
        {
            Provider.Blocked.Add("Child"); StartUI(); var a = await UI.OpenAsync<A>();
            using (var cancel = new CancellationTokenSource())
            {
                var first = a.OpenChildAsync<Child>(ct: cancel.Token); var second = a.OpenChildAsync<Child>();
                cancel.Cancel(); await Cancelled(first);
                Provider.Complete("Child"); var child = await second;
                Assert.That(child.IsVisible, Is.True); Assert.That(Provider.Loads["Child"], Is.EqualTo(1));
            }
        });

        [UnityTest] public IEnumerator ParentCloseCancelsPendingChildAndItem() => UniTask.ToCoroutine(async () =>
        {
            Provider.Blocked.Add("Child"); Provider.Blocked.Add("Item"); StartUI();
            var handle = UI.Open<A>(); var a = await handle.Opened;
            var child = a.OpenChildAsync<Child>(); var item = a.CreateItemAsync<Item>("Item");
            await UI.CloseAsync(handle); await Cancelled(child); await Cancelled(item);
            Provider.CompleteAll();
            await WaitUntil(() => { Assets.Tick(); return Assets.IsIdle; });
        });

        [UnityTest] public IEnumerator NavigationQueuedFromEnableTerminatesDuringDispose() => UniTask.ToCoroutine(async () =>
        {
            UIHandle<B> queued = null;
            Probe.EnableAction = view => { if (view is A) { queued = view.Manager.Open<B>(); view.Manager.Dispose(); } };
            StartUI(); var first = UI.Open<A>(); await first.Closed;
            await Cancelled(first.Opened); await Cancelled(queued.Opened); await queued.Closed;
            Assert.That(UI.InstanceCount, Is.Zero);
        });

        [UnityTest] public IEnumerator WorkerCancellationIsMarshalledToMainThread() => UniTask.ToCoroutine(async () =>
        {
            Provider.Blocked.Add("A"); StartUI();
            using (var cancel = new CancellationTokenSource())
            {
                var handle = UI.Open<A>(null, cancel.Token);
                await Task.Run(() => cancel.Cancel()); await handle.Closed; await Cancelled(handle.Opened);
                Assert.That(UI.InstanceCount, Is.Zero);
            }
        });

        [UnityTest] public IEnumerator HiddenAnimatorPausesAndRestoresOriginalFlag() => UniTask.ToCoroutine(async () =>
        {
            Prefab.AddComponent<Animator>(); StartUI(); var a = await UI.OpenAsync<A>();
            var animator = a.gameObject.GetComponent<Animator>(); Assert.That(animator.enabled, Is.True);
            var b = UI.Open<B>(); await b.Opened; Assert.That(animator.enabled, Is.False);
            await UI.CloseAsync(b); Assert.That(animator.enabled, Is.True);
        });

        [UnityTest] public IEnumerator LifecycleCleanupFailureStillReleasesAllOtherViewsAndAssets() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); await UI.OpenAsync<A>(); await UI.OpenAsync<B>();
            Probe.DisableAction = view => { if (view is B) throw new InvalidOperationException("Injected cleanup failure"); };
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Injected cleanup failure"));
            UI.Dispose();
            await WaitUntil(() => { Assets.Tick(); return Assets.IsIdle; });
            Assert.That(UI.InstanceCount, Is.Zero); Assert.That(Assets.IsDisposed, Is.False);
            Probe.DisableAction = null;
        });
    }
}
