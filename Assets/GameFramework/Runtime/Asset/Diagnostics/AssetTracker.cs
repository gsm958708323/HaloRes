using System.Collections.Generic;
using System.Text;

namespace GameFramework.Asset.Diagnostics
{
    public sealed class AssetTracker
    {
        private readonly AssetSystem _system;
        internal AssetTracker(AssetSystem system) { _system = system; }
        public AssetSystemSnapshot Capture() => _system.Capture();

        public string CreateLeakReport()
        {
            var snapshot = Capture();
            var report = new StringBuilder();
            foreach (var asset in snapshot.Assets)
            {
                report.Append(asset.Address).Append(" ref=").Append(asset.ReferenceCount).Append(" ").Append(asset.State);
                foreach (var owner in asset.Owners) report.Append(" | ").Append(owner);
                if (!string.IsNullOrEmpty(asset.Error)) report.Append(" | ").Append(asset.Error);
                report.AppendLine();
            }
            foreach (var bundle in snapshot.Bundles)
                report.Append(bundle.Name).Append(" ref=").Append(bundle.ReferenceCount).Append(" ").Append(bundle.State).AppendLine();
            return report.Length == 0 ? "No live asset or bundle handles." : report.ToString();
        }
    }
}
