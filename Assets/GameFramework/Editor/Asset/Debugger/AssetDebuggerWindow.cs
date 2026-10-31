using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameFramework.Asset.Editor.Debugger
{
    public sealed class AssetDebuggerWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _filter = string.Empty;
        [MenuItem("Game Framework/Assets/Debugger")]
        private static void Open() => GetWindow<AssetDebuggerWindow>("Asset Debugger");
        private void OnInspectorUpdate() { Repaint(); }

        private void OnGUI()
        {
            _filter = EditorGUILayout.TextField("Address or owner", _filter);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var drivers = Object.FindObjectsByType<AssetSystemDriver>(FindObjectsSortMode.None);
            if (drivers.Length == 0) EditorGUILayout.LabelField("No active AssetSystem driver.");
            foreach (var driver in drivers)
            {
                if (driver.System == null) continue;
                EditorGUILayout.LabelField(driver.name, EditorStyles.boldLabel);
                var snapshot = driver.System.Tracker.Capture();
                EditorGUILayout.LabelField("Assets: " + snapshot.Assets.Count + "    Bundles: " + snapshot.Bundles.Count);
                foreach (var asset in snapshot.Assets)
                {
                    if (!string.IsNullOrEmpty(_filter) && !asset.Address.Contains(_filter) && !asset.Owners.Any(owner => owner.Contains(_filter))) continue;
                    EditorGUILayout.LabelField(asset.Address + "    " + asset.State + "    ref=" + asset.ReferenceCount);
                    foreach (var owner in asset.Owners) EditorGUILayout.LabelField("    " + owner);
                    if (!string.IsNullOrEmpty(asset.Error)) EditorGUILayout.HelpBox(asset.Error, MessageType.Warning);
                }
                EditorGUILayout.Space();
                foreach (var bundle in snapshot.Bundles)
                    EditorGUILayout.LabelField(bundle.Name + "    " + bundle.State + "    ref=" + bundle.ReferenceCount + "    " + bundle.Size + " bytes");
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
