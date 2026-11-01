namespace GameUI.Runtime
{
    internal interface IUITransition
    {
        void Play();
        bool IsDone { get; }
        void Finish();
        void Stop();
    }

    internal sealed class UIInstantTransition : IUITransition
    {
        public bool IsDone { get; private set; } = true;
        public void Play() { IsDone = true; }
        public void Finish() { IsDone = true; }
        public void Stop() { IsDone = true; }
    }
}
