using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Scheduling.L36
{
    public sealed class LoadScope : IDisposable
    {
        private readonly HashSet<ScheduledLoadTaskBase> _tasks =
            new HashSet<ScheduledLoadTaskBase>();

        public LoadScope(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "name must not be empty.",
                    nameof(name));
            }

            Name = name;
        }

        public string Name { get; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            var snapshot = new List<ScheduledLoadTaskBase>(_tasks);
            _tasks.Clear();

            for (var i = 0; i < snapshot.Count; i++)
            {
                snapshot[i].CancelFromScope();
            }
        }

        internal void Track(ScheduledLoadTaskBase task)
        {
            if (task == null)
            {
                return;
            }

            if (IsDisposed)
            {
                task.CancelFromScope();
                return;
            }

            _tasks.Add(task);
        }

        internal void Untrack(ScheduledLoadTaskBase task)
        {
            if (task == null)
            {
                return;
            }

            _tasks.Remove(task);
        }
    }
}
