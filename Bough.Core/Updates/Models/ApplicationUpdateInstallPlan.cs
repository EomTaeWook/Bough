using System.Diagnostics;

namespace Bough.Core.Updates.Models
{
    public class ApplicationUpdateInstallPlan
    {
        internal ApplicationUpdateInstallPlan(string directory, string nonce, VerifiedApplicationUpdate download)
        {
            Directory = directory;
            Nonce = nonce;
            Download = download;
        }

        internal VerifiedApplicationUpdate Download { get; }
        internal string Directory { get; }
        internal string Nonce { get; }
        internal Process Helper { get; set; }
    }
}