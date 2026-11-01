using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GameUI.Components
{
    [TrackClipType(typeof(CanvasGroupAlphaClip))]
    [TrackBindingType(typeof(CanvasGroup))]
    public sealed class CanvasGroupAlphaTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject owner, int inputCount)
        { return ScriptPlayable<CanvasGroupAlphaMixer>.Create(graph, inputCount); }
    }

    public sealed class CanvasGroupAlphaBehaviour : PlayableBehaviour
    {
        [Range(0f, 1f)] public float startAlpha = 1f;
        [Range(0f, 1f)] public float endAlpha = 1f;
    }

    public sealed class CanvasGroupAlphaMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var group = playerData as CanvasGroup;
            if (group == null) return;
            var value = 0f;
            var weight = 0f;
            for (var i = 0; i < playable.GetInputCount(); i++)
            {
                var input = (ScriptPlayable<CanvasGroupAlphaBehaviour>)playable.GetInput(i);
                var inputWeight = playable.GetInputWeight(i);
                var duration = input.GetDuration();
                var progress = duration > 0 ? Mathf.Clamp01((float)(input.GetTime() / duration)) : 1f;
                var behaviour = input.GetBehaviour();
                value += Mathf.Lerp(behaviour.startAlpha, behaviour.endAlpha, progress) * inputWeight;
                weight += inputWeight;
            }
            if (weight > 0f) group.alpha = value / weight;
        }
    }
}
