using UnityEngine;

namespace Game.AssetSystem.Lessons
{
    /// <summary>
    /// Play-mode mode switch.
    ///
    /// Default: the interactive demo (Assets/Game/Lessons/DemoApp) owns play
    /// mode and the 12 lesson log demos stay silent.
    /// Toggle "Tools/Game Asset System/Toggle Lesson Demo Mode" to run the
    /// lesson log demos instead.
    ///
    /// PlayMode tests always silence every demo: their LogError output would
    /// otherwise fail the unrelated test that happens to be running.
    /// </summary>
    public static class LessonBootGate
    {
        private const string ForceLessonDemosKey = "GameAssetSystem.ForceLessonDemos";

        private static bool _enabled = true;

        /// <summary>When true, lesson log demos run and the interactive demo stays away.</summary>
        public static bool ForceLessonDemos
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetBool(ForceLessonDemosKey, false);
#else
                return false;
#endif
            }
        }

        public static bool Enabled
        {
            get
            {
                if (Application.isPlaying && IsPlayModeTestScene())
                {
                    return false;
                }

                return _enabled;
            }
            set { _enabled = value; }
        }

        public static bool IsPlayModeTestScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            return scene.IsValid() &&
                   scene.name.StartsWith("InitTestScene", System.StringComparison.Ordinal);
        }

        public static void DisableAll()
        {
            _enabled = false;
        }

        public static void EnableAll()
        {
            _enabled = true;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Game Asset System/Toggle Lesson Demo Mode")]
        private static void Toggle()
        {
            var next = !UnityEditor.SessionState.GetBool(ForceLessonDemosKey, false);
            UnityEditor.SessionState.SetBool(ForceLessonDemosKey, next);

            Debug.Log(next
                ? "[LessonBootGate] 课程日志模式：进入 Play 后 12 个课程演示会各自运行，交互演示不启动。"
                : "[LessonBootGate] 交互演示模式：进入 Play 后只启动交互演示（默认）。");
        }
#endif
    }
}
