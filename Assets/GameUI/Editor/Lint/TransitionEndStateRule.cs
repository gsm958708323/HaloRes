using System;
using GameUI.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GameUI.Editor
{
    // Inspect curves without evaluating directors, so lint never emits signals or plays audio.
    public sealed class TransitionEndStateRule : IUIPrefabLintRule
    {
        public string Id => "transition-end-state";
        public UILintLevel Level => UILintLevel.Warning;

        public void Check(GameObject root, UILintContext context)
        {
            foreach (var transitions in root.GetComponentsInChildren<UIViewTransitions>(true))
            {
                var director = transitions.enter;
                if (director == null || !(director.playableAsset is TimelineAsset timeline)) continue;
                foreach (var track in timeline.GetOutputTracks()) CheckTrack(track, director, timeline.duration, context);
            }
        }

        private static void CheckTrack(TrackAsset track, PlayableDirector director, double end, UILintContext context)
        {
            if (track.muted) return;
            var binding = director.GetGenericBinding(track);
            TimelineClip last = null;
            foreach (var clip in track.GetClips())
                if (clip.start < end && (last == null || clip.end > last.end)) last = clip;

            if (track is CanvasGroupAlphaTrack && binding is CanvasGroup group && last?.asset is CanvasGroupAlphaClip alpha)
            {
                if (!Mathf.Approximately(group.alpha, alpha.template.endAlpha))
                    context.Report(group, "Enter Timeline final alpha differs from the static state.");
            }
            if (track is AnimationTrack animation)
            {
                var target = binding is Component component ? component.gameObject : binding as GameObject;
                if (target != null)
                {
                    if (last?.asset is AnimationPlayableAsset asset && asset.clip != null)
                        CheckAnimation(target, asset.clip, (float)Math.Min(asset.clip.length, last.ToLocalTime(Math.Min(end, last.end))), context);
                    else if (animation.infiniteClip != null) CheckAnimation(target, animation.infiniteClip, (float)end, context);
                }
            }
            foreach (var child in track.GetChildTracks()) CheckTrack(child, director, end, context);
        }

        private static void CheckAnimation(GameObject root, AnimationClip clip, float time, UILintContext context)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var target = FindTarget(root, binding);
                if (target == null) continue;
                using (var serialized = new SerializedObject(target))
                {
                    var property = serialized.FindProperty(binding.propertyName);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (property == null || curve == null) continue;
                    var value = curve.Evaluate(time);
                    var differs = property.propertyType == SerializedPropertyType.Float
                        ? Mathf.Abs(property.floatValue - value) > 0.001f
                        : property.propertyType == SerializedPropertyType.Boolean && property.boolValue != (value > 0.5f);
                    if (differs) context.Report(target, "Enter Timeline final " + binding.propertyName + " differs from the static state.");
                }
            }
        }

        private static UnityEngine.Object FindTarget(GameObject root, EditorCurveBinding binding)
        {
            var transform = string.IsNullOrEmpty(binding.path) ? root.transform : root.transform.Find(binding.path);
            if (transform == null) return null;
            return binding.type == typeof(GameObject) ? transform.gameObject : transform.GetComponent(binding.type);
        }
    }
}
