using System;
using System.IO;
using Cysharp.Threading.Tasks;
using GameUI.Runtime;
using GameUI.Kernel;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Demo
{
    public sealed class GameUIDemoSmokeTest : MonoBehaviour
    {
        [Serializable]
        private sealed class Result { public bool passed; public string mode; public int checkedViews; public string error; }
        private string _resultPath;
        private string _runtimeError;

        internal static void AttachIfRequested(GameObject target)
        {
            if(Application.isEditor) return;
            var arguments=Environment.GetCommandLineArgs();
            for(var i=0;i<arguments.Length-1;i++)
            {
                if(arguments[i]!="--ui-smoke-result")continue;
                var smoke=target.AddComponent<GameUIDemoSmokeTest>();
                Application.runInBackground=true;
                smoke._resultPath=Path.GetFullPath(arguments[i+1]);
                break;
            }
        }
        private void Start(){Application.logMessageReceived+=OnLog;Run().Forget();}
        private void OnDestroy(){Application.logMessageReceived-=OnLog;}
        private void OnLog(string message,string stack,LogType type)
        {
            if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) _runtimeError=message+"\n"+stack;
        }
        private async UniTaskVoid Run()
        {
            var result=new Result{mode="Bundle"};
            try
            {
                await Wait(()=>UIBoot.Kernel!=null && UI.IsOpen<DemoHomeView>() && !UIBoot.Kernel.Presentation.InputGateLocked);
                result.checkedViews++;
                var popup=UI.Open<DemoPopupView>();await Wait(()=>popup.Phase==UIPhase.Shown);
                UIBoot.Root.Mask.GetComponent<Button>().onClick.Invoke();await popup.WaitClosed();result.checkedViews++;
                var system=UI.Open<DemoSystemView>();await Wait(()=>system.Phase==UIPhase.Shown);
                if(UIBoot.Kernel.Presentation.InputGateLocked)throw new Exception("System overlay blocked its own controls.");
                GameObject.Find("DemoSystem[UI]").transform.Find("CloseSystem").GetComponent<Button>().onClick.Invoke();
                await system.WaitClosed();result.checkedViews++;
                var confirm=UI.Open<DemoConfirmView,bool>(new DemoConfirmArgs{Message="Player smoke"});
                await Wait(()=>confirm.Raw.Phase==UIPhase.Shown);
                GameObject.Find("DemoConfirm[UI]").transform.Find("Confirm").GetComponent<Button>().onClick.Invoke();
                var answer=await confirm.WaitResult();if(!answer.HasValue||!answer.Value)throw new Exception("Typed result was lost.");
                result.checkedViews++;
                var widgets=UI.Open<DemoWidgetsView>();await Wait(()=>widgets.Phase==UIPhase.Shown);
                var view=GameObject.Find("DemoWidgets[UI]");
                if(view.transform.Find("Model").GetComponent<RawImage>().texture==null)throw new Exception("Model target is missing.");
                if(view.transform.Find("List/Viewport/Content").childCount>=20)throw new Exception("Virtual list allocated too many instances.");
                widgets.Close();await widgets.WaitClosed();result.checkedViews++;
                var owner=GetComponent<GameUIDemoBootstrap>();var assets=owner.System;
                UIBoot.Shutdown();await Wait(()=>assets.IsIdle);
                await assets.ShutdownAsync();
                if(assets.Tracker.Capture().Assets.Count!=0||assets.Tracker.Capture().Bundles.Count!=0)throw new Exception("Resource handles remained after shutdown.");
                if(_runtimeError!=null)throw new Exception("Runtime error during smoke test: "+_runtimeError);
                result.passed=true;
            }
            catch(Exception error){result.error=error.ToString();Debug.LogException(error);}
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_resultPath));
                File.WriteAllText(_resultPath,JsonUtility.ToJson(result,true));
            }
            finally { Application.Quit(result.passed?0:1); }
        }
        private static async UniTask Wait(Func<bool> condition)
        {
            var deadline=Time.realtimeSinceStartup+30;
            while(!condition())
            {
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(UIBoot.Kernel?.Dump()??"UI did not initialize.");
                await UniTask.Yield();
            }
        }
    }
}
