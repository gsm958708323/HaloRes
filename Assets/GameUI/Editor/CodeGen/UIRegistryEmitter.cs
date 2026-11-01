using System;
using System.IO;
using System.Linq;
using System.Text;
using GameUI.Runtime;
using UnityEditor;

namespace GameUI.Editor
{
    public static class UIRegistryEmitter
    {
        [MenuItem("GameUI/Generate UIView Registry")]
        public static void Generate()
        {
            var types = TypeCache.GetTypesWithAttribute<UIViewAttribute>().Where(t => typeof(UIView).IsAssignableFrom(t) && !t.IsAbstract).OrderBy(t => t.FullName).ToArray();
            var output = "Assets/GameUI/Generated/UIViewRegistry.g.cs";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var builder = new StringBuilder();
            builder.AppendLine("using GameUI.Kernel;");
            builder.AppendLine("using GameUI.Runtime;");
            builder.AppendLine("namespace GameUI.Generated { public static class UIViewRegistryGenerated {");
            builder.AppendLine("public static UIViewRegistry Create() { var registry = new UIViewRegistry();");
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                var attr = (UIViewAttribute)Attribute.GetCustomAttribute(type, typeof(UIViewAttribute));
                var id = type.FullName.Replace('.', '/');
                var prefab = string.IsNullOrEmpty(attr.PrefabPath) ? id : attr.PrefabPath;
                builder.Append("registry.Register(new UIViewInfo { Id=\"").Append(id).Append("\", ViewType=typeof(").Append(type.FullName).Append("), Mode=UIMode.").Append(attr.Mode)
                    .Append(", Layer=UILayer.").Append(attr.Layer).Append(", CoversWorld=").Append(attr.CoversWorld ? "true" : "false")
                    .Append(", Context=\"").Append(attr.Context ?? string.Empty).Append("\", Visibility=UIOverlayVisibility.").Append(attr.Visibility)
                    .Append(", ContextTag=\"").Append(attr.ContextTag ?? string.Empty).Append("\", BackBehavior=UIBackBehavior.").Append(attr.Back)
                    .Append(", MaskStyle=UIMaskStyle.").Append(attr.Mask).Append(", CloseOnMaskClick=").Append(attr.CloseOnMaskClick ? "true" : "false")
                    .Append(", KeepAlive=").Append(attr.KeepAlive ? "true" : "false").Append(", InstancePolicy=UIInstancePolicy.").Append(attr.Instance)
                    .Append(", PrefabPath=\"").Append(prefab).Append("\", Factory=()=>new ").Append(type.FullName).Append("() });\n");
            }
            builder.AppendLine("return registry; } } }");
            File.WriteAllText(output, builder.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(output);
            UnityEngine.Debug.Log("Generated " + output);
        }
    }
}
