using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
namespace GameUI.Components
{
    [System.Serializable]
    public sealed class CanvasGroupAlphaClip : PlayableAsset, ITimelineClipAsset
    {
        public CanvasGroupAlphaBehaviour template = new CanvasGroupAlphaBehaviour();
        public ClipCaps clipCaps { get { return ClipCaps.Blending; } }
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        { return ScriptPlayable<CanvasGroupAlphaBehaviour>.Create(graph, template); }
    }
}
