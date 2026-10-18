using System.Collections.Generic;
using UnityEngine;

namespace Game.AssetSystem.Demo.L43
{
    public sealed class Lesson43GraphNode : ScriptableObject
    {
        public string label;

        public List<Lesson43GraphNode> children =
            new List<Lesson43GraphNode>();
    }
}
