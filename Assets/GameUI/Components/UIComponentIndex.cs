using System.Collections.Generic;
using UnityEngine;
namespace GameUI.Components
{
    public static class UIComponentIndex<T> where T : MonoBehaviour
    {
        private static readonly List<T> Items = new List<T>();
        public static IReadOnlyList<T> All { get { return Items; } }
        public static int Version { get; private set; }
        public static void Register(T item) { if (item != null && !Items.Contains(item)) { Items.Add(item); Version++; } }
        public static void Unregister(T item) { if (Items.Remove(item)) Version++; }
    }
}
