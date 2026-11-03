using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Infrastructure.Models.Updates;

namespace Bough.App.Infrastructure.Updates
{
    public abstract class ApplicationUpdateInstaller
    {
        private static readonly string[] _ownedNames = { "download.partial", "verified.exe", "verified.zip", "helper.exe", "helper", "install.json", "ready", "authorized", "cancel" };
        private static readonly string _updateRoot = Path.Combine(Path.GetTempPath(), "Bough", "application-update");
        private readonly ApplicationUpdateEnvironment _environment;

        protected ApplicationUpdateInstaller(ApplicationUpdateEnvironment environment)
        {
            _environment = environment;
        }

        internal abstract string CandidateFileName { get; }
        protected abstract string HelperFileName { get; }
        protected ApplicationUpdateEnvironment Environment { get { return _environment; } }
        public abstract bool MatchesAssetName(string name, string tag);
        protected abstract string GetPlatformBlockedCode();
        protected abstract void ValidatePlatformManifest(ApplicationUpdateInstallManifest manifest);
        protected abstract Task InstallAndRestartAsync(ApplicationUpdateInstallManifest manifest, string directory);

        protected virtual Task PreparePackageAsync(ApplicationUpdateInstallManifest manifest, string directory, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        internal string GetInstallBlockedCode()
        {
            if (_environment.PublishedSingleFile == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (Debugger.IsAttached == true)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            string path = _environment.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (Path.IsPathFullyQualified(path) == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            string directory = Path.GetDirectoryName(path);
            if (File.Exists(Path.Combine(directory, "Bough.dll")) == true)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (File.Exists(Path.Combine(directory, "Bough.deps.json")) == true)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            return GetPlatformBlockedCode();
        }
        public Task<ApplicationUpdateInstallPlan> PrepareAsync(VerifiedApplicationUpdate update, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            return Task.Run(async () =>
            {
                string directory = null;
                try
                {
                    string blocked = GetInstallBlockedCode();
                    if (blocked != null)
                    {
                        throw new ApplicationUpdateException(blocked, null, Array.Empty<object>());
                    }
                    ValidateOwnedDirectory(update.Directory);
                    ValidatePlainFile(update.FilePath);
                    await VerifyHashAsync(update.FilePath, update.Release.Sha256, cancellationToken).ConfigureAwait(false);
                    if (new FileInfo(update.FilePath).Length != update.Release.Size)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                    }
                    string installed = Path.GetFullPath(_environment.ExecutablePath);
                    ValidatePlainFile(installed);
                    using Process original = Process.GetCurrentProcess();
                    string installedHash = await GetHashAsync(installed, cancellationToken).ConfigureAwait(false);
                    directory = CreateOwnedDirectory();
                    string candidate = Path.Combine(directory, CandidateFileName);
                    File.Copy(update.FilePath, candidate, false);
                    File.Copy(installed, Path.Combine(directory, HelperFileName), false);
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplicationUpdateInstallManifest manifest = new();
                    manifest.OriginalPid = original.Id;
                    manifest.OriginalStartTicks = original.StartTime.ToUniversalTime().Ticks;
                    manifest.InstallPath = installed;
                    manifest.InstalledSha256 = installedHash;
                    manifest.CandidateSha256 = update.Release.Sha256;
                    manifest.CandidateSize = update.Release.Size;
                    manifest.Nonce = Guid.NewGuid().ToString("N");
                    manifest.WorkingDirectory = Path.GetDirectoryName(installed);
                    manifest.CandidateVersion = update.Release.Version;
                    await PreparePackageAsync(manifest, directory, cancellationToken).ConfigureAwait(false);
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
                    ProcessStartInfo start = new(Path.Combine(plan.Directory, HelperFileName));
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
                catch (OperationCanceledException exception)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", exception, Array.Empty<object>());
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

        public async Task<int> RunHelperAsync(string manifestPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
                ValidateOwnedDirectory(directory);
                if (Path.GetFileName(manifestPath) != "install.json")
                {
                    return 1;
                }
                string helperPath = Path.Combine(directory, HelperFileName);
                StringComparison pathComparison = StringComparison.Ordinal;
                if (OperatingSystem.IsWindows() == true)
                {
                    pathComparison = StringComparison.OrdinalIgnoreCase;
                }
                if (string.Equals(System.Environment.ProcessPath, helperPath, pathComparison) == false)
                {
                    return 1;
                }
                ValidatePlainFile(manifestPath);
                if (new FileInfo(manifestPath).Length > 16384)
                {
                    return 1;
                }
                ApplicationUpdateInstallManifest manifest = JsonSerializer.Deserialize<ApplicationUpdateInstallManifest>(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false));
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
                if (string.Equals(original.MainModule.FileName, manifest.InstallPath, pathComparison) == false)
                {
                    return 1;
                }
                ValidatePlainFile(manifest.InstallPath);
                await VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(directory, "ready"), string.Empty);
                DateTime deadline = DateTime.UtcNow.AddMinutes(10);
                while (original.HasExited == false)
                {
                    if (File.Exists(Path.Combine(directory, "cancel")) == true)
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
                if (File.Exists(Path.Combine(directory, "cancel")) == true)
                {
                    return 0;
                }
                await InstallAndRestartAsync(manifest, directory).ConfigureAwait(false);
                return 0;
            }
            catch (Exception)
            {
                // Never expose updater paths/diagnostics through the global logger.
                return 1;
            }
        }
        public Task CleanupAfterHelperAsync(string directory, int helperPid)
        {
            return Task.Run(async () =>
            {
                try
                {
                    ValidateOwnedDirectory(directory);
                    if (helperPid == System.Environment.ProcessId)
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
            string bundle = Path.Combine(directory, "Bough.app");
            if (Directory.Exists(bundle) == true)
            {
                try
                {
                    DeletePlainDirectoryTree(bundle);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (ApplicationUpdateException)
                {
                    // Do not follow a replaced link just to complete request cleanup.
                }
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

        protected static void DeletePlainDirectoryTree(string directory)
        {
            ValidateNoLink(directory);
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                ValidateNoLink(file);
                File.Delete(file);
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                DeletePlainDirectoryTree(child);
            }
            Directory.Delete(directory, false);
        }
        internal static async Task VerifyHashAsync(string path, string expected, CancellationToken cancellationToken)
        {
            string hash = await GetHashAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDigestMismatch", null, Array.Empty<object>());
            }
        }

        protected static async Task<string> GetHashAsync(string path, CancellationToken cancellationToken)
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

        protected static void ValidatePlainFile(string path)
        {
            if (File.Exists(path) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            ValidateNoLink(path);
            ValidateNoLink(Path.GetDirectoryName(path));
        }

        protected static void ValidateNoLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
        }

        private void ValidateManifest(ApplicationUpdateInstallManifest manifest, string directory)
        {
            if (manifest == null)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (manifest.OriginalPid <= 0)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (manifest.OriginalPid == System.Environment.ProcessId)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (Path.IsPathFullyQualified(manifest.InstallPath) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (string.IsNullOrWhiteSpace(manifest.CandidateVersion) == true)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            ValidatePlatformManifest(manifest);
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
            ValidatePlainFile(Path.Combine(directory, CandidateFileName));
        }

        protected static void StartInstalled(ApplicationUpdateInstallManifest manifest, string directory, string failureCode)
        {
            ProcessStartInfo start = new(manifest.InstallPath);
            start.UseShellExecute = false;
            start.WorkingDirectory = manifest.WorkingDirectory;
            start.ArgumentList.Add("--bough-update-cleanup");
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add(System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

        protected static void TryDeleteFile(string path)
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

    }
}
