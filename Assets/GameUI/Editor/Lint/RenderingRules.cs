using GameUI.Components;
using UnityEngine;
using UnityEngine.UI;
namespace GameUI.Editor
{
    public sealed class ParticleSortingRule : IUIPrefabLintRule
    {
        public string Id => "particle-sorting";
        public UILintLevel Level => UILintLevel.Error;
        public void Check(GameObject root,UILintContext context)
        {
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (renderer.GetComponentInParent<UISortingSlot>() == null) context.Report(renderer,"ParticleSystemRenderer requires UISortingSlot.");
            foreach (var slot in root.GetComponentsInChildren<UISortingSlot>(true))
                if (slot.offset < 1 || slot.offset > 17) context.Report(slot,"Sorting slot offset must be 1 through 17.");
        }
    }
    public sealed class GraphicRaycastRule : IUIPrefabLintRule
    {
        public string Id => "graphic-raycast";
        public UILintLevel Level => UILintLevel.Warning;
        public void Check(GameObject root,UILintContext context)
        {
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                if (graphic.raycastTarget && graphic.GetComponentInParent<Selectable>(true) == null)
                    context.Report(graphic,"Decorative Graphics should disable raycastTarget.");
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                if (animator.runtimeAnimatorController != null) context.Report(animator,"Animator controllers may conflict with Timeline transitions.");
        }
    }
}
