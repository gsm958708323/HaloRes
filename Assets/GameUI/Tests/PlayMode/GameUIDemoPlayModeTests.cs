using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using GameUI.Demo;
using GameUI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameUI.Tests.PlayMode
{
    public sealed class GameUIDemoPlayModeTests
    {
        private const string ScenePath = "Assets/GameUI/Demo/GameUIDemo.unity";

        [UnitySetUp]
        public IEnumerator LoadDemoScene() => UniTask.ToCoroutine(async () =>
        {
            var operation = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null, "The GameUI demo scene must be loadable by path.");
            while (!operation.isDone) await UniTask.Yield();
            await WaitFor(() => GameObject.Find("DemoHome[UI]") != null, "DemoHome did not load.");
        });

        [UnityTearDown]
        public IEnumerator ShutdownDemo() => UniTask.ToCoroutine(async () =>
        {
            UIBoot.Shutdown();
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator DemoHomeLoadsAndWiresButtons() => UniTask.ToCoroutine(async () =>
        {
            var home = GameObject.Find("DemoHome[UI]");
            Assert.That(home, Is.Not.Null);
            Assert.That(home.transform.Find("OpenPopup")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(home.transform.Find("OpenOverlay")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(UI.IsOpen<DemoHomeView>(), Is.True);
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator OpenPopupButtonShowsPopup() => UniTask.ToCoroutine(async () =>
        {
            var home = GameObject.Find("DemoHome[UI]");
            home.transform.Find("OpenPopup").GetComponent<Button>().onClick.Invoke();
            await WaitFor(() => GameObject.Find("DemoPopup[UI]") != null, "DemoPopup did not open.");
            Assert.That(UI.IsOpen<DemoPopupView>(), Is.True);

            GameObject.Find("DemoPopup[UI]").transform.Find("ClosePopup").GetComponent<Button>().onClick.Invoke();
            await WaitFor(() => GameObject.Find("DemoPopup[UI]") == null, "DemoPopup did not close.");
            Assert.That(UI.IsOpen<DemoPopupView>(), Is.False);
        });

        [UnityTest]
        public IEnumerator OpenOverlayButtonShowsOverlay() => UniTask.ToCoroutine(async () =>
        {
            var home = GameObject.Find("DemoHome[UI]");
            home.transform.Find("OpenOverlay").GetComponent<Button>().onClick.Invoke();
            await WaitFor(() => GameObject.Find("DemoOverlay[UI]") != null, "DemoOverlay did not open.");
            Assert.That(UI.IsOpen<DemoOverlayView>(), Is.True);

            GameObject.Find("DemoOverlay[UI]").transform.Find("CloseOverlay").GetComponent<Button>().onClick.Invoke();
            await WaitFor(() => GameObject.Find("DemoOverlay[UI]") == null, "DemoOverlay did not close.");
            Assert.That(UI.IsOpen<DemoOverlayView>(), Is.False);
        });

        private static async UniTask WaitFor(Func<bool> condition, string failure)
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline) Assert.Fail(failure);
                await UniTask.Yield();
            }
        }
    }
}
