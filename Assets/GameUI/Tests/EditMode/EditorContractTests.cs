using System.Collections.Generic;
using GameUI.Components;
using GameUI.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GameUI.Tests
{
    public sealed class EditorContractTests
    {
        private readonly List<Object> _objects=new List<Object>();
        [TearDown] public void Cleanup()
        {
            for(var i=_objects.Count-1;i>=0;i--)if(_objects[i]!=null)Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test] public void LintDetectsEnterAlphaMismatchWithoutMutatingThePrefab()
        {
            var root=new GameObject("View",typeof(RectTransform),typeof(CanvasGroup),typeof(UIViewTransitions),typeof(PlayableDirector));
            _objects.Add(root);
            var timeline=ScriptableObject.CreateInstance<TimelineAsset>();_objects.Add(timeline);
            var track=timeline.CreateTrack<CanvasGroupAlphaTrack>();_objects.Add(track);
            var clip=track.CreateClip<CanvasGroupAlphaClip>();_objects.Add(clip.asset);clip.duration=0.2;
            ((CanvasGroupAlphaClip)clip.asset).template.endAlpha=0.25f;
            var director=root.GetComponent<PlayableDirector>();director.playableAsset=timeline;
            var group=root.GetComponent<CanvasGroup>();group.alpha=1;
            director.SetGenericBinding(track,group);root.GetComponent<UIViewTransitions>().enter=director;
            var context=new UILintContext();new TransitionEndStateRule().Check(root,context);
            Assert.That(context.Messages.Count,Is.EqualTo(1));Assert.That(context.Messages[0],Does.Contain("final alpha"));
            Assert.That(group.alpha,Is.EqualTo(1));Assert.That(director.state,Is.EqualTo(PlayState.Paused));
            ((CanvasGroupAlphaClip)clip.asset).template.endAlpha=1;
            context=new UILintContext();new TransitionEndStateRule().Check(root,context);Assert.That(context.Messages,Is.Empty);
        }

        [Test] public void LintDetectsConflictingPanelLayoutsWithinAView()
        {
            var root=new GameObject("View",typeof(RectTransform),typeof(UIBindingData));_objects.Add(root);
            var first=new GameObject("First",typeof(RectTransform),typeof(UIBindingData));first.transform.SetParent(root.transform);
            var second=new GameObject("Second",typeof(RectTransform),typeof(UIBindingData));second.transform.SetParent(root.transform);
            first.GetComponent<UIBindingData>().layoutHash=1;second.GetComponent<UIBindingData>().layoutHash=2;
            var data=root.GetComponent<UIBindingData>();
            data.nodes.Add(new UIBindingNode{kind="Panel",typeName="SharedPanel",data=first.GetComponent<UIBindingData>()});
            data.nodes.Add(new UIBindingNode{kind="Panel",typeName="SharedPanel",data=second.GetComponent<UIBindingData>()});
            var context=new UILintContext();new SharedPanelLayoutRule().Check(root,context);
            Assert.That(context.Messages,Has.Some.Contains("Panel layout differs"));
        }
    }
}
