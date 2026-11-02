using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI
{
    /// <summary>Owns rendering flags, not assets. Static child subtrees are excluded and managed by their own view.</summary>
    internal sealed class UIViewPresentation
    {
        private readonly CanvasGroup _group;
        private readonly List<Canvas> _canvases = new List<Canvas>();
        private readonly List<GraphicRaycaster> _raycasters = new List<GraphicRaycaster>();
        private readonly List<Animator> _animators = new List<Animator>();
        private readonly List<int> _offsets = new List<int>();
        private readonly List<bool> _sortingOverrides = new List<bool>();
        private bool[] _canvasFlags, _raycastFlags, _animatorFlags;
        private float _alpha;
        private bool _interactable, _blocks, _visible = true;

        internal UIViewPresentation(GameObject instance, IReadOnlyList<UIChildDefinition> children)
        {
            _group = instance.GetComponent<CanvasGroup>();
            if (_group == null) _group = instance.AddComponent<CanvasGroup>();
            var excluded = new List<Transform>();
            if (children != null)
                foreach (var child in children)
                    if (child.ResType == ResType.Static)
                        excluded.Add(instance.transform.Find(child.NodePath)
                            ?? throw new InvalidOperationException("Static UI NodePath was not found: " + child.NodePath));
            foreach (var component in instance.GetComponentsInChildren<Behaviour>(true))
            {
                if (excluded.Exists(root => component.transform == root || component.transform.IsChildOf(root))) continue;
                if (component is Canvas canvas) _canvases.Add(canvas);
                else if (component is GraphicRaycaster raycaster) _raycasters.Add(raycaster);
                else if (component is Animator animator) _animators.Add(animator);
            }
            _canvases.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
            foreach (var canvas in _canvases) { _offsets.Add(canvas.sortingOrder); _sortingOverrides.Add(canvas.overrideSorting); }
            _canvasFlags = new bool[_canvases.Count];
            _raycastFlags = new bool[_raycasters.Count];
            _animatorFlags = new bool[_animators.Count];
            SetVisible(false);
        }
        internal int SortingCount => _canvases.Count;
        internal void Assign(ref int order)
        {
            for (var i = 0; i < _canvases.Count; i++)
            {
                if (_canvases[i] == null) continue;
                _canvases[i].overrideSorting = true;
                _canvases[i].sortingOrder = order++;
            }
        }
        internal void SetVisible(bool visible)
        {
            if (visible == _visible) return;
            if (!visible)
            {
                _alpha = _group.alpha;
                _interactable = _group.interactable;
                _blocks = _group.blocksRaycasts;
                SaveFlags(_canvases, _canvasFlags);
                SaveFlags(_raycasters, _raycastFlags);
                SaveFlags(_animators, _animatorFlags);
            }
            _visible = visible;
            _group.alpha = visible ? _alpha : 0;
            _group.interactable = visible && _interactable;
            _group.blocksRaycasts = visible && _blocks;
            Apply(_canvases, _canvasFlags, visible);
            Apply(_raycasters, _raycastFlags, visible);
            Apply(_animators, _animatorFlags, visible);
        }
        internal void RestoreBorrowed()
        {
            SetVisible(true);
            for (var i = 0; i < _canvases.Count; i++)
                if (_canvases[i] != null) { _canvases[i].sortingOrder = _offsets[i]; _canvases[i].overrideSorting = _sortingOverrides[i]; }
        }
        private static void SaveFlags<T>(List<T> list, bool[] flags) where T : Behaviour
        {
            for (var i = 0; i < list.Count; i++) flags[i] = list[i] != null && list[i].enabled;
        }
        private static void Apply<T>(List<T> list, bool[] flags, bool visible) where T : Behaviour
        {
            for (var i = 0; i < list.Count; i++) if (list[i] != null) list[i].enabled = visible && flags[i];
        }
    }
}
