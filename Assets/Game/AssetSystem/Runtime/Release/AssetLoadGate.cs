using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.AssetSystem.Integration;

using Game.AssetSystem.Core;

namespace Game.AssetSystem.Release
{
    public sealed class AssetLoadGate
    {
        private sealed class Pending
        {
            public Func<Task<IBundlePayload>> Work;
            public TaskCompletionSource<IBundlePayload> Completion;
            public string Owner;
        }

        private readonly Queue<Pending> _queue = new Queue<Pending>();

        private int _running;
        private int _framesTicked;

        public int MaxStartsPerFrame = 2;
        public int MaxConcurrent = 4;

        public int PendingCount
        {
            get { return _queue.Count; }
        }

        public int RunningCount
        {
            get { return _running; }
        }

        public int LastFrameStarts { get; private set; }

        public int TotalStarted { get; private set; }

        public int TotalCompleted { get; private set; }

        public Task<IBundlePayload> Enqueue(
            Func<Task<IBundlePayload>> work,
            string owner)
        {
            MainThreadPump.EnsureMainThread(nameof(Enqueue));

            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            var tcs = new TaskCompletionSource<IBundlePayload>();

            _queue.Enqueue(new Pending
            {
                Work = work,
                Completion = tcs,
                Owner = string.IsNullOrEmpty(owner) ? "Unnamed" : owner
            });

            return tcs.Task;
        }

        public void Tick()
        {
            MainThreadPump.EnsureMainThread(nameof(Tick));

            _framesTicked++;
            var startsThisFrame = 0;

            while (startsThisFrame < MaxStartsPerFrame &&
                   _running < MaxConcurrent &&
                   _queue.Count > 0)
            {
                var pending = _queue.Dequeue();
                _running++;
                startsThisFrame++;
                TotalStarted++;
                BeginExecute(pending);
            }

            LastFrameStarts = startsThisFrame;
        }

        private async void BeginExecute(Pending pending)
        {
            try
            {
                var payload = await pending.Work();
                _running--;
                TotalCompleted++;

                pending.Completion.TrySetResult(payload);
            }
            catch (Exception exception)
            {
                _running--;
                TotalCompleted++;

                pending.Completion.TrySetException(exception);
            }
        }
    }
}
