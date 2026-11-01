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

    [System.Serializable]
    public sealed class CanvasGroupAlphaClip : PlayableAsset, ITimelineClipAsset
    {
        public CanvasGroupAlphaBehaviour template = new CanvasGroupAlphaBehaviour();
        public ClipCaps clipCaps { get { return ClipCaps.Blending; } }
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        { return ScriptPlayable<CanvasGroupAlphaBehaviour>.Create(graph, template); }
    }

    public sealed class CanvasGroupAlphaBehaviour : PlayableBehaviour
    {
        [Range(0f, 1f)] public float alpha = 1f;
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
                value += input.GetBehaviour().alpha * inputWeight;
                weight += inputWeight;
            }
            if (weight > 0f) group.alpha = value / weight;
        }
    }
}
