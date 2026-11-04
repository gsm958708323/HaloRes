using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Scope;
using UnityEngine;

namespace GameFramework.UI
{
    /// <summary>应用门面后的运行时协调器；把导航状态变化接到实例所有权、加载和 Unity 表现。</summary>
    internal sealed class UIRuntime : IDisposable
    {
        private readonly UIManager _facade;
        private readonly Dictionary<Type, UIViewDefinition> _definitions;
        private readonly Dictionary<long, UIHandle> _handles = new Dictionary<long, UIHandle>();
        private readonly Dictionary<long, CancellationTokenRegistration> _cancellations =
            new Dictionary<long, CancellationTokenRegistration>();
        private readonly Queue<Action> _commands = new Queue<Action>();
        private readonly List<Action> _notifications = new List<Action>();
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly SynchronizationContext _context = SynchronizationContext.Current;
        private readonly UINavigationModel _navigation = new UINavigationModel();
        private readonly UIViewInstanceStore _instances;
        private readonly UIRoot _root;
        private long _nextId;
        private bool _processing;
        private bool _disposeRequested;
        private bool _disposed;

        internal UIRuntime(UIManager facade, AssetSystem assets, UIConfigSO configuration,
            Transform parent, Camera worldCamera)
        {
            _facade = facade ?? throw new ArgumentNullException(nameof(facade));
            AssetSystem = assets ?? throw new ArgumentNullException(nameof(assets));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            _definitions = configuration.CreateSnapshot(assets.Manifest);
            _instances = new UIViewInstanceStore(assets);
            try
            {
                _root = new UIRoot(configuration.ReferenceResolution, parent, worldCamera ?? Camera.main);
            }
            catch
            {
                _instances.Dispose();
                throw;
            }
        }

        internal AssetSystem AssetSystem { get; }
        internal Transform Root => _root.Transform;
        internal Canvas GetCanvas(LayerType layer) => _root.GetLayer(layer).Canvas;
        internal bool IsDisposed => _disposed || _disposeRequested;
        internal int InstanceCount => _instances.Records.Count;

        internal UIHandle<T> Open<T>(object[] args, CancellationToken ct = default) where T : UIGameView
        {
            AssertAlive();
            ct.ThrowIfCancellationRequested();
            var definition = GetDefinition(typeof(T));
            if (string.IsNullOrWhiteSpace(definition.PrefabAddress))
                throw new ArgumentException("Root UI needs a prefab address: " + typeof(T));

            var handle = new UIHandle<T>(this, ++_nextId);
            var copiedArgs = args == null ? Array.Empty<object>() : (object[])args.Clone();
            Enqueue(() => OpenCore(handle, definition, copiedArgs, ct));
            return handle;
        }

        internal Task<T> OpenAsync<T>(object[] args, CancellationToken ct) where T : UIGameView
            => Open<T>(args, ct).Opened;

        private void OpenCore(UIHandle handle, UIViewDefinition definition, object[] args, CancellationToken ct)
        {
            if (IsDisposed || ct.IsCancellationRequested) { Notify(() => handle.CompleteClose()); return; }

            UIViewRecord record = null;
            try
            {
                record = _instances.Retain(definition.ViewType,
                    scope => CreateRootView(definition.ViewType, definition.LayerType, scope));
                var entry = _navigation.Add(handle.Id, definition, args);
                // 复用已就绪实例时没有新的加载回调，必须在入栈时推进新访问，否则它会永久停在 Loading。
                if (record.Ready) _navigation.MarkReady(definition.ViewType);
                _handles.Add(entry.Id, handle);
                handle.State = record.Error != null ? UINavigationState.Failed
                    : record.Ready ? UINavigationState.Ready : UINavigationState.Loading;
                if (record.Error != null) { FailView(record, record.Error); return; }
                RegisterCancellation(handle, ct);

                // 同类型视图只保留一份加载任务；后续访问只添加导航记录和实例引用。
                if (record.Preparation == null && !record.Ready) StartPreparation(record);
            }
            catch (Exception error)
            {
                if (record != null) _instances.Release(definition.ViewType);
                CompleteClosed(handle, error);
            }
        }

        private void StartPreparation(UIViewRecord record)
        {
            record.Preparation = PrepareAsync(record);
        }

        private async Task PrepareAsync(UIViewRecord record)
        {
            Exception error = null;
            try { await record.View.LoadAsync(); }
            catch (Exception failure) { error = failure; }
            Enqueue(() => OnPrepared(record.ViewType, error));
        }

        private void OnPrepared(Type viewType, Exception error)
        {
            var record = _instances.Find(viewType);
            if (record == null) return;
            record.Preparation = null;
            if (error != null) { record.Error = error; FailView(record, error); return; }

            // 准备任务属于共享实例；一次加载完成要推进该类型的全部访问记录。
            record.Ready = true;
            _navigation.MarkReady(viewType);
            foreach (var id in _navigation.History(GetDefinition(viewType).LayerType))
                if (_navigation.Find(id)?.ViewType == viewType) SetHandleState(id, UINavigationState.Ready);
            CommitReady();
        }

        internal void Close(UIHandle handle)
        {
            if (handle == null || handle.IsClosed) return;
            AssertAlive();
            if (handle.Owner != this) throw new ArgumentException("Navigation handle belongs to a different UIManager.", nameof(handle));
            Enqueue(() => CloseCore(handle.Id, null));
        }

        internal Task CloseAsync(UIHandle handle)
        {
            if (handle == null) return Task.CompletedTask;
            Close(handle);
            return handle.Closed;
        }

        internal void Close<T>() where T : UIGameView
        {
            AssertAlive();
            Enqueue(() => CloseLast(typeof(T)));
        }

        internal Task CloseAsync<T>() where T : UIGameView
        {
            AssertAlive();
            return EnqueueAndWait(() => CloseLast(typeof(T)));
        }

        internal void Close(UIGameView view)
        {
            if (view is UISubView child && child.Parent != null) { child.Parent.CloseChild(child.GetType()); return; }
            if (view?.ActiveNavigation != null) Close(view.ActiveNavigation);
        }

        internal Task PopAsync(LayerType layer)
        {
            AssertAlive();
            return EnqueueAndWait(() =>
            {
                var entry = _navigation.Pop(layer);
                if (entry != null) RemoveEntry(entry, null);
            });
        }

        internal IReadOnlyList<UIHandle> GetHistory(LayerType layer)
        {
            AssertAlive();
            var ids = _navigation.History(layer);
            var handles = new List<UIHandle>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
                if (_handles.TryGetValue(ids[i], out var handle)) handles.Add(handle);
            return handles.AsReadOnly();
        }

        internal int GetLayerCount(LayerType layer) { AssertAlive(); return _navigation.Count(layer); }
        internal T FindView<T>() where T : UIGameView => _instances.Find(typeof(T))?.View as T;
        internal void CloseChild(long id, Exception error = null) => Enqueue(() => CloseCore(id, error));
        internal void SaveState(long entryId, object state) => Enqueue(() => _navigation.SaveState(entryId, state));

        private void CloseLast(Type type)
        {
            var entry = _navigation.FindLast(GetDefinition(type).LayerType, type);
            if (entry != null) CloseCore(entry.Id, null);
        }

        private void CloseCore(long id, Exception error)
        {
            var entry = _navigation.Close(id);
            if (entry != null) RemoveEntry(entry, error);
        }

        private void RemoveEntry(UINavigationEntry entry, Exception error)
        {
            // 每条访问释放一份引用；只有最后一条记录关闭时，实例及其 AssetScope 才会释放。
            if (_cancellations.TryGetValue(entry.Id, out var cancellation))
            {
                _cancellations.Remove(entry.Id);
                cancellation.Dispose();
            }
            _instances.Release(entry.ViewType);
            _handles.TryGetValue(entry.Id, out var handle);
            _handles.Remove(entry.Id);
            Notify(() => handle?.CompleteClose(error));
        }

        private void FailView(UIViewRecord record, Exception error)
        {
            // 同类型访问共享一个实例，准备或生命周期失败时必须一起完成，避免遗留未完成的句柄。
            foreach (var entry in _navigation.FailView(record.ViewType))
            {
                _cancellations.TryGetValue(entry.Id, out var cancellation);
                _cancellations.Remove(entry.Id);
                cancellation.Dispose();
                _instances.Release(entry.ViewType);
                _handles.TryGetValue(entry.Id, out var handle);
                _handles.Remove(entry.Id);
                Notify(() => handle?.CompleteClose(error));
            }
        }

        private void CommitReady()
        {
            // 先按导航顺序提交；共享实例只初始化一次，每条访问仍保留自己的参数和状态。
            foreach (var entry in _navigation.CommitReadyInOrder())
            {
                var record = _instances.Find(entry.ViewType);
                if (record == null) continue;
                try
                {
                    if (!record.View.IsAwakened) record.View.Awake();
                    if (IsDisposed) return;
                    if (record.ActiveEntryId == 0) record.ActiveEntryId = entry.Id;
                    var active = _navigation.Find(record.ActiveEntryId) ?? entry;
                    record.ActiveEntryId = active.Id;
                    record.View.SelectNavigation(GetHandle(active.Id), active.Arguments, active.SavedState, active.HasSavedState);
                    if (!record.View.IsStarted) record.View.Start(active.Arguments);
                    if (IsDisposed) return;
                    SetHandleState(entry.Id, UINavigationState.Open);
                }
                catch (Exception error) { FailView(record, error); }
            }
        }

        private void SetHandleState(long id, UINavigationState state)
        {
            if (_handles.TryGetValue(id, out var handle)) handle.State = state;
        }

        private void CompleteOpenedHandles()
        {
            // 在显隐刷新后完成 Opened，使调用方拿到句柄时观察到已经应用的导航状态。
            foreach (var pair in _handles)
            {
                var entry = _navigation.Find(pair.Key);
                if (entry == null || entry.Status != NavigationEntryStatus.Open) continue;
                var record = _instances.Find(entry.ViewType);
                if (record != null) pair.Value.CompleteOpen(record.View);
            }
        }

        private void Refresh()
        {
            // 快照描述最终显示结果；这里统一保存离场状态、切换当前访问并更新可见视图排序。
            var snapshot = _navigation.CreateSnapshot();
            var visible = new HashSet<Type>();
            var totalCanvases = 0;
            foreach (var target in snapshot.VisibleViews)
            {
                visible.Add(target.ViewType);
                if (_instances.Find(target.ViewType) is UIViewRecord record) totalCanvases += record.View.SortingCount;
            }

            foreach (var record in new List<UIViewRecord>(_instances.Records))
            {
                if (record.References <= 0) continue;
                var isVisible = visible.Contains(record.ViewType);
                if (record.View.IsVisible && (!isVisible || record.ActiveEntryId != GetActiveEntry(snapshot, record.ViewType)))
                {
                    // 保存或隐藏回调失败都不能中断其它界面的显隐刷新。
                    if (record.ActiveEntryId != 0)
                    {
                        var entryId = record.ActiveEntryId;
                        UIView.Cleanup(() => _navigation.SaveState(entryId, record.View.CaptureState()));
                    }
                    UIView.Cleanup(record.View.Disable);
                }
            }

            // 离场状态已写回导航记录，重新取快照后再恢复目标访问，避免用旧快照覆盖保存结果。
            snapshot = _navigation.CreateSnapshot();
            foreach (var record in new List<UIViewRecord>(_instances.Records))
            {
                if (record.References <= 0) continue;
                if (snapshot.TryGet(record.ViewType, out var state))
                {
                    record.ActiveEntryId = state.ActiveEntryId;
                    record.View.SelectNavigation(GetHandle(state.ActiveEntryId), ToArray(state.Arguments), state.SavedState, state.HasSavedState);
                }
            }

            if (totalCanvases > short.MaxValue && snapshot.VisibleViews.Count > 0)
            {
                FailView(_instances.Find(snapshot.VisibleViews[snapshot.VisibleViews.Count - 1].ViewType),
                    new InvalidOperationException("Visible UI canvases exceed Unity sorting range."));
                return;
            }

            var order = 0;
            var retryVisibleStack = false;
            foreach (var target in snapshot.VisibleViews)
            {
                var record = _instances.Find(target.ViewType);
                if (record == null || record.References <= 0) continue;
                try { record.View.SetCanvasOrder(ref order); record.View.Enable(); }
                catch (Exception error)
                {
                    FailView(record, error);
                    retryVisibleStack = true;
                    break;
                }
            }

            // 失败视图已从导航栈移除，重算一次以恢复它后面的背景页面。
            if (retryVisibleStack && !IsDisposed) Refresh();
        }

        private long GetActiveEntry(UINavigationSnapshot snapshot, Type type)
            => snapshot.TryGet(type, out var state) ? state.ActiveEntryId : 0;

        private UIHandle GetHandle(long id) => _handles.TryGetValue(id, out var handle) ? handle : null;

        private static object[] ToArray(IReadOnlyList<object> values)
        {
            var result = new object[values.Count];
            for (var i = 0; i < result.Length; i++) result[i] = values[i];
            return result;
        }

        private UIViewDefinition GetDefinition(Type type)
            => _definitions.TryGetValue(type, out var definition) ? definition : throw new ArgumentException("UI is not configured: " + type);

        internal UIGameView CreateChildView(Type type, UIGameView parent, UIChildDefinition binding)
        {
            var view = (UIGameView)Activator.CreateInstance(type);
            view.Bind(_facade, this, GetDefinition(type), parent.LayerType, parent.Assets.CreateChild(type.Name));
            if (view is UISubView child) { child.Parent = parent; child.Binding = binding; }
            return view;
        }

        private UIGameView CreateRootView(Type type, LayerType layer, AssetScope scope)
        {
            var view = (UIGameView)Activator.CreateInstance(type);
            view.Bind(_facade, this, GetDefinition(type), layer, scope);
            return view;
        }

        internal void AssertAlive()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId) throw new InvalidOperationException("Use UI on the Unity main thread.");
            if (IsDisposed || AssetSystem.IsDisposed) throw new ObjectDisposedException(nameof(UIRuntime));
        }

        internal void Enqueue(Action command)
        {
            if (_disposed) return;
            _commands.Enqueue(command);
            if (_processing) return;
            _processing = true;
            try
            {
                // 生命周期回调中的导航请求只入队；每条命令完成提交和显隐刷新后才派发通知。
                while (_commands.Count > 0)
                {
                    try { _commands.Dequeue()(); }
                    catch (Exception error) { Debug.LogException(error); }
                    if (_disposeRequested) { Shutdown(); break; }
                    try { CommitReady(); Refresh(); }
                    catch (Exception error) { Debug.LogException(error); }
                    if (_disposeRequested) { Shutdown(); break; }
                    CompleteOpenedHandles();
                    var notifications = _notifications.ToArray();
                    _notifications.Clear();
                    foreach (var notification in notifications)
                        try { notification(); } catch (Exception error) { Debug.LogException(error); }
                }
            }
            finally { _processing = false; }
        }

        private Task EnqueueAndWait(Action command)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                try { command(); Notify(() => completion.TrySetResult(true)); }
                catch (Exception error) { Notify(() => completion.TrySetException(error)); }
            });
            return completion.Task;
        }

        private void RegisterCancellation(UIHandle handle, CancellationToken token)
        {
            if (token.CanBeCanceled) _cancellations[handle.Id] = token.Register(() => Post(() => CloseCore(handle.Id, null)));
        }

        private void CompleteClosed(UIHandle handle, Exception error = null) => Notify(() => handle.CompleteClose(error));
        internal void Notify(Action action) => _notifications.Add(action);
        internal void Post(Action action)
        {
            // 取消回调可能来自工作线程，统一切回创建 UIManager 的线程修改导航状态。
            if (Thread.CurrentThread.ManagedThreadId == _threadId) Enqueue(action);
            else _context?.Post(_ => Enqueue(action), null);
        }

        internal static async void Observe(Task operation)
        {
            try { await operation; }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error); }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            AssertAlive();
            _disposeRequested = true;
            if (!_processing) Shutdown();
        }

        private void Shutdown()
        {
            // 先终止访问与异步回调，再释放视图实例，最后销毁根节点；AssetSystem 仍由应用拥有。
            foreach (var registration in _cancellations.Values) registration.Dispose();
            _cancellations.Clear();
            foreach (var entry in _navigation.RemoveAll())
            {
                _handles.TryGetValue(entry.Id, out var handle);
                Notify(() => handle?.CompleteClose());
            }
            _handles.Clear();
            _instances.Dispose();
            _root.Dispose();
            _disposed = true;
            _disposeRequested = false;
            // Dispose 可能由生命周期回调触发；先终止尚未执行的命令并收集其完成通知，再统一派发。
            while (_commands.Count > 0)
            {
                try { _commands.Dequeue()(); }
                catch (Exception error) { Debug.LogException(error); }
            }
            var notifications = _notifications.ToArray();
            _notifications.Clear();
            foreach (var notification in notifications) notification();
        }
    }
}
