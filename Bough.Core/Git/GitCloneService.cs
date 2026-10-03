using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
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

        public string ValidateDestination(string remote, string destinationPath)
        {
            NormalizeRemote(remote);
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new GitException("CloneDestinationRequired", null, Array.Empty<object>());
            }
            if (destinationPath != destinationPath.Trim())
            {
                throw new GitException("CloneDestinationInvalid", null, Array.Empty<object>());
            }
            if (Path.IsPathFullyQualified(destinationPath) == false)
            {
                throw new GitException("CloneDestinationAbsoluteRequired", null, Array.Empty<object>());
            }
            string destination;
            try
            {
                destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));
            }
            catch (ArgumentException exception)
            {
                throw new GitException("CloneDestinationInvalid", exception, Array.Empty<object>());
            }
            catch (NotSupportedException exception)
            {
                throw new GitException("CloneDestinationInvalid", exception, Array.Empty<object>());
            }
            catch (PathTooLongException exception)
            {
                throw new GitException("CloneDestinationInvalid", exception, Array.Empty<object>());
            }
            string root = Path.GetPathRoot(destination);
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(destination, root, comparison))
            {
                throw new GitException("CloneDestinationRootForbidden", null, Array.Empty<object>());
            }
            string parent = Path.GetDirectoryName(destination);
            if (Directory.Exists(parent) == false)
            {
                throw new GitException("CloneDestinationParentMissing", null, Array.Empty<object>());
            }
            string name = Path.GetFileName(destination);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new GitException("CloneDestinationInvalid", null, Array.Empty<object>());
            }
            if (OperatingSystem.IsWindows())
            {
                if (name.EndsWith('.'))
                {
                    throw new GitException("CloneDestinationInvalid", null, Array.Empty<object>());
                }
                if (name.EndsWith(' '))
                {
                    throw new GitException("CloneDestinationInvalid", null, Array.Empty<object>());
                }
                string firstPart = name.Split('.')[0];
                if (_reservedWindowsNames.Contains(firstPart))
                {
                    throw new GitException("CloneDestinationInvalid", null, Array.Empty<object>());
                }
            }
            if (File.Exists(destination))
            {
                throw new GitException("CloneDestinationFileExists", null, destination);
            }
            if (Directory.Exists(destination))
            {
                using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(destination).GetEnumerator();
                if (entries.MoveNext())
                {
                    throw new GitException("CloneDestinationNotEmpty", null, destination);
                }
            }
            return destination;
        }

        public async Task<string> CloneAsync(string remote, string destinationPath,
            IProgress<int> progress, CancellationToken cancellationToken = default, Action processStarted = null)
        {
            string source = NormalizeRemote(remote);
            string destination = ValidateDestination(source, destinationPath);
            string parent = Path.GetDirectoryName(destination);
            CloneProgress stderrProgress = new(progress);
            GitCommandResult result = await _runner.RunWithProgressAsync(parent,
                new[] { "clone", "--progress", "--", source, destination }, stderrProgress,
                true, cancellationToken, processStarted);
            if (result.ExitCode != 0)
            {
                // Inspect diagnostics in memory only. Never carry addresses, headers, or stderr
                // into display arguments, exception messages, inner exceptions, or logs.
                throw new GitException(ClassifyFailure(result.Error), null, result.ExitCode);
            }
            return destination;
        }

        public GitCloneDestinationState GetDestinationState(string destination)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(destination);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    return GitCloneDestinationState.ContainsContent;
                }
                using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(destination).GetEnumerator();
                if (entries.MoveNext())
                {
                    return GitCloneDestinationState.ContainsContent;
                }
                return GitCloneDestinationState.EmptyDirectory;
            }
            catch (FileNotFoundException)
            {
                return GitCloneDestinationState.Absent;
            }
            catch (DirectoryNotFoundException)
            {
                return GitCloneDestinationState.Absent;
            }
            catch (IOException)
            {
                return GitCloneDestinationState.InspectionFailed;
            }
            catch (UnauthorizedAccessException)
            {
                return GitCloneDestinationState.InspectionFailed;
            }
            catch (SecurityException)
            {
                return GitCloneDestinationState.InspectionFailed;
            }
            catch (ArgumentException)
            {
                return GitCloneDestinationState.InspectionFailed;
            }
            catch (NotSupportedException)
            {
                return GitCloneDestinationState.InspectionFailed;
            }
        }

        private static string ClassifyFailure(string standardError)
        {
            // Git's wording depends on Git/transport/provider and locale. Unrecognised
            // diagnostics deliberately remain unknown; exit code 128 proves no cause.
            using StringReader reader = new(standardError);
            string fallback = "CloneFailureUnknown";
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string diagnostic = line.Trim();
                if (diagnostic.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase) == false)
                {
                    if (diagnostic.StartsWith("error:", StringComparison.OrdinalIgnoreCase) == false)
                    {
                        if (diagnostic.StartsWith("remote:", StringComparison.OrdinalIgnoreCase) == false)
                        {
                            // OpenSSH reports an authentication failure without a Git prefix.
                            if (diagnostic.EndsWith("Permission denied (publickey).", StringComparison.OrdinalIgnoreCase))
                            {
                                return "CloneFailureAuthentication";
                            }
                            if (diagnostic.EndsWith("Permission denied (publickey,password).", StringComparison.OrdinalIgnoreCase))
                            {
                                return "CloneFailureAuthentication";
                            }
                            if (diagnostic.StartsWith("ssh:", StringComparison.OrdinalIgnoreCase) == false)
                            {
                                continue;
                            }
                        }
                    }
                }

                // Strip quoted address/path fields before classifying. A token or URL must
                // not itself be evidence for a failure category, even though never displayed.
                diagnostic = Regex.Replace(diagnostic, @"'[^']*'|""[^""]*""", string.Empty);
                if (ContainsDiagnostic(diagnostic, "authentication failed"))
                {
                    return "CloneFailureAuthentication";
                }
                if (ContainsDiagnostic(diagnostic, "invalid username or password"))
                {
                    return "CloneFailureAuthentication";
                }
                if (ContainsDiagnostic(diagnostic, "invalid username or token"))
                {
                    return "CloneFailureAuthentication";
                }
                if (ContainsDiagnostic(diagnostic, "could not read username for"))
                {
                    return "CloneFailureAuthentication";
                }
                if (ContainsDiagnostic(diagnostic, "could not read password for"))
                {
                    return "CloneFailureAuthentication";
                }
                if (ContainsDiagnostic(diagnostic, "requested URL returned error: 401"))
                {
                    return "CloneFailureAuthentication";
                }
                if (ContainsDiagnostic(diagnostic, "requested URL returned error: 403"))
                {
                    return "CloneFailureRemoteAccessDenied";
                }
                if (ContainsDiagnostic(diagnostic, "access denied"))
                {
                    if (diagnostic.StartsWith("remote:", StringComparison.OrdinalIgnoreCase))
                    {
                        return "CloneFailureRemoteAccessDenied";
                    }
                }
                if (ContainsDiagnostic(diagnostic, "no space left on device"))
                {
                    return "CloneFailureDiskFull";
                }
                if (ContainsDiagnostic(diagnostic, "disk quota exceeded"))
                {
                    return "CloneFailureDiskFull";
                }
                if (ContainsDiagnostic(diagnostic, "not an empty directory"))
                {
                    return "CloneFailureDestinationConflict";
                }
                if (ContainsDiagnostic(diagnostic, "permission denied"))
                {
                    if (IsLocalWriteDiagnostic(diagnostic))
                    {
                        return "CloneFailureFileAccess";
                    }
                }
                if (ContainsDiagnostic(diagnostic, "access is denied"))
                {
                    if (IsLocalWriteDiagnostic(diagnostic))
                    {
                        return "CloneFailureFileAccess";
                    }
                }
                if (IsConnectionDiagnostic(diagnostic))
                {
                    return "CloneFailureConnection";
                }
                if (ContainsDiagnostic(diagnostic, "repository not found"))
                {
                    fallback = "CloneFailureRepositoryUnavailable";
                }
                if (diagnostic.StartsWith("fatal: repository ", StringComparison.OrdinalIgnoreCase))
                {
                    if (diagnostic.EndsWith("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        fallback = "CloneFailureRepositoryUnavailable";
                    }
                    if (diagnostic.EndsWith("does not exist", StringComparison.OrdinalIgnoreCase))
                    {
                        fallback = "CloneFailureRepositoryUnavailable";
                    }
                }
                if (ContainsDiagnostic(diagnostic, "could not read from remote repository"))
                {
                    // This message alone does not distinguish an incorrect address from
                    // missing repository permissions or another transport failure.
                    fallback = "CloneFailureRepositoryUnavailable";
                }
            }
            return fallback;
        }

        private static bool IsLocalWriteDiagnostic(string diagnostic)
        {
            if (diagnostic.StartsWith("remote:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (ContainsDiagnostic(diagnostic, "could not create work tree dir"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "unable to create file"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "cannot create directory"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "cannot mkdir"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "unable to write"))
            {
                return true;
            }
            return ContainsDiagnostic(diagnostic, "could not write");
        }

        private static bool IsConnectionDiagnostic(string diagnostic)
        {
            if (ContainsDiagnostic(diagnostic, "could not resolve host"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "could not resolve proxy"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "failed to connect"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "connection timed out"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "connection refused"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "network is unreachable"))
            {
                return true;
            }
            if (ContainsDiagnostic(diagnostic, "SSL certificate problem"))
            {
                return true;
            }
            return ContainsDiagnostic(diagnostic, "unable to access") && ContainsDiagnostic(diagnostic, "TLS");
        }

        private static bool ContainsDiagnostic(string diagnostic, string fragment)
        {
            return diagnostic.Contains(fragment, StringComparison.OrdinalIgnoreCase);
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

        private class CloneProgress : IProgress<string>
        {
            private readonly IProgress<int> _progress;

            public CloneProgress(IProgress<int> progress)
            {
                _progress = progress;
            }

            public void Report(string line)
            {
                // Only numeric progress crosses into a UI callback. Raw diagnostics are
                // consumed on the runner's read path and are never posted to the UI.
                ReportPercentage(line, _progress);
            }
        }
    }
}
