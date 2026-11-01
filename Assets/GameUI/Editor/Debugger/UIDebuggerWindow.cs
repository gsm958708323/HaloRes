using System.IO;
using GameUI.Runtime;
using UnityEditor;
using UnityEngine;
namespace GameUI.Editor
{
    public sealed class UIDebuggerWindow : EditorWindow
    {
        private Vector2 _scroll;
        [MenuItem("Window/GameUI/Debugger")]
        private static void Open() { GetWindow<UIDebuggerWindow>("GameUI Debugger"); }
        [MenuItem("GameUI/Log Current Snapshot")]
        private static void LogSnapshot() { if (UIBoot.Kernel != null) Debug.Log(UIBoot.Kernel.Dump()); }
        private void OnInspectorUpdate() { Repaint(); }
        private void OnGUI()
        {
            if (UIBoot.Kernel == null) { EditorGUILayout.HelpBox("UIBoot is not installed.",MessageType.Info); return; }
            var kernel=UIBoot.Kernel;
            EditorGUILayout.LabelField("Input Locked",kernel.Presentation.InputLocked.ToString());
            EditorGUILayout.LabelField("World Covered",kernel.Presentation.WorldCovered.ToString());
            EditorGUILayout.LabelField("External input owners",string.Join(", ",kernel.Input.Owners));
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(kernel.Dump());
            foreach (var instance in kernel.VisibleInDrawOrder)
                if (instance.Logic is UIView view) EditorGUILayout.TextArea(view.DumpScopes());
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Export snapshot"))
            {
                var path=EditorUtility.SaveFilePanel("UI snapshot","","ui-snapshot.txt","txt");
                if (!string.IsNullOrEmpty(path)) File.WriteAllText(path,kernel.Dump());
            }
        }
    }
}
