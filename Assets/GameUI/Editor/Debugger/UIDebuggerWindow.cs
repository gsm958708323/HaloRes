using GameUI.Runtime;
using UnityEditor;
using UnityEngine;

namespace GameUI.Editor
{
    public sealed class UIDebuggerWindow : EditorWindow
    {
        [MenuItem("Window/GameUI/Debugger")]
        private static void Open() { GetWindow<UIDebuggerWindow>("GameUI Debugger"); }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kernel", EditorStyles.boldLabel);
            if (UIBoot.Kernel == null) { EditorGUILayout.HelpBox("UIBoot is not installed.", MessageType.Info); return; }
            EditorGUILayout.LabelField("Input Locked", UIBoot.Kernel.Presentation.InputLocked.ToString());
            EditorGUILayout.LabelField("World Covered", UIBoot.Kernel.Presentation.WorldCovered.ToString());
            EditorGUILayout.TextArea(UIBoot.Kernel.Dump(), GUILayout.ExpandHeight(true));
            if (GUILayout.Button("Refresh")) Repaint();
        }
    }
}
