using GameUI.Components;
using UnityEngine.Playables;

namespace GameUI.Runtime
{
    internal sealed class UITimelineTransition : IUITransition
    {
        private readonly PlayableDirector _director;
        public UITimelineTransition(PlayableDirector director) { _director = director; }
        public bool IsDone { get { return _director == null || _director.state != PlayState.Playing || _director.time >= _director.duration - 0.0001d; } }
        public void Play()
        {
            if (_director == null) return;
            _director.time = 0d;
            _director.Evaluate();
            _director.Play();
        }
        public void Finish()
        {
            if (_director == null) return;
            _director.time = _director.duration;
            _director.Evaluate();
        }
        public void Stop() { if (_director != null) _director.Stop(); }
    }
}
