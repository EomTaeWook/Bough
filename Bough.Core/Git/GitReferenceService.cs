using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.Core.Git
{
    public class GitReferenceService
    {
        private static readonly string[] _remoteArguments = new string[] { "remote" };
        private static readonly string[] _allReferencesArguments = new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(HEAD)%00%(objecttype)%00%(*objectname)%00%(*objecttype)%00%(upstream)%00%(upstream:remotename)%00%(upstream:remoteref)", "refs/heads", "refs/remotes", "refs/tags" };
        private static readonly string[] _currentBranchArguments = new string[] { "symbolic-ref", "--quiet", "--short", "HEAD" };
        private static readonly string[] _localReferencesArguments = new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(HEAD)%00%(upstream)%00%(upstream:remotename)%00%(upstream:remoteref)", "refs/heads" };
        private static readonly string[] _submoduleConfigArguments = new string[] { "config", "--null", "--file", ".gitmodules", "--get-regexp", "^submodule\\..*\\.(path|url)$" };
        private static readonly string[] _submoduleTreeArguments = new string[] { "ls-tree", "-r", "-z", "HEAD" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "HEAD" };
        private readonly GitCommandRunner _runner;
        private readonly GitRepositoryService _repositoryService;

        public GitReferenceService(GitCommandRunner runner, GitRepositoryService repositoryService)
        {
            _runner = runner;
            _repositoryService = repositoryService;
        }

        public async Task<GitReferenceSnapshot> GetSnapshotAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            string currentBranch = "Detached HEAD";
            ArrayQueue<GitLocalBranch> branches = [];
            GitCommandResult remoteResult = await _runner.RunAsync(repository.RootPath, _remoteArguments, false, cancellationToken);
            string[] remoteNames = SplitLines(remoteResult.Output);
            Dictionary<string, ArrayQueue<GitRemoteBranch>> remoteBranches = new(StringComparer.Ordinal);
            foreach (string remoteName in remoteNames)
            {
                remoteBranches.Add(remoteName, []);
            }

            ArrayQueue<GitTag> tags = [];
            GitCommandResult referenceResult = await _runner.RunAsync(repository.RootPath,
                _allReferencesArguments, false, cancellationToken);
            bool foundCurrentBranch = false;
            foreach (string[] fields in ParseRows(referenceResult.Output, 9))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fields[0].StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    string name = fields[0]["refs/heads/".Length..];
                    bool isCurrent = fields[2] == "*";
                    branches.Add(new GitLocalBranch(name, fields[1], isCurrent, fields[6], fields[7], fields[8]));
                    if (isCurrent)
                    {
                        currentBranch = name;
                        foundCurrentBranch = true;
                    }
                    continue;
                }

                if (fields[0].StartsWith("refs/tags/", StringComparison.Ordinal))
                {
                    string commitHash = string.Empty;
                    if (fields[3] == "commit")
                    {
                        commitHash = fields[1];
                    }
                    if (fields[3] == "tag" && fields[5] == "commit")
                    {
                        commitHash = fields[4];
                    }
                    tags.Add(new GitTag(fields[0]["refs/tags/".Length..], commitHash));
                    continue;
                }

                const string prefix = "refs/remotes/";
                if (fields[0].StartsWith(prefix, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string fullName = fields[0][prefix.Length..];
                int separator = fullName.IndexOf('/');
                if (separator < 1 || fullName.EndsWith("/HEAD", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                string remoteName = fullName[..separator];
                if (remoteBranches.TryGetValue(remoteName, out ArrayQueue<GitRemoteBranch> items) == true)
                {
                    items.Add(new GitRemoteBranch(remoteName, fullName[(separator + 1)..], fields[1]));
                }
            }

            if (foundCurrentBranch == false)
            {
                GitCommandResult headResult = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
                if (headResult.ExitCode == 0)
                {
                    currentBranch = headResult.Output.Trim();
                }
            }

            ArrayQueue<GitRemote> remotes = [];
            foreach (string remoteName in remoteNames)
            {
                GitCommandResult urlResult = await _runner.RunAsync(repository.RootPath, new string[] { "remote", "get-url", remoteName }, true, cancellationToken);
                string url = string.Empty;
                if (urlResult.ExitCode == 0)
                {
                    url = SanitizeUrl(urlResult.Output.Trim());
                }
                else
                {
                    throw new GitException("ReferenceRemoteUrlUnreadable", null, remoteName, urlResult.Error.Trim());
                }
                remotes.Add(new GitRemote(remoteName, url, remoteBranches[remoteName].ToArray()));
            }

            IReadOnlyList<GitSubmodule> submodules = await ReadSubmodulesAsync(repository, cancellationToken);
            return new GitReferenceSnapshot(currentBranch, branches.ToArray(), remotes.ToArray(), tags.ToArray(), submodules);
        }

        public async Task<GitRepository> SwitchBranchAsync(GitRepository repository, string branchName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
            GitCommandResult current = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            if (current.ExitCode == 0)
            {
                if (string.Equals(current.Output.Trim(), branchName, StringComparison.Ordinal))
                {
                    return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
                }
            }
            await _runner.RunAsync(repository.RootPath, new string[] { "switch", "--no-guess", branchName }, false, cancellationToken);
            GitRepository updated = await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
            if (updated.CurrentBranch != branchName)
            {
                throw new GitException("ReferenceBranchSwitchMismatch", null, branchName, updated.CurrentBranch);
            }
            return updated;
        }

        public async Task CreateLightweightTagAsync(GitRepository repository, string tagName, string target, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
            ArgumentException.ThrowIfNullOrWhiteSpace(target);
            string name = tagName.Trim();
            if (name.StartsWith("-", StringComparison.Ordinal))
            {
                throw new GitException("ReferenceTagNameInvalid", null, name);
            }

            GitCommandResult format = await _runner.RunAsync(repository.RootPath,
                new string[] { "check-ref-format", $"refs/tags/{name}" }, true, cancellationToken);
            if (format.ExitCode != 0)
            {
                throw new GitException("ReferenceTagNameRejected", null, name, format.Error.Trim());
            }

            string reference = $"refs/tags/{name}";
            GitCommandResult existing = await _runner.RunAsync(repository.RootPath,
                new string[] { "show-ref", "--verify", "--quiet", reference }, true, cancellationToken);
            if (existing.ExitCode == 0)
            {
                throw new GitException("ReferenceTagExists", null, name);
            }
            if (existing.ExitCode != 1)
            {
                throw new GitException("ReferenceTagLookupFailed", null, existing.Error.Trim());
            }

            string point = target.Trim();
            if (point != "HEAD")
            {
                if (point.Length != 40 && point.Length != 64)
                {
                    throw new GitException("ReferenceTagTargetHashInvalid", null, point);
                }
                foreach (char character in point)
                {
                    if (Uri.IsHexDigit(character) == false)
                    {
                        throw new GitException("ReferenceTagTargetHashInvalid", null, point);
                    }
                }
            }

            GitCommandResult commit = await _runner.RunAsync(repository.RootPath,
                new string[] { "rev-parse", "--verify", "--quiet", $"{point}^{{commit}}" }, true, cancellationToken);
            if (commit.ExitCode != 0)
            {
                throw new GitException("ReferenceTagTargetNotFound", null, point);
            }

            await _runner.RunAsync(repository.RootPath,
                new string[] { "tag", "--", name, commit.Output.Trim() }, false, cancellationToken);
        }

        public async Task<IReadOnlyList<GitLocalBranch>> GetLocalBranchesAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                _localReferencesArguments, false, cancellationToken);
            ArrayQueue<GitLocalBranch> branches = new();
            foreach (string[] fields in ParseRows(result.Output, 6))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = fields[0]["refs/heads/".Length..];
                branches.Add(new GitLocalBranch(name, fields[1], fields[2] == "*", fields[3], fields[4], fields[5]));
            }
            return branches.ToArray();
        }

        public async Task<GitRepository> CreateBranchAsync(GitRepository repository, string branchName, string startPoint, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
            GitCommandResult checkResult = await _runner.RunAsync(repository.RootPath, new string[] { "check-ref-format", "--branch", branchName }, true, cancellationToken);
            if (checkResult.ExitCode != 0)
            {
                throw new GitException("ReferenceBranchNameRejected", null, branchName, checkResult.Error.Trim());
            }

            string point = "HEAD";
            if (string.IsNullOrWhiteSpace(startPoint) == false)
            {
                point = startPoint.Trim();
            }
            if (point.StartsWith("-", StringComparison.Ordinal) == true)
            {
                throw new GitException("ReferenceStartPointInvalid", null, point);
            }

            GitCommandResult commitResult = await _runner.RunAsync(repository.RootPath, new string[] { "rev-parse", "--verify", "--quiet", $"{point}^{{commit}}" }, true, cancellationToken);
            if (commitResult.ExitCode != 0)
            {
                throw new GitException("ReferenceStartCommitNotFound", null, point);
            }

            await _runner.RunAsync(repository.RootPath, new string[] { "switch", "-c", branchName, commitResult.Output.Trim() }, false, cancellationToken);
            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        public async Task<GitRepository> TrackRemoteBranchAsync(GitRepository repository, GitRemoteBranch remoteBranch, string localName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(remoteBranch);
            ArgumentException.ThrowIfNullOrWhiteSpace(localName);
            GitCommandResult checkResult = await _runner.RunAsync(repository.RootPath, new string[] { "check-ref-format", "--branch", localName }, true, cancellationToken);
            if (checkResult.ExitCode != 0)
            {
                throw new GitException("ReferenceBranchNameRejected", null, localName, checkResult.Error.Trim());
            }

            await _runner.RunAsync(repository.RootPath, new string[] { "switch", "--track", "-c", localName, remoteBranch.FullName }, false, cancellationToken);
            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        private async Task<IReadOnlyList<GitSubmodule>> ReadSubmodulesAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            string configPath = Path.Combine(repository.RootPath, ".gitmodules");
            if (File.Exists(configPath) == false)
            {
                return Array.Empty<GitSubmodule>();
            }

            GitCommandResult configResult = await _runner.RunAsync(repository.RootPath, _submoduleConfigArguments, true, cancellationToken);
            if (configResult.ExitCode == 1)
            {
                return Array.Empty<GitSubmodule>();
            }
            if (configResult.ExitCode != 0)
            {
                throw new GitException("ReferenceSubmoduleConfigUnreadable", null, configResult.Error.Trim());
            }

            Dictionary<string, string> paths = new(StringComparer.Ordinal);
            Dictionary<string, string> urls = new(StringComparer.Ordinal);
            foreach (string entry in configResult.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.IndexOf('\n');
                if (separator < 0)
                {
                    throw new GitException("ReferenceSubmoduleConfigInvalid", null, Array.Empty<object>());
                }

                string key = entry[..separator];
                string value = entry[(separator + 1)..];
                const string prefix = "submodule.";
                if (key.StartsWith(prefix, StringComparison.Ordinal) == false)
                {
                    continue;
                }
                if (key.EndsWith(".path", StringComparison.Ordinal) == true)
                {
                    paths[key[prefix.Length..^5]] = value;
                }
                if (key.EndsWith(".url", StringComparison.Ordinal) == true)
                {
                    urls[key[prefix.Length..^4]] = SanitizeUrl(value);
                }
            }

            Dictionary<string, string> expected = new(StringComparer.Ordinal);
            GitCommandResult treeResult = await _runner.RunAsync(repository.RootPath, _submoduleTreeArguments, true, cancellationToken);
            if (treeResult.ExitCode == 0)
            {
                foreach (string entry in treeResult.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (entry.StartsWith("160000 commit ", StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    int separator = entry.IndexOf('\t');
                    if (separator > 14)
                    {
                        expected[entry[(separator + 1)..]] = entry[14..separator];
                    }
                }
            }

            ArrayQueue<GitSubmodule> submodules = [];
            foreach (KeyValuePair<string, string> pair in paths.OrderBy(item => item.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = pair.Value;
                string fullPath = Path.GetFullPath(Path.Combine(repository.RootPath, path));
                string relativePath = Path.GetRelativePath(repository.RootPath, fullPath);
                if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    throw new GitException("ReferenceSubmoduleOutsideRepository", null, path);
                }

                string expectedCommit = string.Empty;
                if (expected.TryGetValue(path, out string commit) == true)
                {
                    expectedCommit = commit;
                }
                string currentCommit = string.Empty;
                string state = "ReferenceSubmoduleUninitialized";
                string gitMarker = Path.Combine(fullPath, ".git");
                if (File.Exists(gitMarker) == true || Directory.Exists(gitMarker) == true)
                {
                    GitCommandResult currentResult = await _runner.RunAsync(fullPath, _verifyHeadArguments, true, cancellationToken);
                    if (currentResult.ExitCode == 0)
                    {
                        currentCommit = currentResult.Output.Trim();
                        state = "ReferenceSubmoduleDifferentCommit";
                        if (expectedCommit == currentCommit)
                        {
                            state = "ReferenceSubmoduleMatchingCommit";
                        }
                    }
                    else
                    {
                        state = "ReferenceSubmoduleCheckoutUnavailable";
                    }
                }

                string url = string.Empty;
                if (urls.TryGetValue(pair.Key, out string configuredUrl) == true)
                {
                    url = configuredUrl;
                }
                submodules.Add(new GitSubmodule(path, url, expectedCommit, currentCommit, state));
            }

            return submodules.ToArray();
        }

        private static string[][] ParseRows(string output, int fieldCount)
        {
            ArrayQueue<string[]> rows = [];
            foreach (string line in SplitLines(output))
            {
                string[] fields = line.Split('\0');
                if (fields.Length != fieldCount)
                {
                    throw new GitException("ReferenceOutputInvalid", null, Array.Empty<object>());
                }

                rows.Add(fields);
            }

            return rows.ToArray();
        }

        private static string[] SplitLines(string output)
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public static string SanitizeUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) == false)
            {
                return url;
            }

            if (parsed.UserInfo.Length == 0 && parsed.Query.Length == 0 && parsed.Fragment.Length == 0)
            {
                return url;
            }

            string userInfo = string.Empty;
            if (parsed.UserInfo.Length > 0)
            {
                userInfo = "***@";
            }

            return $"{parsed.Scheme}://{userInfo}{parsed.Authority}{parsed.AbsolutePath}";
        }
    }
}
