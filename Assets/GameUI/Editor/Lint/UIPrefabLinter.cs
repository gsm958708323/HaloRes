using System.Collections.Generic;
using GameUI.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace GameUI.Editor
{
    public enum UILintLevel { Info, Warning, Error }
    public interface IUIPrefabLintRule { string Id { get; } UILintLevel Level { get; } void Check(GameObject root, UILintContext context); }

    public sealed class UILintContext
    {
        internal readonly List<string> Messages = new List<string>();
        public void Report(Object node, string message) { Messages.Add((node != null ? node.name : "<root>") + ": " + message); }
    }

    public sealed class TransitionSignalRule : IUIPrefabLintRule
    {
        public string Id { get { return "transition-signal"; } }
        public UILintLevel Level { get { return UILintLevel.Warning; } }
        public void Check(GameObject root, UILintContext context)
        {
            var directors = root.GetComponentsInChildren<PlayableDirector>(true);
            for (var i = 0; i < directors.Length; i++) if (directors[i].playableAsset != null)
                context.Report(directors[i], "Transition Timeline contains a playable asset; keep Signals presentation-only.");
        }
    }

    public static class UIPrefabLinter
    {
        [MenuItem("GameUI/Lint Selected Prefab")]
        public static void LintSelected()
        {
            var root = Selection.activeObject as GameObject;
            if (root == null) { Debug.LogWarning("Select a prefab or prefab instance."); return; }
            var context = new UILintContext();
            var binding = root.GetComponent<UIBindingData>();
            if (binding == null) context.Report(root, "UIBindingData is missing.");
            new TransitionSignalRule().Check(root, context);
            for (var i = 0; i < context.Messages.Count; i++) Debug.LogWarning("[GameUI Lint] " + context.Messages[i], root);
            if (context.Messages.Count == 0) Debug.Log("[GameUI Lint] No issues found.", root);
        }
    }
}
