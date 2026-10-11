namespace Bough.App.Infrastructure.Models.Updates
{
    public class ApplicationUpdateCheck
    {
        internal ApplicationUpdateCheck(string currentVersion, ApplicationUpdateRelease release, bool hasUpdate, string installBlockedCode)
        {
            CurrentVersion = currentVersion;
            Release = release;
            HasUpdate = hasUpdate;
            InstallBlockedCode = installBlockedCode;
        }

        public string CurrentVersion { get; }
        public ApplicationUpdateRelease Release { get; }
        public bool HasStableRelease { get { return Release != null; } }
        public bool HasUpdate { get; }
        public string InstallBlockedCode { get; }
        public bool CanInstall { get { return HasUpdate && InstallBlockedCode == null; } }
    }
}
