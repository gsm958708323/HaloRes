using GameUI.Kernel;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    public sealed class UIPresentationApplier : IUIPresentationSink
    {
        private readonly UIRoot _root;
        private readonly UICameraRig _camera;

        public UIPresentationApplier(UIRoot root, UICameraRig camera)
        {
            _root = root;
            _camera = camera;
        }

        public void Apply(in UIPresentation presentation)
        {
            if (_root == null) return;
            _camera?.SetWorldCovered(presentation.WorldCovered);
            _root.ApplyMask(presentation);
            _root.ApplyInputBlocker(presentation.InputGateLocked, presentation.SystemOverlayVisible, presentation.SystemOverlayOrder);
            _root.SetLoadingIndicator(presentation.LoadingIndicator);
        }
    }
}
