using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameFramework.UI.Tests
{
    public sealed class UIOwnershipTests : UIRuntimeTestBase
    {
        [UnityTest] public IEnumerator DynamicChildrenAreParentOwnedAndSelectionSurvivesCovering() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var parent = await UI.OpenAsync<A>();
            var first = await parent.OpenOneChildUIAsync<Child>();
            var second = await parent.OpenOneChildUIAsync<Child2>();
            Assert.That(first.IsVisible, Is.False); Assert.That(second.IsVisible, Is.True);
            var cover = UI.Open<B>(); await cover.Opened;
            Assert.That(second.IsVisible, Is.False);
            await UI.CloseAsync(cover); Assert.That(second.IsVisible, Is.True); Assert.That(first.IsVisible, Is.False);
            await parent.CloseChildAsync(typeof(Child2));
            Assert.That(second.IsReleased, Is.False);
            Assert.That(await parent.OpenChildAsync<Child2>(), Is.SameAs(second));
            await parent.RemoveChildAsync<Child2>(); Assert.That(second.IsReleased, Is.True);
        });

        [UnityTest] public IEnumerator StaticChildBorrowingPreservesHierarchyAndNeverDestroysNativeNode() => UniTask.ToCoroutine(async () =>
        {
            Config.Views[0].ChildList = new[] { Binding<Child>("StaticNode") }; StartUI();
            var parent = await UI.OpenAsync<A>(); var node = parent.transform.Find("StaticNode");
            var child = await parent.OpenChildAsync<Child>(); Assert.That(child.transform, Is.SameAs(node));
            Assert.That(node.parent, Is.SameAs(parent.transform));
            Assert.That(Provider.Loads.ContainsKey("Child"), Is.False);
            await parent.RemoveChildAsync<Child>(); await UniTask.NextFrame();
            Assert.That(node != null, Is.True);
            var reopened = await parent.OpenChildAsync<Child>();
            Assert.That(reopened, Is.Not.SameAs(child)); Assert.That(reopened.gameObject, Is.SameAs(node.gameObject));
            Assert.That(node.GetComponent<CanvasGroup>().alpha, Is.GreaterThan(0));
        });

        [UnityTest] public IEnumerator FailedChildSelectionKeepsPreviousChildVisible() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var parent = await UI.OpenAsync<A>(); var first = await parent.OpenOneChildUIAsync<Child>();
            Provider.Failures.Add("Child2"); await Failed(parent.OpenOneChildUIAsync<Child2>());
            Assert.That(first.IsVisible, Is.True);
        });

        [UnityTest] public IEnumerator ClosingPendingChildCancelsItsWaitAndLateCompletionCannotShowIt() => UniTask.ToCoroutine(async () =>
        {
            Provider.Blocked.Add("Child"); StartUI(); var parent = await UI.OpenAsync<A>();
            var request = parent.OpenChildAsync<Child>(); await parent.CloseChildAsync(typeof(Child));
            await Cancelled(request); Provider.Complete("Child"); await UniTask.NextFrame();
            Assert.That(parent.FindChild<Child>(), Is.Null);
        });

        [UnityTest] public IEnumerator HidingPausesRenderingInputAndVisibleWorkWithoutDeactivatingObject() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var a = await UI.OpenAsync<A>();
            var token = a.VisibilityToken;
            var canvas = a.gameObject.GetComponent<Canvas>(); var raycaster = a.gameObject.GetComponent<GraphicRaycaster>();
            var b = UI.Open<B>(); await b.Opened;
            Assert.That(a.gameObject.activeSelf, Is.True); Assert.That(canvas.enabled, Is.False); Assert.That(raycaster.enabled, Is.False);
            Assert.That(a.gameObject.GetComponent<CanvasGroup>().blocksRaycasts, Is.False); Assert.That(token.IsCancellationRequested, Is.True);
            await UI.CloseAsync(b);
            Assert.That(canvas.enabled && raycaster.enabled, Is.True); Assert.That(a.VisibilityToken.IsCancellationRequested, Is.False);
        });

        [UnityTest] public IEnumerator InitiallyDisabledCanvasAndRaycasterStayDisabledAfterRestore() => UniTask.ToCoroutine(async () =>
        {
            Prefab.GetComponent<GraphicRaycaster>().enabled = false; StartUI(); var a = await UI.OpenAsync<A>();
            var b = UI.Open<B>(); await b.Opened; await UI.CloseAsync(b);
            Assert.That(a.gameObject.GetComponent<GraphicRaycaster>().enabled, Is.False);
        });

        [UnityTest] public IEnumerator TemplateAndAddressItemsFollowOwnerAndReleaseAllLeases() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var handle = UI.Open<A>(); var parent = await handle.Opened;
            var template = parent.transform.Find("Template").gameObject;
            var local = parent.CreateItem<Item>(template);
            var loaded = await parent.CreateItemAsync<Item>("Item");
            Assert.That(local.IsVisible && loaded.IsVisible, Is.True);
            handle.Close(); await handle.Closed;
            Assert.That(local.IsReleased && loaded.IsReleased, Is.True);
            await WaitUntil(() => { Assets.Tick(); return Assets.IsIdle; });
            Assert.That(Assets.Tracker.Capture().Assets, Is.Empty);
        });

        [UnityTest] public IEnumerator SortingAccountsForNestedCanvasesChildrenAndOtherLayers() => UniTask.ToCoroutine(async () =>
        {
            StartUI(); var parent = await UI.OpenAsync<A>(); var child = await parent.OpenChildAsync<Child>();
            var popup = await UI.OpenAsync<Popup>(); var top = await UI.OpenAsync<Top>();
            var parentOrders = parent.gameObject.GetComponentsInChildren<Canvas>(true).Select(c => c.sortingOrder).ToArray();
            var childOrders = child.gameObject.GetComponentsInChildren<Canvas>(true).Select(c => c.sortingOrder).ToArray();
            Assert.That(parentOrders.Max(), Is.LessThan(childOrders.Min()));
            Assert.That(childOrders.Max(), Is.LessThan(popup.SortingOrder));
            Assert.That(popup.SortingOrder, Is.LessThan(top.SortingOrder));
        });

        [UnityTest] public IEnumerator ManagerDisposeReleasesOnlyUIAndLeavesApplicationScopeAlive() => UniTask.ToCoroutine(async () =>
        {
            var external = Assets.CreateScope("Application"); var kept = await external.LoadAsync<GameObject>("Item");
            StartUI(); await UI.OpenAsync<A>(); await UI.OpenAsync<B>(); UI.Dispose();
            await UniTask.NextFrame(); Assets.Tick();
            Assert.That(Assets.IsDisposed, Is.False); Assert.That(kept != null, Is.True);
            Assert.That(Assets.Tracker.Capture().Assets.Single().Address, Is.EqualTo("Item"));
            external.Dispose();
        });
    }
}
