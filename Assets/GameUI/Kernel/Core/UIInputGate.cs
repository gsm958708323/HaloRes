using System;
using System.Collections.Generic;

namespace GameUI.Kernel
{
    public sealed class UIInputGate
    {
        private readonly HashSet<string> _owners = new HashSet<string>(StringComparer.Ordinal);

        public bool IsLocked { get { return _owners.Count != 0; } }
        public IReadOnlyCollection<string> Owners { get { return _owners; } }

        public IDisposable Acquire(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Input owner is required.", nameof(owner));
            if (!_owners.Add(owner)) throw new InvalidOperationException("Input owner already holds a gate: " + owner);
            return new Releaser(this, owner);
        }

        private void Release(string owner) { _owners.Remove(owner); }

        private sealed class Releaser : IDisposable
        {
            private UIInputGate _gate;
            private readonly string _owner;

            internal Releaser(UIInputGate gate, string owner) { _gate = gate; _owner = owner; }

            public void Dispose()
            {
                if (_gate == null) return;
                _gate.Release(_owner);
                _gate = null;
            }
        }
    }
}
