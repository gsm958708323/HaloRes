using System.Collections.Generic;
using UnityEngine;

namespace Game.AssetSystem.Demos.Diagnostics
{
    /// <summary>
    /// On-screen log mirror for the framework.
    ///
    /// The core layers are pure logic: their only output is the console. This
    /// HUD mirrors the recent framework logs on screen so a play-mode run is
    /// not a black window. F1 toggles it, F2 clears the buffer.
    ///
    /// It no longer boots itself. A global <c>AfterSceneLoad</c> hook meant
    /// every scene — including the lesson console, which only writes to the
    /// console on purpose — got a panel pasted over it. Only the full-feature
    /// dashboard creates it now, via <see cref="EnsureHud"/>.
    /// </summary>
    public sealed class AssetSystemHud : MonoBehaviour
    {
        private const int MaxLines = 16;
        private const int PanelWidth = 780;
        private const int PanelHeight = 400;

        private static AssetSystemHud _instance;

        private readonly Queue<string> _lines = new Queue<string>(MaxLines);
        private readonly List<string> _snapshot = new List<string>(MaxLines);

        private bool _visible = true;
        private bool _subscribed;
        private Texture2D _background;

        /// <summary>
        /// Clears the singleton when a new play session starts. Needed because
        /// "Enter Play Mode Options" may skip the domain reload, which would
        /// otherwise leave a dead reference here and stop the HUD from booting.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        /// <summary>
        /// Creates the HUD when it is not alive yet and returns it. Called by
        /// <see cref="Game.AssetSystem.Demos.FullFeature.FullFeatureDashboard"/>;
        /// nothing else should need it.
        /// </summary>
        public static AssetSystemHud EnsureHud()
        {
            if (EnsureLiveInstance())
            {
                return _instance;
            }

            var go = new GameObject("[AssetSystemHud]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AssetSystemHud>();

            return _instance;
        }

        /// <summary>Shows or hides the panel without destroying the buffer.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
        }

        /// <summary>
        /// True when a usable instance already exists. A leftover object from a
        /// previous play session (domain reload disabled) can still be alive but
        /// inactive; that one is destroyed so the HUD can boot again.
        /// </summary>
        private static bool EnsureLiveInstance()
        {
            if (_instance == null)
            {
                return false;
            }

            if (_instance.isActiveAndEnabled)
            {
                return true;
            }

            UnityEngine.Object.Destroy(_instance.gameObject);
            _instance = null;

            return false;
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

            Push("HUD ready. 框架是纯逻辑层：输出只在这里和 Console，场景里不会有物体。");
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

            if (condition.IndexOf("[FullChain", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[FullFeature", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[AssetSystem", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[Scenario", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[Registry", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[BundleRuntime", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[AssetCache", System.StringComparison.Ordinal) < 0 &&
                condition.IndexOf("[Patch", System.StringComparison.Ordinal) < 0 &&
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
                GUI.Label(new Rect(0, 0, 220, 24), "F1: show framework HUD");
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

            GUILayout.Label("Asset System  ·  纯逻辑框架，无可渲染内容  ·  F1 hide  ·  F2 clear", title);
            GUILayout.Label(
                "scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                "   scene-driven: on" +
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
                    "no output yet. 进入 Play 后日志会逐条镜像到这里。",
                    body);
            }

            GUILayout.EndArea();
        }
    }
}
