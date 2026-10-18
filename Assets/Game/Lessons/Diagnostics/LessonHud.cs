using System.Collections.Generic;
using UnityEngine;

namespace Game.AssetSystem.Lessons.Diagnostics
{
    /// <summary>
    /// On-screen overlay for the lesson demos. The framework itself has no
    /// visual content: every demo is a headless logic run whose only output is
    /// the console. This HUD mirrors the recent lesson logs on screen so play
    /// mode is not a black window.
    /// F1 toggles it, F2 clears the buffer.
    /// </summary>
    public sealed class LessonHud : MonoBehaviour
    {
        private const int MaxLines = 16;
        private const int PanelWidth = 780;
        private const int PanelHeight = 400;

        private static LessonHud _instance;

        private readonly Queue<string> _lines = new Queue<string>(MaxLines);
        private readonly List<string> _snapshot = new List<string>(MaxLines);

        private bool _visible = true;
        private bool _subscribed;
        private Texture2D _background;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            // The HUD is diagnostics, not a lesson: it stays available even when
            // the lesson demos are switched off through LessonBootGate.
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[LessonHud]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LessonHud>();
        }

        private void Awake()
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _background.Apply();

            Application.logMessageReceived += OnLog;
            _subscribed = true;

            Push("HUD ready. 课程是纯逻辑演示：输出只在这里和 Console，场景里不会有物体。");
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                Application.logMessageReceived -= OnLog;
                _subscribed = false;
            }

            if (_background != null)
            {
                Destroy(_background);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                _visible = !_visible;
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                _lines.Clear();
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return;
            }

            if (condition.IndexOf("[Lesson", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[FullChain", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[Scenario", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[Registry", System.StringComparison.Ordinal) < 0 &&
                type != LogType.Error &&
                type != LogType.Exception)
            {
                return;
            }

            var prefix = type == LogType.Error || type == LogType.Exception
                ? "! "
                : type == LogType.Warning ? "~ " : "";

            Push(prefix + condition);
        }

        private void Push(string line)
        {
            if (line.Length > 150)
            {
                line = line.Substring(0, 147) + "...";
            }

            if (_lines.Count >= MaxLines)
            {
                _lines.Dequeue();
            }

            _lines.Enqueue(line);
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                GUILayout.BeginArea(new Rect(12, 12, 220, 40));
                GUI.Label(new Rect(0, 0, 220, 24), "F1: show lesson HUD");
                GUILayout.EndArea();
                return;
            }

            GUI.DrawTexture(
                new Rect(12, 12, PanelWidth, PanelHeight),
                _background);

            GUILayout.BeginArea(
                new Rect(24, 22, PanelWidth - 24, PanelHeight - 20));

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };

            var body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = false
            };

            GUILayout.Label("Asset System Lessons  ·  逻辑演示，无可渲染内容  ·  F1 hide  ·  F2 clear", title);
            GUILayout.Label(
                "scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                "   autoBoot: " + (Game.AssetSystem.Lessons.LessonBootGate.Enabled ? "on" : "off") +
                "   fps: " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)),
                body);

            GUILayout.Space(4);

            _snapshot.Clear();
            _snapshot.AddRange(_lines);

            for (var i = 0; i < _snapshot.Count; i++)
            {
                GUILayout.Label(_snapshot[i], body);
            }

            if (_snapshot.Count == 0)
            {
                GUILayout.Label(
                    "no lesson output yet. 等待 1 秒，演示会逐条打印到这里和 Console。",
                    body);
            }

            GUILayout.EndArea();
        }
    }
}
