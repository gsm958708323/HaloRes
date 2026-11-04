using System;
using System.Linq;
using GameFramework.UI.Samples;
using NUnit.Framework;

namespace GameFramework.UI.Tests
{
    public sealed class UINavigationModelTests
    {
        private static UIViewDefinition Definition<T>(bool popup = false) => new UIViewDefinition(typeof(T),
            new UIConfig { PrefabAddress = "UI/" + typeof(T).Name, LayerType = LayerType.NormalLayer, IsPopup = popup },
            Array.Empty<UIChildDefinition>());

        [Test]
        public void RepeatedTypeUsesOneVisibleVisitAndBackRestoresPreviousVisit()
        {
            var model = new UINavigationModel();
            var a = model.Add(1, Definition<FullView1>(), null);
            var b1 = model.Add(2, Definition<FullView2>(), new object[] { "first" });
            var c = model.Add(3, Definition<FullView2_Top>(), null);
            var b2 = model.Add(4, Definition<FullView2>(), new object[] { "second" });
            foreach (var type in new[] { typeof(FullView1), typeof(FullView2), typeof(FullView2_Top) }) model.MarkReady(type);

            Assert.That(model.CommitReadyInOrder().Select(entry => entry.Id), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
            Assert.That(model.CreateSnapshot().VisibleViews.Select(view => view.ActiveEntryId), Is.EqualTo(new long[] { 4 }));
            Assert.That(model.Pop(LayerType.NormalLayer).Id, Is.EqualTo(b2.Id));
            Assert.That(model.CreateSnapshot().VisibleViews.Select(view => view.ActiveEntryId), Is.EqualTo(new long[] { 3 }));
            Assert.That(model.Pop(LayerType.NormalLayer).Id, Is.EqualTo(c.Id));

            var restored = model.CreateSnapshot();
            Assert.That(restored.VisibleViews.Select(view => view.ActiveEntryId), Is.EqualTo(new long[] { b1.Id }));
            Assert.That(restored.TryGet(typeof(FullView2), out var state), Is.True);
            Assert.That(state.Arguments.Single(), Is.EqualTo("first"));
            Assert.That(model.History(LayerType.NormalLayer), Is.EqualTo(new long[] { a.Id, b1.Id }));
        }

        [Test]
        public void PendingEntriesCommitInInvocationOrderAndCloseRemovesExactEntry()
        {
            var model = new UINavigationModel();
            model.Add(1, Definition<FullView1>(), null);
            model.Add(2, Definition<FullView2>(), null);
            model.Add(3, Definition<FullView2_Top>(), null);
            model.MarkReady(typeof(FullView2_Top));
            Assert.That(model.CommitReadyInOrder(), Is.Empty);

            model.MarkReady(typeof(FullView1));
            Assert.That(model.CommitReadyInOrder().Select(entry => entry.Id), Is.EqualTo(new long[] { 1 }));
            Assert.That(model.Close(2).Id, Is.EqualTo(2));
            model.MarkReady(typeof(FullView2));
            Assert.That(model.CommitReadyInOrder().Select(entry => entry.Id), Is.EqualTo(new long[] { 3 }));
        }
    }
}
