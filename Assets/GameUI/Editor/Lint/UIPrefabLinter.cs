using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameUI.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameUI.Editor
{
    public enum UILintLevel { Info, Warning, Error }
    public interface IUIPrefabLintRule
    {
        string Id { get; }
        UILintLevel Level { get; }
        void Check(GameObject root,UILintContext context);
    }
    public sealed class UILintContext
    {
        public readonly List<string> Messages = new List<string>();
        public int ErrorCount { get; private set; }
        private UILintLevel _level;
        internal void SetLevel(UILintLevel level) { _level=level; }
        public void Report(UnityEngine.Object node,string message)
        {
            if (_level == UILintLevel.Error) ErrorCount++;
            Messages.Add(_level + ": " + (node != null ? node.name : "<root>") + ": " + message);
        }
    }

    public static class UIPrefabLinter
    {
        [MenuItem("GameUI/Lint Selected Prefab")]
        public static void LintSelected()
        {
            var root=Selection.activeObject as GameObject;
            if (root == null) throw new InvalidOperationException("Select a UI prefab.");
            var result=Check(root);
            foreach (var message in result.Messages)
                if (message.StartsWith("Error:",StringComparison.Ordinal)) Debug.LogError("[GameUI Lint] " + message,root);
                else Debug.LogWarning("[GameUI Lint] " + message,root);
            if (result.Messages.Count == 0) Debug.Log("[GameUI Lint] No issues found.",root);
        }
        public static UILintContext Check(GameObject root)
        {
            var context=new UILintContext();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IUIPrefabLintRule>().Where(t=>!t.IsAbstract).OrderBy(t=>t.FullName,StringComparer.Ordinal))
            {
                var rule=(IUIPrefabLintRule)Activator.CreateInstance(type);
                context.SetLevel(rule.Level);
                rule.Check(root,context);
            }
            return context;
        }
    }

    [InitializeOnLoad]
    public sealed class UIPrefabSaveHook : AssetModificationProcessor
    {
        static UIPrefabSaveHook() { PrefabStage.prefabSaving += OnPrefabSaving; }
        private static void OnPrefabSaving(GameObject root)
        {
            var stage=PrefabStageUtility.GetCurrentPrefabStage();
            if(stage!=null) Prepare(root,stage.assetPath);
        }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (var path in paths)
            {
                if (!path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)) continue;
                var stage=PrefabStageUtility.GetCurrentPrefabStage();
                if(stage!=null && stage.assetPath==path) continue;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Prepare(prefab,path);
            }
            return paths;
        }

        private static void Prepare(GameObject prefab,string path)
        {
                if (prefab == null || prefab.GetComponent<UIBindingData>() == null) return;
                foreach(var data in prefab.GetComponentsInChildren<UIBindingData>(true).Reverse())
                {
                    if(string.IsNullOrEmpty(data.logicType))continue;
                    var split=data.logicType.LastIndexOf('.');
                    var className=split < 0 ? data.logicType : data.logicType.Substring(split+1);
                    var ns=split < 0 ? "" : data.logicType.Substring(0,split);
                    UIBindingEmitter.Generate(data,className,ns,Path.GetDirectoryName(path).Replace('\\','/'));
                }
                var result=UIPrefabLinter.Check(prefab);
                foreach (var message in result.Messages)
                    if (message.StartsWith("Error:",StringComparison.Ordinal)) Debug.LogError("[GameUI Lint] " + path + " " + message,prefab);
                    else Debug.LogWarning("[GameUI Lint] " + path + " " + message,prefab);
        }
    }
}
