using System.Collections.Generic;
using GameUI.Components;
using UnityEngine;
namespace GameUI.Runtime
{
    internal sealed class UIWorldHudSystem : System.IDisposable
    {
        private readonly Dictionary<UIWorldHudAnchor,UIWorldHud> _huds=new Dictionary<UIWorldHudAnchor,UIWorldHud>();
        private readonly List<UIWorldHudAnchor> _removed=new List<UIWorldHudAnchor>();
        private readonly UIRoot _root;
        private readonly UICameraRig _cameras;
        private Camera _worldCamera;
        internal UIWorldHudSystem(UIRoot root,UICameraRig cameras) { _root=root;_cameras=cameras; }
        internal void Tick()
        {
            var anchors=UIComponentIndex<UIWorldHudAnchor>.All;
            if(_worldCamera != _cameras.WorldCamera)
            {
                foreach(var hud in _huds.Values) hud.Dispose();
                _huds.Clear();
                _worldCamera=_cameras.WorldCamera;
            }
            foreach(var pair in _huds)
                if(pair.Key == null || !pair.Key.isActiveAndEnabled) _removed.Add(pair.Key);
            foreach(var key in _removed) { _huds[key].Dispose();_huds.Remove(key); }
            _removed.Clear();
            if(_cameras.WorldCamera == null) return;
            for(var i=0;i<anchors.Count;i++)
            {
                var anchor=anchors[i]; if(anchor == null || anchor.hud == null) continue;
                UIWorldHud hud;
                if(!_huds.TryGetValue(anchor,out hud))
                {
                    hud=new UIWorldHud(anchor.hud,anchor.transform,_root.WorldHudRoot,_cameras.WorldCamera,_cameras.UICamera,anchor.offset);
                    _huds.Add(anchor,hud);
                }
                hud.LateUpdate();
            }
        }
        public void Dispose() { foreach(var hud in _huds.Values)hud.Dispose();_huds.Clear();UIWorldHud.ClearPool(); }
    }
}
