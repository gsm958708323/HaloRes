namespace Game.AssetSystem.Scheduling
{
    public enum AssetLoadPriority
    {
        Low = 0,
        Normal = 1,
        High = 2
    }

    public enum ScheduledLoadStatus
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Canceled
    }

    public enum ScheduledLoadErrorCode
    {
        None,
        Canceled,
        ScopeDisposed,
        WorkerFailed,
        InternalError
    }

    public readonly struct ScheduledLoadError
    {
        public ScheduledLoadErrorCode Code { get; }
        public string Message { get; }

        public ScheduledLoadError(
            ScheduledLoadErrorCode code,
            string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }

    public readonly struct ScheduledLoadResult<T>
    {
        public ScheduledLoadStatus Status { get; }
        public T Value { get; }
        public ScheduledLoadError Error { get; }
        public string Key { get; }
        public AssetLoadPriority Priority { get; }

        public ScheduledLoadResult(
            ScheduledLoadStatus status,
            T value,
            ScheduledLoadError error,
            string key,
            AssetLoadPriority priority)
        {
            Status = status;
            Value = value;
            Error = error;
            Key = key;
            Priority = priority;
        }

        public bool IsSuccess
        {
            get { return Status == ScheduledLoadStatus.Succeeded; }
        }
    }
}
