using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.UI.Tests
{
    public sealed class UINavigationTests : UIRuntimeTestBase
    {
        [UnityTest] public IEnumerator ABCBUsesOneBAndPreservesEveryReturn() => UniTask.ToCoroutine(async () =>
        {
            StartUI();
            var a = UI.Open<A>(); await a.Opened;
            var b1 = UI.Open<B>(); var b = await b1.Opened;
            var c = UI.Open<C>(); await c.Opened;
            var b2 = UI.Open<B>(); Assert.That(await b2.Opened, Is.SameAs(b));
            Assert.That(UI.GetHistory(LayerType.NormalLayer).Select(h => h.Id), Is.EqualTo(new[] { a.Id, b1.Id, c.Id, b2.Id }));
            Assert.That(UI.InstanceCount, Is.EqualTo(3));
            Assert.That(Provider.Loads["B"], Is.EqualTo(1));
            Assert.That(b.AwakeCount, Is.EqualTo(1)); Assert.That(b.StartCount, Is.EqualTo(1));
            await UI.PopAsync(LayerType.NormalLayer);
            Assert.That(b2.IsClosed, Is.True); Assert.That((await c.Opened).IsVisible, Is.True); Assert.That(b.IsReleased, Is.False);
            await UI.PopAsync(LayerType.NormalLayer);
            Assert.That(b.IsVisible, Is.True); Assert.That(b.ActiveNavigation, Is.SameAs(b1));
            await UI.PopAsync(LayerType.NormalLayer);
            Assert.That(b.IsReleased, Is.True); Assert.That((await a.Opened).IsVisible, Is.True);
            Assert.That(b.DestroyCount, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator RepeatedVisitsKeepArgumentsAndSeparatePageState() => UniTask.ToCoroutine(async () =>
        {
            StartUI();
            var firstArgs = new object[] { "first" };
            var first = UI.Open<B>(firstArgs); var view = await first.Opened;
            firstArgs[0] = "mutated";
            view.Value = "first edit";
            await UI.OpenAsync<C>();
            var second = UI.Open<B>("second"); await second.Opened;
            Assert.That(view.Arguments[0], Is.EqualTo("second")); Assert.That(view.Value, Is.Null);
            view.Value = "second edit";
            await UI.PopAsync(LayerType.NormalLayer); await UI.PopAsync(LayerType.NormalLayer);
            Assert.That(view.ActiveNavigation, Is.SameAs(first));
            Assert.That(view.Arguments[0], Is.EqualTo("first"));
            Assert.That(view.Value, Is.EqualTo("first edit"));
        });

        [UnityTest] public IEnumerator ConcurrentBRequestsShareLoadAndOneCancellationCannotStealIt() => UniTask.ToCoroutine(async () =>
        {
            Provider.Blocked.Add("B"); StartUI();
            using (var cancellation = new CancellationTokenSource())
            {
                var first = UI.Open<B>(null, cancellation.Token); var second = UI.Open<B>();
                cancellation.Cancel();
                await Cancelled(first.Opened);
                Assert.That(second.Opened.IsCompleted, Is.False);
                Provider.Complete("B"); var view = await second.Opened;
                Assert.That(Provider.Loads["B"], Is.EqualTo(1)); Assert.That(view.AwakeCount, Is.EqualTo(1));
            }
        });

        [UnityTest] public IEnumerator PreparationRunsInParallelAndCommitUsesInvocationOrder() => UniTask.ToCoroutine(async () =>
        {
            Provider.Blocked.Add("A"); Provider.Blocked.Add("B"); StartUI();
            var first = UI.Open<A>(); var second = UI.Open<B>();
            Assert.That(Provider.Loads.ContainsKey("A") && Provider.Loads.ContainsKey("B"), Is.True);
            Provider.Complete("B"); await UniTask.NextFrame();
            Assert.That(second.Opened.IsCompleted, Is.False);
            Provider.Complete("A"); await first.Opened; var b = await second.Opened;
            Assert.That(Probe.Events.IndexOf("A.Start"), Is.LessThan(Probe.Events.IndexOf("B.Start")));
            Assert.That(b.IsVisible, Is.True);
        });

        [UnityTest] public IEnumerator ClosePendingIsImmediateAndLateProviderCompletionIsReclaimed() => UniTask.ToCoroutine(async () =>
        {
            Provider.Blocked.Add("B"); StartUI();
            var a = await UI.OpenAsync<A>(); var pending = UI.Open<B>();
            pending.Close(); await pending.Closed; await Cancelled(pending.Opened);
            Assert.That(a.IsVisible, Is.True); Assert.That(UI.FindView<B>(), Is.Null);
            Provider.Complete("B");
            await WaitUntil(() => { Assets.Tick(); return Assets.Tracker.Capture().Assets.All(asset => asset.Address != "B"); });
        });

        [UnityTest] public IEnumerator FailedPreparationPreservesBackgroundAndLaterRequestsProceed() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var a = await UI.OpenAsync<A>();
            Provider.Failures.Add("B"); var bad = UI.Open<B>(); await Failed(bad.Opened);
            Assert.That(a.IsVisible, Is.True); Assert.That(bad.State, Is.EqualTo(UINavigationState.Failed));
            Provider.Failures.Remove("B"); Assets.Tick(); var good = await UI.OpenAsync<B>();
            Assert.That(good.IsVisible, Is.True); Assert.That(a.IsVisible, Is.False);
        });

        [UnityTest] public IEnumerator PopCancelsPendingAndConsecutivePopsRemoveDifferentVisits() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var a = UI.Open<A>(); await a.Opened;
            var b = UI.Open<B>(); await b.Opened;
            Provider.Blocked.Add("C"); var c = UI.Open<C>();
            var pop1 = UI.PopAsync(LayerType.NormalLayer); var pop2 = UI.PopAsync(LayerType.NormalLayer);
            await Task.WhenAll(pop1, pop2); await Cancelled(c.Opened);
            Assert.That(b.IsClosed, Is.True); Assert.That(a.IsClosed, Is.False); Assert.That((await a.Opened).IsVisible, Is.True);
        });

        [UnityTest] public IEnumerator ClosingMiddleVisitDoesNotCloseTheTop() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var a = UI.Open<A>(); await a.Opened;
            var b = UI.Open<B>(); var view = await b.Opened;
            var c = UI.Open<C>(); await c.Opened;
            await UI.CloseAsync(b); b.Close();
            Assert.That(view.IsReleased, Is.True); Assert.That((await c.Opened).IsVisible, Is.True);
            await UI.PopAsync(LayerType.NormalLayer); Assert.That((await a.Opened).IsVisible, Is.True);
        });

        [UnityTest] public IEnumerator FullscreenAndPopupVisibilityIsDerivedPerLayer() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var a = await UI.OpenAsync<A>(); var popup = await UI.OpenAsync<Popup>();
            Assert.That(a.IsVisible && popup.IsVisible, Is.True);
            var c = UI.Open<C>(); await c.Opened;
            Assert.That(a.IsVisible || popup.IsVisible, Is.False);
            var top = await UI.OpenAsync<Top>(); Assert.That((await c.Opened).IsVisible, Is.True);
            await UI.CloseAsync(c); Assert.That(a.IsVisible && popup.IsVisible && top.IsVisible, Is.True);
        });

        [UnityTest] public IEnumerator RepeatPopupChangesVisitWithoutCreatingAnotherInstance() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); await UI.OpenAsync<A>();
            var first = UI.Open<Popup>(); var view = await first.Opened;
            var second = UI.Open<Popup>(); Assert.That(await second.Opened, Is.SameAs(view));
            await UI.CloseAsync(second); Assert.That(view.ActiveNavigation, Is.SameAs(first)); Assert.That(view.IsVisible, Is.True);
        });

        [UnityTest] public IEnumerator LifecycleNavigationIsQueuedAndClosingSelfTargetsCurrentVisit() => UniTask.ToCoroutine(async () =>
        {
            UIHandle<B> b = null;
            Probe.EnableAction = view => { if (view is A) b = view.Manager.Open<B>(); };
            StartUI(); var a = await UI.OpenAsync<A>(); await WaitUntil(() => b != null && b.Opened.IsCompleted);
            var bv = await b.Opened; Assert.That(a.IsVisible, Is.False);
            Probe.EnableAction = null; bv.Exit(); await b.Closed;
            Assert.That(a.IsVisible, Is.True); Assert.That(a.EnableCount, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator ThrowingEnableRollsBackAndDoesNotPoisonNavigation() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var a = await UI.OpenAsync<A>();
            Probe.EnableAction = view => { if (view is B) throw new InvalidOperationException("Injected lifecycle failure"); };
            var bad = UI.Open<B>(); await Failed(bad.Opened);
            Assert.That(a.IsVisible, Is.True); Assert.That(UI.FindView<B>(), Is.Null);
            Probe.EnableAction = null; Assert.That((await UI.OpenAsync<C>()).IsVisible, Is.True);
        });

        [UnityTest] public IEnumerator DisposeInsideAwakeTerminatesHandlesAndPreservesExternalAssetSystem() => UniTask.ToCoroutine(async () =>
        {
            Probe.AwakeAction = view => view.Manager.Dispose(); StartUI();
            var handle = UI.Open<A>(); await handle.Closed; await Cancelled(handle.Opened);
            Assert.That(Assets.IsDisposed, Is.False); Assert.That(UI.IsDisposed, Is.True);
        });

        [UnityTest] public IEnumerator MoreThanTenVisitsDoNotOverflowLayerOrDuplicateInstances() => UniTask.ToCoroutine(async () =>
        {
            StartUI();
            for (var i = 0; i < 24; i++) await UI.OpenAsync<B>();
            Assert.That(UI.GetLayerCount(LayerType.NormalLayer), Is.EqualTo(24));
            Assert.That(UI.InstanceCount, Is.EqualTo(1)); Assert.That(Provider.Loads["B"], Is.EqualTo(1));
            for (var i = 0; i < 24; i++) await UI.PopAsync(LayerType.NormalLayer);
            Assert.That(UI.InstanceCount, Is.Zero);
        });
    }
}
