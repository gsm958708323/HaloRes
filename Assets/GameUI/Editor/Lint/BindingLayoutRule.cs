using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using GameUI.Components;
using GameUI.Runtime;
using UnityEditor;
using UnityEngine;
namespace GameUI.Editor
{
    public sealed class BindingLayoutRule : IUIPrefabLintRule
    {
        public string Id => "binding-layout";
        public UILintLevel Level => UILintLevel.Error;
        public void Check(GameObject root,UILintContext context)
        {
            foreach (var data in root.GetComponentsInChildren<UIBindingData>(true))
            {
                var names=new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in data.entries)
                    if (entry == null || string.IsNullOrWhiteSpace(entry.name) || !names.Add(UIRegistryEmitter.Identifier(entry.name))) context.Report(data,"Duplicate or empty binding name.");
                foreach (var node in data.nodes)
                    if (node == null || node.data == null || !names.Add(UIRegistryEmitter.Identifier(node.data.name))) context.Report(data,"Duplicate or empty node binding.");
                try
                {
                    if (UIBindingEmitter.ComputeLayoutHash(data) != data.layoutHash) context.Report(data,"Binding layout hash is stale. Regenerate binding code.");
                }
                catch (Exception error) { context.Report(data,error.Message); }
                var path=AssetDatabase.GetAssetPath(data);
                var className=string.IsNullOrEmpty(data.logicType) ? data.name : data.logicType.Substring(data.logicType.LastIndexOf('.')+1);
                var generated=string.IsNullOrEmpty(path) ? null : Path.Combine(Path.GetDirectoryName(path),className+".Binding.g.cs");
                if (generated != null && File.Exists(generated))
                {
                    if (!File.ReadAllText(generated).Contains("BindingLayoutHash = 0x"+data.layoutHash.ToString("X8")))
                        context.Report(data,"Generated binding code does not match the serialized layout hash.");
                }
                else foreach (var type in TypeCache.GetTypesDerivedFrom<UIElement>())
                    {
                        if (type.FullName != data.logicType) continue;
                        var field=type.GetField("BindingLayoutHash",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy);
                        if (field != null && field.IsLiteral && (uint)field.GetRawConstantValue() != data.layoutHash)
                            context.Report(data,"Generated binding code does not match the serialized layout hash.");
                        break;
                    }
                if (data != root.GetComponent<UIBindingData>() && !string.IsNullOrEmpty(data.logicType) && data.logicType.EndsWith("View",StringComparison.Ordinal))
                    context.Report(data,"Do not nest a navigable View inside another View.");
            }
        }
    }
}
