using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Infrastructure.Models.Updates;

namespace Bough.App.Infrastructure.Updates
{
    public class WindowsApplicationUpdateInstaller : ApplicationUpdateInstaller
    {
        public WindowsApplicationUpdateInstaller(ApplicationUpdateEnvironment environment) : base(environment)
        {
        }

        internal override string CandidateFileName { get { return "verified.exe"; } }
        protected override string HelperFileName { get { return "helper.exe"; } }

        public override bool MatchesAssetName(string name, string tag)
        {
            if (name == "Bough-" + tag + "-win-x64.exe")
            {
                return true;
            }
            return name == "Bough.exe";
        }

        protected override string GetPlatformBlockedCode()
        {
            if (OperatingSystem.IsWindows() == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (Path.GetExtension(Environment.ExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            return null;
        }

        protected override void ValidatePlatformManifest(ApplicationUpdateInstallManifest manifest)
        {
            if (Path.GetExtension(manifest.InstallPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (string.Equals(Path.GetDirectoryName(manifest.InstallPath), manifest.WorkingDirectory, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
        }

        protected override async Task InstallAndRestartAsync(ApplicationUpdateInstallManifest manifest, string directory)
        {
            string backup = null;
            string incoming = null;
            bool replaced = false;
            try
            {
                ValidatePlainFile(manifest.InstallPath);
                await VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                string candidate = Path.Combine(directory, CandidateFileName);
                ValidatePlainFile(candidate);
                if (new FileInfo(candidate).Length != manifest.CandidateSize)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                }
                await VerifyHashAsync(candidate, manifest.CandidateSha256, CancellationToken.None).ConfigureAwait(false);
                string unique = Guid.NewGuid().ToString("N");
                incoming = Path.Combine(manifest.WorkingDirectory, ".bough-update-" + unique + ".new");
                backup = Path.Combine(manifest.WorkingDirectory, ".bough-update-" + unique + ".backup");
                File.Copy(candidate, incoming, false);
                await VerifyHashAsync(incoming, manifest.CandidateSha256, CancellationToken.None).ConfigureAwait(false);
                File.Replace(incoming, manifest.InstallPath, backup);
                replaced = true;
                StartInstalled(manifest, directory, null);
                TryDeleteFile(backup);
                backup = null;
            }
            catch (Exception exception)
            {
                string failureCode = "AppUpdateCoreReplaceFailed";
                Exception failure = exception;
                if (replaced == true)
                {
                    failureCode = "AppUpdateLaunchFailed";
                    try
                    {
                        File.Replace(backup, manifest.InstallPath, null);
                        backup = null;
                    }
                    catch (Exception restorationException)
                    {
                        failureCode = "AppUpdateCoreRollbackFailed";
                        failure = new AggregateException(exception, restorationException);
                    }
                }
                if (failureCode != "AppUpdateCoreRollbackFailed")
                {
                    try
                    {
                        ValidatePlainFile(manifest.InstallPath);
                        await VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                        StartInstalled(manifest, directory, failureCode);
                    }
                    catch (Exception restartException)
                    {
                        failure = new AggregateException(failure, restartException);
                    }
                }
                throw new ApplicationUpdateException(failureCode, failure, Array.Empty<object>());
            }
            finally
            {
                if (incoming != null)
                {
                    TryDeleteFile(incoming);
                }
            }
        }
    }
}
