using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dignus.DependencyInjection.Attributes;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class GitCloneService
    {
        private static readonly Regex _scpRemotePattern = new(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[^\s]+$", RegexOptions.Compiled);
        private static readonly Regex _percentagePattern = new(@"(?<!\d)(\d{1,3})%", RegexOptions.Compiled);
        private static readonly HashSet<string> _reservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        private readonly GitCommandRunner _runner;

        public GitCloneService(GitCommandRunner runner)
        {
            _runner = runner;
        }

        public string ValidateDestination(string remote, string parentPath, string folderName)
        {
            NormalizeRemote(remote);
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                throw new GitException("CloneParentRequired", null, Array.Empty<object>());
            }
            string parent;
            try
            {
                parent = Path.GetFullPath(parentPath.Trim());
            }
            catch (ArgumentException exception)
            {
                throw new GitException("CloneParentMissing", exception, Array.Empty<object>());
            }
            catch (NotSupportedException exception)
            {
                throw new GitException("CloneParentMissing", exception, Array.Empty<object>());
            }
            catch (PathTooLongException exception)
            {
                throw new GitException("CloneParentMissing", exception, Array.Empty<object>());
            }
            if (Directory.Exists(parent) == false)
            {
                throw new GitException("CloneParentMissing", null, Array.Empty<object>());
            }
            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new GitException("CloneFolderRequired", null, Array.Empty<object>());
            }
            string name = folderName.Trim();
            if (name == ".")
            {
                throw new GitException("CloneFolderInvalid", null, Array.Empty<object>());
            }
            if (name == "..")
            {
                throw new GitException("CloneFolderInvalid", null, Array.Empty<object>());
            }
            if (name != folderName)
            {
                throw new GitException("CloneFolderInvalid", null, Array.Empty<object>());
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new GitException("CloneFolderInvalid", null, Array.Empty<object>());
            }
            if (OperatingSystem.IsWindows())
            {
                if (name.EndsWith('.'))
                {
                    throw new GitException("CloneFolderInvalid", null, Array.Empty<object>());
                }
                string firstPart = name.Split('.')[0];
                if (_reservedWindowsNames.Contains(firstPart))
                {
                    throw new GitException("CloneFolderInvalid", null, Array.Empty<object>());
                }
            }
            string destination = Path.Combine(parent, name);
            if (Directory.Exists(destination))
            {
                throw new GitException("CloneDestinationOccupied", null, destination);
            }
            if (File.Exists(destination))
            {
                throw new GitException("CloneDestinationOccupied", null, destination);
            }
            return destination;
        }

        public async Task<string> CloneAsync(string remote, string parentPath, string folderName,
            IProgress<int> progress, CancellationToken cancellationToken = default, Action processStarted = null)
        {
            string source = NormalizeRemote(remote);
            string destination = ValidateDestination(source, parentPath, folderName);
            string parent = Path.GetDirectoryName(destination);
            Progress<string> stderrProgress = new(line => ReportPercentage(line, progress));
            GitCommandResult result = await _runner.RunWithProgressAsync(parent,
                new[] { "clone", "--progress", "--", source, destination }, stderrProgress,
                true, cancellationToken, processStarted);
            if (result.ExitCode != 0)
            {
                // Git may include credentials in stderr. Keep only the exit code at the UI boundary.
                throw new GitException("CloneGitFailed", null, result.ExitCode);
            }
            return destination;
        }

        private static string NormalizeRemote(string remote)
        {
            if (string.IsNullOrWhiteSpace(remote))
            {
                throw new GitException("CloneUrlRequired", null, Array.Empty<object>());
            }
            string value = remote.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri address))
            {
                if (IsSupportedScheme(address.Scheme))
                {
                    if (string.IsNullOrEmpty(address.UserInfo) == false)
                    {
                        throw new GitException("CloneUrlContainsCredentials", null, Array.Empty<object>());
                    }
                    if (string.IsNullOrEmpty(address.Query) == false)
                    {
                        throw new GitException("CloneUrlContainsCredentials", null, Array.Empty<object>());
                    }
                    if (string.IsNullOrEmpty(address.Fragment) == false)
                    {
                        throw new GitException("CloneUrlContainsCredentials", null, Array.Empty<object>());
                    }
                    return value;
                }
            }
            if (_scpRemotePattern.IsMatch(value))
            {
                return value;
            }
            if (Directory.Exists(value))
            {
                return Path.GetFullPath(value);
            }
            throw new GitException("CloneUrlInvalid", null, Array.Empty<object>());
        }

        private static bool IsSupportedScheme(string scheme)
        {
            if (scheme == Uri.UriSchemeHttp)
            {
                return true;
            }
            if (scheme == Uri.UriSchemeHttps)
            {
                return true;
            }
            if (scheme == "ssh")
            {
                return true;
            }
            if (scheme == "git")
            {
                return true;
            }
            return scheme == Uri.UriSchemeFile;
        }

        private static void ReportPercentage(string line, IProgress<int> progress)
        {
            if (progress == null)
            {
                return;
            }
            MatchCollection matches = _percentagePattern.Matches(line);
            if (matches.Count == 0)
            {
                return;
            }
            Match last = matches[matches.Count - 1];
            if (int.TryParse(last.Groups[1].Value, out int percentage) == false)
            {
                return;
            }
            if (percentage > 100)
            {
                return;
            }
            progress.Report(percentage);
        }
    }
}
