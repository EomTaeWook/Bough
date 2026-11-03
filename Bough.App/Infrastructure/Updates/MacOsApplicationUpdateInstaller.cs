using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Bough.App.Infrastructure.Models.Updates;

namespace Bough.App.Infrastructure.Updates
{
    public class MacOsApplicationUpdateInstaller : ApplicationUpdateInstaller
    {
        private const string _bundleIdentifier = "io.github.eomtaewook.bough";
        private const long _maximumExpandedBytes = 2L * 1024 * 1024 * 1024;

        public MacOsApplicationUpdateInstaller(ApplicationUpdateEnvironment environment) : base(environment)
        {
        }

        internal override string CandidateFileName { get { return "verified.zip"; } }
        protected override string HelperFileName { get { return "helper"; } }

        public override bool MatchesAssetName(string name, string tag)
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                return name == "Bough-" + tag + "-osx-arm64.app.zip";
            }
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                return name == "Bough-" + tag + "-osx-x64.app.zip";
            }
            return false;
        }

        protected override string GetPlatformBlockedCode()
        {
            if (OperatingSystem.IsMacOS() == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (RuntimeInformation.OSArchitecture != RuntimeInformation.ProcessArchitecture)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
                {
                    return "AppUpdateCoreDeploymentRequired";
                }
            }
            try
            {
                string bundle = GetInstalledBundle(Environment.ExecutablePath);
                ValidateBundle(bundle, Environment.Version);
                return null;
            }
            catch (Exception)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
        }

        protected override async Task PreparePackageAsync(ApplicationUpdateInstallManifest manifest, string directory, CancellationToken cancellationToken)
        {
            if (OperatingSystem.IsMacOS() == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            string installedBundle = GetInstalledBundle(manifest.InstallPath);
            ValidateBundle(installedBundle, Environment.Version);
            manifest.WorkingDirectory = Path.GetDirectoryName(installedBundle);
            ValidateAncestors(manifest.WorkingDirectory);
            // Probe only a new request-owned file; never write to the running bundle.
            string probe = Path.Combine(manifest.WorkingDirectory, ".bough-update-" + Guid.NewGuid().ToString("N") + ".probe");
            using (FileStream writable = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            string archive = Path.Combine(directory, CandidateFileName);
            ValidatePlainFile(archive);
            if (new FileInfo(archive).Length != manifest.CandidateSize)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
            }
            await VerifyHashAsync(archive, manifest.CandidateSha256, cancellationToken).ConfigureAwait(false);
            string candidateBundle = await ExtractBundleAsync(archive, directory, cancellationToken).ConfigureAwait(false);
            ValidateBundle(candidateBundle, manifest.CandidateVersion);
            manifest.InstalledBundleSha256 = await GetBundleHashAsync(installedBundle, cancellationToken).ConfigureAwait(false);
            manifest.PreparedBundleSha256 = await GetBundleHashAsync(candidateBundle, cancellationToken).ConfigureAwait(false);
            SetExecutable(Path.Combine(directory, HelperFileName));
        }

        protected override void ValidatePlatformManifest(ApplicationUpdateInstallManifest manifest)
        {
            if (OperatingSystem.IsMacOS() == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            if (RuntimeInformation.OSArchitecture != RuntimeInformation.ProcessArchitecture)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            string bundle = GetInstalledBundle(manifest.InstallPath);
            if (string.Equals(Path.GetDirectoryName(bundle), manifest.WorkingDirectory, StringComparison.Ordinal) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            ValidateAncestors(manifest.WorkingDirectory);
            if (ApplicationUpdateService.IsSha256(manifest.InstalledBundleSha256) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (ApplicationUpdateService.IsSha256(manifest.PreparedBundleSha256) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            if (ApplicationReleaseVersion.TryParse(manifest.CandidateVersion, out ApplicationReleaseVersion candidate) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (candidate.IsStable == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            ValidateBundle(bundle, Environment.Version);
        }

        protected override async Task InstallAndRestartAsync(ApplicationUpdateInstallManifest manifest, string directory)
        {
            string installedBundle = GetInstalledBundle(manifest.InstallPath);
            string incoming = null;
            string backup = null;
            bool originalMoved = false;
            bool replacementMoved = false;
            try
            {
                ValidatePlatformManifest(manifest);
                await VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                await VerifyBundleHashAsync(installedBundle, manifest.InstalledBundleSha256).ConfigureAwait(false);
                string archive = Path.Combine(directory, CandidateFileName);
                ValidatePlainFile(archive);
                if (new FileInfo(archive).Length != manifest.CandidateSize)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                }
                await VerifyHashAsync(archive, manifest.CandidateSha256, CancellationToken.None).ConfigureAwait(false);
                string preparedBundle = Path.Combine(directory, "Bough.app");
                ValidateBundle(preparedBundle, manifest.CandidateVersion);
                await VerifyBundleHashAsync(preparedBundle, manifest.PreparedBundleSha256).ConfigureAwait(false);
                string unique = Guid.NewGuid().ToString("N");
                incoming = Path.Combine(manifest.WorkingDirectory, ".bough-update-" + unique + ".new");
                backup = Path.Combine(manifest.WorkingDirectory, ".bough-update-" + unique + ".backup");
                CopyBundle(preparedBundle, incoming);
                SetExecutable(Path.Combine(incoming, "Contents", "MacOS", "Bough"));
                ValidateBundle(incoming, manifest.CandidateVersion);
                await VerifyBundleHashAsync(incoming, manifest.PreparedBundleSha256).ConfigureAwait(false);
                // Both renames stay in the installation's filesystem. Never overwrite a foreign path.
                await VerifyBundleHashAsync(installedBundle, manifest.InstalledBundleSha256).ConfigureAwait(false);
                Directory.Move(installedBundle, backup);
                originalMoved = true;
                Directory.Move(incoming, installedBundle);
                replacementMoved = true;
                StartInstalled(manifest, directory, null);
                try
                {
                    await VerifyBundleHashAsync(backup, manifest.InstalledBundleSha256).ConfigureAwait(false);
                    DeletePlainDirectoryTree(backup);
                    backup = null;
                }
                catch (Exception)
                {
                    // A cleanup failure retains the old backup; it is not a failed replacement.
                }
            }
            catch (Exception exception)
            {
                string failureCode = "AppUpdateCoreReplaceFailed";
                Exception failure = exception;
                if (originalMoved == true)
                {
                    if (replacementMoved == true)
                    {
                        failureCode = "AppUpdateLaunchFailed";
                    }
                    try
                    {
                        if (replacementMoved == true)
                        {
                            await VerifyBundleHashAsync(installedBundle, manifest.PreparedBundleSha256).ConfigureAwait(false);
                            Directory.Move(installedBundle, incoming);
                        }
                        Directory.Move(backup, installedBundle);
                        backup = null;
                        await VerifyBundleHashAsync(installedBundle, manifest.InstalledBundleSha256).ConfigureAwait(false);
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
                        await VerifyBundleHashAsync(installedBundle, manifest.InstalledBundleSha256).ConfigureAwait(false);
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
                    try
                    {
                        if (Directory.Exists(incoming) == true)
                        {
                            DeletePlainDirectoryTree(incoming);
                        }
                    }
                    catch (Exception)
                    {
                        // Preserve only this request's staging files when cleanup is unavailable.
                    }
                }
            }
        }

        private static string GetInstalledBundle(string executable)
        {
            string full = Path.GetFullPath(executable);
            if (Path.GetFileName(full) != "Bough")
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            string executableDirectory = Path.GetDirectoryName(full);
            if (Path.GetFileName(executableDirectory) != "MacOS")
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            string contents = Path.GetDirectoryName(executableDirectory);
            if (Path.GetFileName(contents) != "Contents")
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            string bundle = Path.GetDirectoryName(contents);
            if (Path.GetFileName(bundle) != "Bough.app")
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            ValidateAncestors(bundle);
            return bundle;
        }

        private static void ValidateAncestors(string directory)
        {
            string current = directory;
            while (string.IsNullOrEmpty(current) == false)
            {
                ValidateNoLink(current);
                current = Path.GetDirectoryName(current);
            }
        }

        private static async Task<string> ExtractBundleAsync(string archivePath, string directory, CancellationToken cancellationToken)
        {
            string bundle = Path.Combine(directory, "Bough.app");
            using FileStream source = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using ZipArchive archive = new(source, ZipArchiveMode.Read, false);
            if (archive.Entries.Count > 8192)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            long expandedBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = entry.FullName;
                if (name.Length > 1024)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                if (name.Contains('\\') == true)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                string[] components = name.TrimEnd('/').Split('/');
                if (components[0] != "Bough.app")
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                foreach (string component in components)
                {
                    if (component.Length == 0)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                    if (component == ".")
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                    if (component == "..")
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                    if (component.IndexOf('\0') >= 0)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                }
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                bool directoryEntry = name.EndsWith("/", StringComparison.Ordinal);
                if (unixType != 0)
                {
                    int expectedType = 0x8000;
                    if (directoryEntry == true)
                    {
                        expectedType = 0x4000;
                    }
                    if (unixType != expectedType)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                }
                if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                string target = Path.GetFullPath(Path.Combine(directory, name));
                if (target != bundle)
                {
                    if (target.StartsWith(bundle + Path.DirectorySeparatorChar, StringComparison.Ordinal) == false)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                }
                string parent = directory;
                int parentCount = components.Length - 1;
                if (directoryEntry == true)
                {
                    parentCount = components.Length;
                }
                for (int componentIndex = 0; componentIndex < parentCount; componentIndex++)
                {
                    parent = Path.Combine(parent, components[componentIndex]);
                    if (Path.Exists(parent) == true)
                    {
                        ValidateNoLink(parent);
                    }
                    Directory.CreateDirectory(parent);
                    ValidateNoLink(parent);
                }
                if (directoryEntry == true)
                {
                    continue;
                }
                if (entry.Length < 0)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                if (entry.Length > _maximumExpandedBytes)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                expandedBytes += entry.Length;
                if (expandedBytes > _maximumExpandedBytes)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                using Stream compressed = entry.Open();
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await compressed.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > entry.Length)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                if (written != entry.Length)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                }
            }
            SetExecutable(Path.Combine(bundle, "Contents", "MacOS", "Bough"));
            return bundle;
        }
        private static void ValidateBundle(string bundle, string expectedVersion)
        {
            ValidateNoLink(bundle);
            string contents = Path.Combine(bundle, "Contents");
            ValidateNoLink(contents);
            string executableDirectory = Path.Combine(contents, "MacOS");
            ValidateNoLink(executableDirectory);
            string resources = Path.Combine(contents, "Resources");
            ValidateNoLink(resources);
            string executable = Path.Combine(executableDirectory, "Bough");
            ValidatePlainFile(executable);
            ValidatePlainFile(Path.Combine(resources, "AppIcon.icns"));
            string plistPath = Path.Combine(contents, "Info.plist");
            ValidatePlainFile(plistPath);
            if (new FileInfo(plistPath).Length > 65536)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            XmlReaderSettings settings = new();
            settings.DtdProcessing = DtdProcessing.Ignore;
            settings.XmlResolver = null;
            using XmlReader reader = XmlReader.Create(plistPath, settings);
            XDocument plist = XDocument.Load(reader);
            XElement dictionary = plist.Root?.Element("dict");
            if (dictionary == null)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            string key = null;
            foreach (XElement element in dictionary.Elements())
            {
                if (key == null)
                {
                    if (element.Name.LocalName != "key")
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                    key = element.Value;
                    continue;
                }
                if (values.TryAdd(key, element.Value) == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                key = null;
            }
            if (key != null)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (values.GetValueOrDefault("CFBundleIdentifier") != _bundleIdentifier)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (values.GetValueOrDefault("CFBundleExecutable") != "Bough")
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (values.GetValueOrDefault("CFBundlePackageType") != "APPL")
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (ApplicationReleaseVersion.TryParse(expectedVersion, out ApplicationReleaseVersion version) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (values.GetValueOrDefault("CFBundleShortVersionString") != version.Text)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (values.GetValueOrDefault("CFBundleVersion") != version.Text)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (OperatingSystem.IsMacOS() == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            if (OperatingSystem.IsWindows() == true)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            if ((File.GetUnixFileMode(executable) & UnixFileMode.UserExecute) == 0)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            using FileStream binary = File.OpenRead(executable);
            byte[] header = new byte[16];
            binary.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4)) != 0xFEEDFACF)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            uint expectedCpu = 0x01000007;
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                expectedCpu = 0x0100000C;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)) != expectedCpu)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4)) != 2)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
        }

        private static void SetExecutable(string path)
        {
            if (OperatingSystem.IsMacOS() == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            ValidatePlainFile(path);
            if (OperatingSystem.IsWindows() == true)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDeploymentRequired", null, Array.Empty<object>());
            }
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        private static async Task VerifyBundleHashAsync(string bundle, string expected)
        {
            string actual = await GetBundleHashAsync(bundle, CancellationToken.None).ConfigureAwait(false);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreDigestMismatch", null, Array.Empty<object>());
            }
        }

        private static async Task<string> GetBundleHashAsync(string bundle, CancellationToken cancellationToken)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await AppendDirectoryHashAsync(bundle, bundle, hash, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private static async Task AppendDirectoryHashAsync(string root, string directory, IncrementalHash hash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateNoLink(directory);
            hash.AppendData(Encoding.UTF8.GetBytes("D:" + Path.GetRelativePath(root, directory) + "\0"));
            string[] files = Directory.GetFiles(directory);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidatePlainFile(file);
                hash.AppendData(Encoding.UTF8.GetBytes("F:" + Path.GetRelativePath(root, file) + "\0"));
                using FileStream content = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                hash.AppendData(await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false));
            }
            string[] children = Directory.GetDirectories(directory);
            Array.Sort(children, StringComparer.Ordinal);
            foreach (string child in children)
            {
                await AppendDirectoryHashAsync(root, child, hash, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void CopyBundle(string source, string target)
        {
            ValidateNoLink(source);
            if (Path.Exists(target) == true)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            Directory.CreateDirectory(target);
            ValidateNoLink(target);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                ValidatePlainFile(file);
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
            }
            foreach (string child in Directory.EnumerateDirectories(source))
            {
                CopyBundle(child, Path.Combine(target, Path.GetFileName(child)));
            }
        }
    }
}
