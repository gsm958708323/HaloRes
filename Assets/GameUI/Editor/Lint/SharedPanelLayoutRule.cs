using System;
using System.Collections.Generic;
using GameUI.Components;
using UnityEditor;
using UnityEngine;

namespace GameUI.Editor
{
    public sealed class SharedPanelLayoutRule : IUIPrefabLintRule
    {
        public string Id => "shared-panel-layout";
        public UILintLevel Level => UILintLevel.Error;

        public void Check(GameObject root, UILintContext context)
        {
            var layouts = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (var data in root.GetComponentsInChildren<UIBindingData>(true))
                foreach (var node in data.nodes)
                {
                    if (node == null || node.data == null || !string.Equals(node.kind, "Panel", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(node.typeName)) { context.Report(data, "Panel type is required."); continue; }
                    if (layouts.TryGetValue(node.typeName, out var hash) && hash != node.data.layoutHash)
                        context.Report(node.data, "Panel layout differs for " + node.typeName + ".");
                    layouts[node.typeName] = node.data.layoutHash;
                }
            if (layouts.Count == 0) return;
            var shared = new HashSet<string>(StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab == root) continue;
                foreach (var data in prefab.GetComponentsInChildren<UIBindingData>(true))
                    foreach (var node in data.nodes)
                    {
                        if (node?.data == null || !string.Equals(node.kind, "Panel", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!layouts.TryGetValue(node.typeName ?? "", out var hash)) continue;
                        shared.Add(node.typeName);
                        if (hash != node.data.layoutHash)
                            context.Report(root, "Panel " + node.typeName + " has a different layout in " + AssetDatabase.GetAssetPath(prefab) + ".");
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(node.data.gameObject)) context.Report(root, "Shared Panel " + node.typeName + " must use a nested prefab.");
                    }
            }
            foreach (var data in root.GetComponentsInChildren<UIBindingData>(true))
                foreach (var node in data.nodes)
                    if (node?.data != null && shared.Contains(node.typeName ?? "") && !PrefabUtility.IsAnyPrefabInstanceRoot(node.data.gameObject))
                        context.Report(node.data, "Shared Panel " + node.typeName + " must use a nested prefab.");
        }
    }
}
