using System.Collections.Generic;
using UnityEngine;

namespace Game.AssetSystem.FullChain
{
    public sealed class DemoGraphNode : ScriptableObject
    {
        public string label;

        public List<DemoGraphNode> children =
            new List<DemoGraphNode>();
    }
}
