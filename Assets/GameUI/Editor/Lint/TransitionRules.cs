using GameUI.Components;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
namespace GameUI.Editor
{
    public sealed class TransitionConfigurationRule : IUIPrefabLintRule
    {
        public string Id => "transition-configuration";
        public UILintLevel Level => UILintLevel.Error;
        public void Check(GameObject root,UILintContext context)
        {
            foreach (var director in root.GetComponentsInChildren<PlayableDirector>(true))
            {
                if (director.playOnAwake) context.Report(director,"Play On Awake must be disabled.");
                if (director.timeUpdateMode != DirectorUpdateMode.UnscaledGameTime) context.Report(director,"Use UnscaledGameTime.");
            }
        }
    }
    public sealed class TransitionSignalRule : IUIPrefabLintRule
    {
        public string Id => "transition-signals";
        public UILintLevel Level => UILintLevel.Warning;
        public void Check(GameObject root,UILintContext context)
        {
            foreach (var director in root.GetComponentsInChildren<PlayableDirector>(true))
            {
                var timeline=director.playableAsset as TimelineAsset;
                if (timeline == null) continue;
                if (timeline.markerTrack != null) CheckMarkers(timeline.markerTrack,context);
                foreach (var track in timeline.GetOutputTracks()) CheckTrack(track,context);
                if (timeline.duration > 0.5) context.Report(director,"Transition duration exceeds 0.5 seconds.");
            }
        }
        private static void CheckTrack(TrackAsset track,UILintContext context)
        {
            CheckMarkers(track,context);
            foreach (var child in track.GetChildTracks()) CheckTrack(child,context);
        }
        private static void CheckMarkers(TrackAsset track,UILintContext context)
        {
            foreach (var marker in track.GetMarkers())
                if (marker is SignalEmitter) context.Report(track,"Signals must remain presentation-only when transitions are skipped.");
        }
    }
}
