using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Updates.Models;

namespace Bough.Core.Updates
{
    public class ApplicationUpdateInstaller
    {
        private static readonly string[] _ownedNames = { "download.partial", "verified.exe", "helper.exe", "install.json", "ready", "authorized", "cancel" };
        private static readonly string _updateRoot = Path.Combine(Path.GetTempPath(), "Bough", "application-update");
        private readonly ApplicationUpdateService _updates;

        public ApplicationUpdateInstaller(ApplicationUpdateService updates)
        {
            _updates = updates;
        }

        public Task<ApplicationUpdateInstallPlan> PrepareAsync(VerifiedApplicationUpdate update, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            return Task.Run(async () =>
            {
                string directory = null;
                try
                {
                    _updates.ValidateInstallEnvironment();
                    ValidateOwnedDirectory(update.Directory);
                    ValidatePlainFile(update.FilePath);
                    await VerifyHashAsync(update.FilePath, update.Release.Sha256, cancellationToken).ConfigureAwait(false);
                    if (new FileInfo(update.FilePath).Length != update.Release.Size)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                    }
                    string installed = Path.GetFullPath(_updates.Environment.ExecutablePath);
                    ValidatePlainFile(installed);
                    using Process original = Process.GetCurrentProcess();
                    string installedHash = await GetHashAsync(installed, cancellationToken).ConfigureAwait(false);
                    directory = CreateOwnedDirectory();
                    string candidate = Path.Combine(directory, "verified.exe");
                    File.Copy(update.FilePath, candidate, false);
                    File.Copy(installed, Path.Combine(directory, "helper.exe"), false);
                    cancellationToken.ThrowIfCancellationRequested();
                    InstallManifest manifest = new();
                    manifest.OriginalPid = original.Id;
                    manifest.OriginalStartTicks = original.StartTime.ToUniversalTime().Ticks;
                    manifest.InstallPath = installed;
                    manifest.InstalledSha256 = installedHash;
                    manifest.CandidateSha256 = update.Release.Sha256;
                    manifest.CandidateSize = update.Release.Size;
                    manifest.Nonce = Guid.NewGuid().ToString("N");
                    manifest.WorkingDirectory = Path.GetDirectoryName(installed);
                    await File.WriteAllTextAsync(Path.Combine(directory, "install.json"), JsonSerializer.Serialize(manifest), cancellationToken).ConfigureAwait(false);
                    return new ApplicationUpdateInstallPlan(directory, manifest.Nonce, update);
                }
                catch (OperationCanceledException)
                {
                    if (directory != null)
                    {
                        DeleteOwnedDirectory(directory);
                    }
                    throw;
                }
                catch (Exception exception)
                {
                    if (directory != null)
                    {
                        DeleteOwnedDirectory(directory);
                    }
                    if (exception is ApplicationUpdateException)
                    {
                        throw;
                    }
                    throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", exception, Array.Empty<object>()) { ReleasePage = update.Release.ReleasePage };
                }
            }, cancellationToken);
        }

        public Task StartHelperAsync(ApplicationUpdateInstallPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    ValidateOwnedDirectory(plan.Directory);
                    cancellationToken.ThrowIfCancellationRequested();
                    ProcessStartInfo start = new(Path.Combine(plan.Directory, "helper.exe"));
                    start.UseShellExecute = false;
                    start.CreateNoWindow = true;
                    start.WorkingDirectory = plan.Directory;
                    start.ArgumentList.Add("--bough-update-helper");
                    start.ArgumentList.Add(Path.Combine(plan.Directory, "install.json"));
                    plan.Helper = Process.Start(start);
                    if (plan.Helper == null)
                    {
                        throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
                    }
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    while (File.Exists(Path.Combine(plan.Directory, "ready")) == false)
                    {
                        if (plan.Helper.HasExited)
                        {
                            throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
                        }
                        await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
                }
                catch (ApplicationUpdateException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", exception, Array.Empty<object>());
                }
            }, cancellationToken);
        }

        // Called after Avalonia's normal desktop lifetime returns, never while the UI is closing.
        public Task AuthorizeAfterShutdownAsync(ApplicationUpdateInstallPlan plan)
        {
            return Task.Run(() =>
            {
                ValidateOwnedDirectory(plan.Directory);
                File.WriteAllText(Path.Combine(plan.Directory, "authorized"), plan.Nonce);
                // The helper owns its separately verified copy. Keep the UI download on declined
                // shutdown, but remove it after the accepted handoff so it is not orphaned.
                try
                {
                    DeleteOwnedDownload(plan.Download);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            });
        }

        public Task CancelAsync(ApplicationUpdateInstallPlan plan)
        {
            return Task.Run(async () =>
            {
                if (plan == null)
                {
                    return;
                }
                try
                {
                    ValidateOwnedDirectory(plan.Directory);
                    File.WriteAllText(Path.Combine(plan.Directory, "cancel"), string.Empty);
                    if (plan.Helper != null)
                    {
                        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
                        await plan.Helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    DeleteOwnedDirectory(plan.Directory);
                }
                catch (IOException)
                {
                    // Only this request's temporary files may be left; never touch the installation.
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (OperationCanceledException)
                {
                    // Do not kill a helper or the original application to finish cleanup.
                }
                finally
                {
                    plan.Helper?.Dispose();
                }
            });
        }

        public static async Task<int> RunHelperAsync(string manifestPath)
        {
            string directory = null;
            InstallManifest manifest = null;
            string backup = null;
            string incoming = null;
            bool replaced = false;
            bool canRestart = false;
            string failureCode = null;
            try
            {
                directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
                ValidateOwnedDirectory(directory);
                if (Path.GetFileName(manifestPath) != "install.json")
                {
                    return 1;
                }
                string helperPath = Path.Combine(directory, "helper.exe");
                if (string.Equals(Environment.ProcessPath, helperPath, StringComparison.OrdinalIgnoreCase) == false)
                {
                    return 1;
                }
                ValidatePlainFile(manifestPath);
                if (new FileInfo(manifestPath).Length > 16384)
                {
                    return 1;
                }
                manifest = JsonSerializer.Deserialize<InstallManifest>(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false));
                ValidateManifest(manifest, directory);
                await VerifyHashAsync(helperPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                using Process original = Process.GetProcessById(manifest.OriginalPid);
                if (original.StartTime.ToUniversalTime().Ticks != manifest.OriginalStartTicks)
                {
                    return 1;
                }
                if (original.MainModule == null)
                {
                    return 1;
                }
                if (string.Equals(original.MainModule.FileName, manifest.InstallPath, StringComparison.OrdinalIgnoreCase) == false)
                {
                    return 1;
                }
                ValidatePlainFile(manifest.InstallPath);
                await VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(directory, "ready"), string.Empty);
                DateTime deadline = DateTime.UtcNow.AddMinutes(10);
                while (original.HasExited == false)
                {
                    if (File.Exists(Path.Combine(directory, "cancel")))
                    {
                        return 0;
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        return 1;
                    }
                    await Task.Delay(100).ConfigureAwait(false);
                }
                string authorization = Path.Combine(directory, "authorized");
                if (File.Exists(authorization) == false)
                {
                    return 0;
                }
                if (await File.ReadAllTextAsync(authorization).ConfigureAwait(false) != manifest.Nonce)
                {
                    return 1;
                }
                if (File.Exists(Path.Combine(directory, "cancel")))
                {
                    return 0;
                }
                canRestart = true;
                // Fixed target must still be the original executable, not a replaced path or link.
                ValidatePlainFile(manifest.InstallPath);
                await VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                string candidate = Path.Combine(directory, "verified.exe");
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
                // Keep the original until launching the replacement has succeeded.
                TryDeleteFile(backup);
                backup = null;
                return 0;
            }
            catch (Exception)
            {
                failureCode = "AppUpdateCoreReplaceFailed";
                if (replaced)
                {
                    failureCode = "AppUpdateLaunchFailed";
                    try
                    {
                        File.Replace(backup, manifest.InstallPath, null);
                        backup = null;
                    }
                    catch (Exception)
                    {
                        // Retain the original backup if the OS prevents restoration.
                        failureCode = "AppUpdateCoreRollbackFailed";
                    }
                }
                if (canRestart)
                {
                    if (failureCode != "AppUpdateCoreRollbackFailed")
                    {
                        try
                        {
                            ValidatePlainFile(manifest.InstallPath);
                            await VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                            StartInstalled(manifest, directory, failureCode);
                        }
                        catch (Exception)
                        {
                            // No arbitrary alternate executable, elevation, or reboot scheduling.
                        }
                    }
                }
                return 1;
            }
            finally
            {
                if (incoming != null)
                {
                    TryDeleteFile(incoming);
                }
                // The running helper cannot delete itself on Windows. The relaunched app removes
                // this fixed request directory after the helper PID exits; no shared temp sweep.
            }
        }

        public static Task CleanupAfterHelperAsync(string directory, int helperPid)
        {
            return Task.Run(async () =>
            {
                try
                {
                    ValidateOwnedDirectory(directory);
                    if (helperPid == Environment.ProcessId)
                    {
                        return;
                    }
                    try
                    {
                        using Process helper = Process.GetProcessById(helperPid);
                        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
                        await helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (ArgumentException)
                    {
                        // The helper has already exited.
                    }
                    DeleteOwnedDirectory(directory);
                }
                catch (Exception)
                {
                    // Best-effort removal of this specific updater directory, without logging paths.
                }
            });
        }

        internal static string CreateOwnedDirectory()
        {
            Directory.CreateDirectory(_updateRoot);
            ValidateNoLink(_updateRoot);
            ValidateNoLink(Path.GetDirectoryName(_updateRoot));
            string directory = Path.Combine(_updateRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            ValidateOwnedDirectory(directory);
            return directory;
        }

        internal static void DeleteOwnedDownload(VerifiedApplicationUpdate update)
        {
            DeleteOwnedDirectory(update.Directory);
        }

        internal static void DeleteOwnedDirectory(string directory)
        {
            if (Directory.Exists(directory) == false)
            {
                return;
            }
            ValidateOwnedDirectory(directory);
            foreach (string name in _ownedNames)
            {
                TryDeleteFile(Path.Combine(directory, name));
            }
            try
            {
                Directory.Delete(directory, false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        internal static async Task VerifyHashAsync(string path, string expected, CancellationToken cancellationToken)
        {
            string hash = await GetHashAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDigestMismatch", null, Array.Empty<object>());
            }
        }

        private static async Task<string> GetHashAsync(string path, CancellationToken cancellationToken)
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash);
        }

        private static void ValidateOwnedDirectory(string directory)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string parent = Path.GetDirectoryName(full);
            if (string.Equals(parent, _updateRoot, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (Guid.TryParseExact(Path.GetFileName(full), "N", out Guid _) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            ValidateNoLink(full);
            ValidateNoLink(_updateRoot);
        }

        private static void ValidatePlainFile(string path)
        {
            if (File.Exists(path) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            ValidateNoLink(path);
            ValidateNoLink(Path.GetDirectoryName(path));
        }

        private static void ValidateNoLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
        }

        private static void ValidateManifest(InstallManifest manifest, string directory)
        {
            if (manifest == null)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (manifest.OriginalPid <= 0)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (manifest.OriginalPid == Environment.ProcessId)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (Path.IsPathFullyQualified(manifest.InstallPath) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (Path.GetExtension(manifest.InstallPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (string.Equals(Path.GetDirectoryName(manifest.InstallPath), manifest.WorkingDirectory, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (manifest.InstallPath.StartsWith(_updateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (Guid.TryParseExact(manifest.Nonce, "N", out Guid _) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (ApplicationUpdateService.IsSha256(manifest.InstalledSha256) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (ApplicationUpdateService.IsSha256(manifest.CandidateSha256) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (manifest.CandidateSize <= 0)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            ValidatePlainFile(Path.Combine(directory, "verified.exe"));
        }

        private static void StartInstalled(InstallManifest manifest, string directory, string failureCode)
        {
            ProcessStartInfo start = new(manifest.InstallPath);
            start.UseShellExecute = false;
            start.WorkingDirectory = manifest.WorkingDirectory;
            start.ArgumentList.Add("--bough-update-cleanup");
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (failureCode != null)
            {
                start.ArgumentList.Add(failureCode);
            }
            using Process relaunched = Process.Start(start);
            if (relaunched == null)
            {
                throw new ApplicationUpdateException("AppUpdateLaunchFailed", null, Array.Empty<object>());
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private class InstallManifest
        {
            public InstallManifest()
            {
            }

            public int OriginalPid { get; set; }
            public long OriginalStartTicks { get; set; }
            public string InstallPath { get; set; }
            public string WorkingDirectory { get; set; }
            public string InstalledSha256 { get; set; }
            public string CandidateSha256 { get; set; }
            public long CandidateSize { get; set; }
            public string Nonce { get; set; }
        }
    }
}