using Game.AssetSystem.Patch;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// Result of one download-and-stage run. Same numbers the patch pipeline
    /// produced before, now owned by the framework instead of a demo so a game
    /// can show them without referencing demo code.
    /// </summary>
    public sealed class PatchDownloadReport
    {
        public string Version;
        public PatchApplyStatus Status;
        public string Message;
        public int Committed;
        public int Skipped;
        public long ResumeBytes;
        public long TransferredBytes;
        public long TotalBytes;

        public bool Success
        {
            get { return Status == PatchApplyStatus.Succeeded; }
        }

        public override string ToString()
        {
            return "status=" + Status +
                   " committed=" + Committed +
                   " skipped=" + Skipped +
                   " resume=" + ResumeBytes +
                   " transferred=" + TransferredBytes + "/" + TotalBytes;
        }
    }
}
