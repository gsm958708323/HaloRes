using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Scope;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace GameFramework.UI
{
    public sealed class UIManager : IDisposable
    {
        private readonly Dictionary<Type, UIViewDefinition> _definitions;
        private readonly Dictionary<Type, UIViewRecord> _records = new Dictionary<Type, UIViewRecord>();
        private readonly Dictionary<long, UINavigationEntry> _entries = new Dictionary<long, UINavigationEntry>();
        private readonly SortedDictionary<LayerType, UILayer> _layers = new SortedDictionary<LayerType, UILayer>();
        private readonly Queue<Action> _commands = new Queue<Action>();
        private readonly List<Action> _notifications = new List<Action>();
        private readonly AssetScope _scope;
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly SynchronizationContext _context = SynchronizationContext.Current;
        private GameObject _root;
        private long _nextId;
        private bool _processing;
        private bool _disposeRequested;
        private bool _disposed;

        public UIManager(AssetSystem assets, UIConfigSO configuration, Transform parent = null, Camera worldCamera = null)
        {
            AssetSystem = assets ?? throw new ArgumentNullException(nameof(assets));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _definitions = configuration.CreateSnapshot(assets.Manifest);
            _scope = assets.CreateScope("UI");
            try { InitializeRoot(configuration.ReferenceResolution, parent, worldCamera ?? Camera.main); }
            catch { Dispose(); throw; }
        }

        public AssetSystem AssetSystem { get; }
        public Transform Root => _root == null ? null : _root.transform;
        public bool IsDisposed => _disposed || _disposeRequested;
        public int InstanceCount => _records.Count;
        public UILayer GetLayer(LayerType type) { AssertAlive(); return _layers[type]; }
        public T FindView<T>() where T : UIGameView => _records.TryGetValue(typeof(T), out var record) ? (T)record.View : null;

        public UIHandle<T> Open<T>(params object[] args) where T : UIGameView => Open<T>(args, default);

        public UIHandle<T> Open<T>(object[] args, CancellationToken ct) where T : UIGameView
        {
            AssertAlive();
            ct.ThrowIfCancellationRequested();
            var definition = GetDefinition(typeof(T));
            if (string.IsNullOrWhiteSpace(definition.PrefabAddress)) throw new ArgumentException("Root UI needs a prefab address: " + typeof(T));
            var handle = new UIHandle<T>(this, ++_nextId);
            var arguments = args == null ? Array.Empty<object>() : (object[])args.Clone();
            Enqueue(() =>
            {
                if (IsDisposed || ct.IsCancellationRequested) { handle.CompleteClose(); return; }
                UIViewRecord record;
                try
                {
                    if (!_records.TryGetValue(typeof(T), out record))
                    {
                        record = new UIViewRecord { View = CreateView(typeof(T), _layers[definition.LayerType]) };
                        _records.Add(typeof(T), record);
                        Prepare(record);
                    }
                }
                catch (Exception error) { handle.CompleteClose(error); return; }
                var entry = new UINavigationEntry { Handle = handle, Record = record, Arguments = arguments };
                record.References++;
                _entries.Add(handle.Id, entry);
                _layers[definition.LayerType].Add(entry);
                if (ct.CanBeCanceled) entry.Cancellation = ct.Register(() => PostCancellation(handle));
            });
            return handle;
        }

        public Task<T> OpenAsync<T>(object[] args = null, CancellationToken ct = default) where T : UIGameView
            => Open<T>(args, ct).Opened;

        private async void Prepare(UIViewRecord record)
        {
            Exception failure = null;
            try { await record.View.LoadAsync(); }
            catch (Exception error) { failure = error; }
            Enqueue(() =>
            {
                record.Ready = true; record.Error = failure;
                foreach (var entry in _entries.Values)
                    if (entry.Record == record) entry.Handle.State = UINavigationState.Ready;
            });
        }

        public void Close(UIHandle handle)
        {
            if (handle == null || handle.IsClosed) return;
            AssertAlive();
            if (handle.Owner != this) throw new ArgumentException("Navigation handle belongs to another UIManager.", nameof(handle));
            Enqueue(() => { if (_entries.TryGetValue(handle.Id, out var entry)) Remove(entry); });
        }

        public Task CloseAsync(UIHandle handle) { Close(handle); return handle?.Closed ?? Task.CompletedTask; }
        public void Close<T>() where T : UIGameView
        {
            AssertAlive();
            Enqueue(() => { var entry = _layers[GetDefinition(typeof(T)).LayerType].FindLast(typeof(T)); if (entry != null) Remove(entry); });
        }
        public Task CloseAsync<T>() where T : UIGameView
        {
            AssertAlive();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() => { var entry = _layers[GetDefinition(typeof(T)).LayerType].FindLast(typeof(T)); if (entry != null) Remove(entry); Notify(() => completion.TrySetResult(true)); });
            return completion.Task;
        }
        public void Close(UIGameView view)
        {
            if (view is UISubView child && child.Parent != null) child.Parent.CloseChild(child.GetType());
            else if (view?.ActiveNavigation != null) Close(view.ActiveNavigation);
        }
        public Task PopAsync(LayerType type)
        {
            AssertAlive();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Select the target when this command executes, so two consecutive pops remove two visits.
            Enqueue(() => { var entry = _layers[type].FindLast(); if (entry != null) Remove(entry); Notify(() => completion.TrySetResult(true)); });
            return completion.Task;
        }

        internal void Remove(UINavigationEntry entry, Exception error = null)
        {
            if (!_entries.Remove(entry.Handle.Id)) return;
            entry.Cancellation.Dispose();
            _layers[entry.Record.View.Configuration.LayerType].Remove(entry);
            entry.Record.References--;
            Notify(() => entry.Handle.CompleteClose(error));
        }
        internal void Fault(UIViewRecord record, Exception error)
        {
            foreach (var entry in new List<UINavigationEntry>(_entries.Values)) if (entry.Record == record) Remove(entry, error);
        }
        internal void Notify(Action notification) => _notifications.Add(notification);

        internal void Post(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _threadId) Enqueue(action);
            else _context?.Post(_ => Enqueue(action), null);
        }

        private void PostCancellation(UIHandle handle)
        {
            if (Thread.CurrentThread.ManagedThreadId == _threadId) Enqueue(() => { if (_entries.TryGetValue(handle.Id, out var entry)) Remove(entry); });
            else _context?.Post(_ => { if (!IsDisposed) Close(handle); }, null);
        }

        internal void Enqueue(Action command)
        {
            if (_disposed) return;
            _commands.Enqueue(command);
            if (_processing) return;
            _processing = true;
            try
            {
                while (_commands.Count > 0)
                {
                    _commands.Dequeue()();
                    if (_disposeRequested) { Shutdown(); break; }
                    foreach (var layer in _layers.Values) layer.CommitReady(this);
                    Refresh();
                    if (_disposeRequested) { Shutdown(); break; }
                    foreach (var entry in _entries.Values)
                        if (entry.Committed) entry.Handle.CompleteOpen(entry.Record.View);
                    var notifications = _notifications.ToArray();
                    _notifications.Clear();
                    foreach (var notification in notifications) notification();
                }
            }
            finally { _processing = false; }
        }

        private void Refresh()
        {
            var visible = new List<UINavigationEntry>();
            foreach (var layer in _layers.Values) visible.AddRange(layer.VisibleEntries());
            foreach (var record in new List<UIViewRecord>(_records.Values))
            {
                var selected = visible.Find(entry => entry.Record == record);
                try
                {
                    // A final close needs no history snapshot. Release first even when a business capture callback failed.
                    if (record.References == 0)
                    {
                        _records.Remove(record.View.GetType());
                        record.View.Release();
                        continue;
                    }
                    if (record.View.IsVisible && (selected == null || selected != record.Active))
                    {
                        SaveState(record);
                        record.View.Disable();
                    }
                    if (selected != null && record.Active != selected)
                    {
                        record.Active = selected;
                        record.View.SelectNavigation(selected);
                    }
                }
                catch (Exception error) { Fault(record, error); Enqueue(() => { }); }
            }
            var count = 0;
            foreach (var entry in visible) if (_entries.ContainsKey(entry.Handle.Id)) count += entry.Record.View.SortingCount;
            if (count > short.MaxValue)
            {
                var failed = visible[visible.Count - 1];
                Fault(failed.Record, new InvalidOperationException("Visible UI canvases exceed Unity's sorting-order range."));
                Enqueue(() => { });
                return;
            }
            var order = 0;
            foreach (var entry in visible)
            {
                if (IsDisposed) return;
                if (!_entries.ContainsKey(entry.Handle.Id)) continue;
                try { entry.Record.View.SetCanvasOrder(ref order); entry.Record.View.Enable(); }
                catch (Exception error) { Fault(entry.Record, error); Enqueue(() => { }); }
            }
        }

        private static void SaveState(UIViewRecord record)
        {
            if (record.Active == null) return;
            record.Active.SavedState = record.View.CaptureState();
            record.Active.HasSavedState = true;
        }
        internal UIViewDefinition GetDefinition(Type type) => _definitions.TryGetValue(type, out var definition)
            ? definition : throw new ArgumentException("UI is not configured: " + type);
        internal UIGameView CreateView(Type type, UILayer layer, UIGameView parent = null, UIChildDefinition binding = null)
        {
            var view = (UIGameView)Activator.CreateInstance(type);
            view.Bind(this, GetDefinition(type), layer, (parent == null ? _scope : parent.Assets).CreateChild(type.Name));
            if (view is UISubView child) { child.Parent = parent; child.Binding = binding; }
            return view;
        }
        internal void AssertAlive()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId) throw new InvalidOperationException("Use UIManager on the Unity main thread.");
            if (IsDisposed || _root == null || AssetSystem.IsDisposed) throw new ObjectDisposedException(nameof(UIManager));
        }
        internal static async void Observe(Task operation)
        {
            try { await operation; }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void InitializeRoot(Vector2 resolution, Transform parent, Camera camera)
        {
            _root = new GameObject("UIRoot", typeof(RectTransform));
            _root.layer = 5;
            if (parent != null) _root.transform.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(_root);
            foreach (LayerType type in Enum.GetValues(typeof(LayerType)))
            {
                var node = new GameObject(type.ToString(), typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                node.layer = type == LayerType.SceneLayer ? 0 : 5;
                node.transform.SetParent(_root.transform, false);
                var canvas = node.GetComponent<Canvas>();
                canvas.renderMode = type == LayerType.SceneLayer ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = camera;
                canvas.overrideSorting = true;
                ((RectTransform)node.transform).sizeDelta = resolution;
                if (type == LayerType.SceneLayer) node.transform.localScale = Vector3.one * 0.001f;
                var scaler = node.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = resolution;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                _layers.Add(type, new UILayer(type, canvas));
            }
            if (EventSystem.current != null) return;
            var events = new GameObject("EventSystem", typeof(EventSystem));
            events.transform.SetParent(_root.transform, false);
#if ENABLE_INPUT_SYSTEM
            events.AddComponent<InputSystemUIInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            if (Thread.CurrentThread.ManagedThreadId != _threadId) throw new InvalidOperationException("Dispose UIManager on the Unity main thread.");
            _disposeRequested = true;
            if (!_processing) Shutdown();
        }
        private void Shutdown()
        {
            foreach (var entry in new List<UINavigationEntry>(_entries.Values)) Remove(entry);
            foreach (var record in _records.Values) record.View.Release();
            _records.Clear();
            _scope.Dispose();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            // Run commands issued by lifecycle callbacks so their returned handles/tasks also terminate.
            while (_commands.Count > 0) _commands.Dequeue()();
            _disposed = true;
            foreach (var notification in _notifications.ToArray()) notification();
            _notifications.Clear();
        }
    }
}
