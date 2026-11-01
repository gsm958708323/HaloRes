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
            if (_root.Mask != null)
            {
                _root.Mask.gameObject.SetActive(presentation.MaskVisible);
                _root.Mask.color = presentation.MaskStyle == UIMaskStyle.Dim ? new Color(0f, 0f, 0f, 0.55f) : new Color(0f, 0f, 0f, 0f);
                if (_root.MaskCanvas != null) _root.MaskCanvas.sortingOrder = presentation.MaskOrder;
            }
            if (_root.Blocker != null)
            {
                _root.Blocker.gameObject.SetActive(presentation.InputLocked);
                _root.Blocker.raycastTarget = presentation.InputLocked;
            }
            if (_root.LoadingIndicator != null) _root.LoadingIndicator.gameObject.SetActive(presentation.LoadingIndicator);
        }
    }
}
