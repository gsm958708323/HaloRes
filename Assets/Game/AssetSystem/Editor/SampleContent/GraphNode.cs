using System.Collections.Generic;
using UnityEngine;

namespace Game.AssetSystem.Editor.SampleContent
{
    /// <summary>
    /// Minimal asset graph node used by the sample content generated for the
    /// build-pipeline tests and the full-chain demo.
    /// </summary>
    public sealed class GraphNode : ScriptableObject
    {
        public string label;
        public List<GraphNode> children = new List<GraphNode>();
    }
}
