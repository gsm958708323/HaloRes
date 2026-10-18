using System.Collections.Generic;
using UnityEngine;

namespace Game.AssetSystem.Editor.Demo.L42
{
    public sealed class Lesson42GraphNode : ScriptableObject
    {
        public string label;
        public List<Lesson42GraphNode> children =
            new List<Lesson42GraphNode>();
    }
}
