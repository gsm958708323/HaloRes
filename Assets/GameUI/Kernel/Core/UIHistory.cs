using System;
using System.Collections.Generic;
using System.Text;

namespace GameUI.Kernel
{
    public readonly struct UIChange
    {
        public UIChange(int frame, double time, int entryId, string viewId, UIMode mode, UIPhase from, UIPhase to, UIExitIntent intent, UICloseReason reason, string source)
        {
            Frame = frame;
            Time = time;
            EntryId = entryId;
            ViewId = viewId;
            Mode = mode;
            From = from;
            To = to;
            Intent = intent;
            Reason = reason;
            Source = source;
        }

        public int Frame { get; }
        public double Time { get; }
        public int EntryId { get; }
        public string ViewId { get; }
        public UIMode Mode { get; }
        public UIPhase From { get; }
        public UIPhase To { get; }
        public UIExitIntent Intent { get; }
        public UICloseReason Reason { get; }
        public string Source { get; }
    }

    public sealed class UIHistory
    {
        private readonly UIChange[] _buffer = new UIChange[64];
        private int _next;
        private int _count;

        public int Count { get { return _count; } }

        public event Action<UIChange> Changed;

        internal void Write(in UIChange change)
        {
            _buffer[_next] = change;
            _next = (_next + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
            Changed?.Invoke(change);
        }

        public IReadOnlyList<UIChange> Snapshot()
        {
            var result = new List<UIChange>(_count);
            for (var i = 0; i < _count; i++) result.Add(_buffer[(_next - _count + i + _buffer.Length) % _buffer.Length]);
            return result;
        }

        public string Dump()
        {
            var builder = new StringBuilder();
            foreach (var change in Snapshot())
                builder.Append(change.Frame).Append(' ').Append(change.EntryId).Append(' ').Append(change.ViewId)
                    .Append(' ').Append(change.From).Append(" -> ").Append(change.To)
                    .Append(" intent=").Append(change.Intent).Append(" reason=").Append(change.Reason)
                    .Append(" source=").Append(change.Source).AppendLine();
            return builder.ToString();
        }
    }
}
