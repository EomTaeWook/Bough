using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.Core.Git
{
    public enum GitPullStrategy
    {
        FastForwardOnly,
        Merge,
        Rebase
    }

    public enum GitPullStage
    {
        Fetching,
        Inspecting,
        Applying
    }

    public class GitPullProgress
    {
        public GitPullProgress(GitPullStage stage, GitRemoteMessage transferStatus, IReadOnlyList<GitRemoteMessage> incomingSummary)
        {
            Stage = stage;
            TransferStatus = transferStatus;
            IncomingSummary = incomingSummary;
        }

        public GitPullStage Stage { get; }
        public GitRemoteMessage TransferStatus { get; }
        public IReadOnlyList<GitRemoteMessage> IncomingSummary { get; }
    }

    public class GitRemoteMessage
    {
        public GitRemoteMessage(string key, params object[] arguments)
        {
            Key = key;
            Arguments = arguments;
        }

        public string Key { get; }
        public IReadOnlyList<object> Arguments { get; }
    }

    public class GitFetchFailure
    {
        public GitFetchFailure(string remote, string error, int exitCode)
        {
            Remote = remote;
            Error = error;
            ExitCode = exitCode;
        }

        public string Remote { get; }
        public string Error { get; }
        public int ExitCode { get; }
    }

    public class GitRemoteState
    {
        public GitRemoteState(string repositoryRoot, string branchName, string headHash, string upstreamName, string upstreamRemote, string upstreamBranch, int ahead, int behind, IReadOnlyList<string> remotes)
        {
            RepositoryRoot = repositoryRoot;
            BranchName = branchName;
            HeadHash = headHash;
            UpstreamName = upstreamName;
            UpstreamRemote = upstreamRemote;
            UpstreamBranch = upstreamBranch;
            Ahead = ahead;
            Behind = behind;
            Remotes = remotes;
        }

        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string HeadHash { get; }
        public string UpstreamName { get; }
        public string UpstreamRemote { get; }
        public string UpstreamBranch { get; }
        public int Ahead { get; }
        public int Behind { get; }
        public IReadOnlyList<string> Remotes { get; }
        public bool IsDetached { get { return BranchName.Length == 0; } }
        public bool HasUpstream { get { return UpstreamRemote.Length > 0 && UpstreamBranch.Length > 0; } }
    }

    public class GitFetchResult
    {
        public GitFetchResult(IReadOnlyList<string> updatedReferences, IReadOnlyList<string> removedReferences, IReadOnlyList<string> succeededRemotes, IReadOnlyList<GitFetchFailure> failedRemotes)
        {
            UpdatedReferences = updatedReferences;
            RemovedReferences = removedReferences;
            SucceededRemotes = succeededRemotes;
            FailedRemotes = failedRemotes;
        }

        public IReadOnlyList<string> UpdatedReferences { get; }
        public IReadOnlyList<string> RemovedReferences { get; }
        public IReadOnlyList<string> SucceededRemotes { get; }
        public IReadOnlyList<GitFetchFailure> FailedRemotes { get; }
    }

    public class GitRemoteOperationService
    {
        private readonly GitCommandRunner _runner;
        private static readonly string[] _remoteArguments = new string[] { "remote" };
        private static readonly string[] _currentBranchArguments = new string[] { "symbolic-ref", "--quiet", "--short", "HEAD" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "HEAD" };
        private static readonly string[] _fetchHeadArguments = new string[] { "rev-parse", "--verify", "FETCH_HEAD^{commit}" };
        private static readonly string[] _remoteReferencesArguments = new string[] { "for-each-ref", "--format=%(refname:short)%00%(objectname)", "refs/remotes", "refs/tags" };

        public GitRemoteOperationService(GitCommandRunner runner)
        {
            _runner = runner;
        }

        public async Task<GitRemoteState> GetStateAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitCommandResult remoteResult = await _runner.RunAsync(repository.RootPath, _remoteArguments, false, cancellationToken);
            string[] remotes = remoteResult.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            GitCommandResult branchResult = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            string branch = string.Empty;
            if (branchResult.ExitCode == 0)
            {
                branch = branchResult.Output.Trim();
            }

            string head = string.Empty;
            string upstreamName = string.Empty;
            string upstreamRemote = string.Empty;
            string upstreamBranch = string.Empty;
            int ahead = 0;
            int behind = 0;
            if (branch.Length > 0)
            {
                GitCommandResult trackingResult = await _runner.RunAsync(repository.RootPath,
                    new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(upstream:short)%00%(upstream:remotename)%00%(upstream:remoteref)%00%(upstream:track,nobracket)", $"refs/heads/{branch}" }, false, cancellationToken);
                foreach (string row in trackingResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = row.TrimEnd('\r').Split('\0');
                    if (fields.Length != 6)
                    {
                        throw new GitException("RemoteStateOutputInvalid", null, Array.Empty<object>());
                    }
                    if (fields[0] != $"refs/heads/{branch}")
                    {
                        continue;
                    }

                    head = fields[1];
                    if (fields[5].Trim('[', ']') == "gone")
                    {
                        break;
                    }

                    upstreamName = fields[2];
                    string remote = fields[3];
                    string remoteRef = fields[4];
                    if (remotes.Contains(remote, StringComparer.Ordinal))
                    {
                        if (remoteRef.StartsWith("refs/heads/", StringComparison.Ordinal))
                        {
                            upstreamRemote = remote;
                            upstreamBranch = remoteRef["refs/heads/".Length..];
                        }
                    }

                    bool trackingKnown = fields[5].Length == 0;
                    foreach (string count in fields[5].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (count.StartsWith("ahead ", StringComparison.Ordinal))
                        {
                            trackingKnown = int.TryParse(count["ahead ".Length..], out ahead);
                            if (trackingKnown == false)
                            {
                                break;
                            }
                            continue;
                        }
                        if (count.StartsWith("behind ", StringComparison.Ordinal))
                        {
                            trackingKnown = int.TryParse(count["behind ".Length..], out behind);
                            if (trackingKnown == false)
                            {
                                break;
                            }
                            continue;
                        }
                        trackingKnown = false;
                        break;
                    }
                    if (trackingKnown == false)
                    {
                        ahead = -1;
                        behind = -1;
                    }
                    break;
                }
            }
            else
            {
                GitCommandResult headResult = await _runner.RunAsync(repository.RootPath, _verifyHeadArguments, true, cancellationToken);
                if (headResult.ExitCode == 0)
                {
                    head = headResult.Output.Trim();
                }
            }

            return new GitRemoteState(repository.RootPath, branch, head, upstreamName, upstreamRemote, upstreamBranch, ahead, behind, Array.AsReadOnly(remotes));
        }

        public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(GitRepository repository, string remoteName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitRemoteState state = await GetStateAsync(repository, cancellationToken);
            return await GetRemoteBranchesAsync(repository, state, remoteName, cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(GitRepository repository, GitRemoteState state, string remoteName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(state);
            if (state.RepositoryRoot != repository.RootPath)
            {
                throw new GitException("RemoteRepositoryChanged", null, Array.Empty<object>());
            }
            string remote = ResolveRemote(state, remoteName);
            string prefix = $"refs/remotes/{remote}/";
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "for-each-ref", "--format=%(refname)%00%(symref)", prefix }, false, cancellationToken);
            List<string> branches = [];
            foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.TrimEnd('\r').Split('\0');
                if (fields.Length != 2)
                {
                    continue;
                }
                if (fields[1].Length > 0)
                {
                    continue;
                }
                if (fields[0].StartsWith(prefix, StringComparison.Ordinal) == false)
                {
                    continue;
                }
                branches.Add(fields[0].Substring(prefix.Length));
            }
            branches.Sort(StringComparer.Ordinal);
            return branches.AsReadOnly();
        }

        public async Task ValidatePushTargetAsync(GitRepository repository, string remoteName, string branchName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitRemoteState state = await GetStateAsync(repository, cancellationToken);
            await ValidatePushTargetAsync(repository, state, remoteName, branchName, cancellationToken);
        }

        public async Task ValidatePushTargetAsync(GitRepository repository, GitRemoteState state, string remoteName, string branchName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(state);
            if (state.RepositoryRoot != repository.RootPath)
            {
                throw new GitException("RemoteRepositoryChanged", null, Array.Empty<object>());
            }
            ResolveRemote(state, remoteName);
            if (string.IsNullOrWhiteSpace(branchName) == true)
            {
                throw new GitException("RemoteTargetBranchRequired", null, Array.Empty<object>());
            }
            await VerifyBranchNameAsync(repository, branchName.Trim(), cancellationToken);
        }

        public async Task<GitFetchResult> FetchAsync(GitRepository repository, GitRemoteState expected, string remoteName, bool fetchAll, bool prune, CancellationToken cancellationToken = default)
        {
            GitRemoteState current = await VerifyStateAsync(repository, expected, cancellationToken);
            ArrayQueue<string> targets = [];
            if (fetchAll == true)
            {
                foreach (string remote in current.Remotes) { targets.Add(remote); }
            }
            else
            {
                string target = ResolveRemote(current, remoteName);
                targets.Add(target);
            }
            if (targets.Count == 0)
            {
                throw new GitException("RemoteNoRemotesConfigured", null, Array.Empty<object>());
            }

            Dictionary<string, string> before = await ReadReferencesAsync(repository, cancellationToken);
            ArrayQueue<string> succeeded = [];
            ArrayQueue<GitFetchFailure> failed = [];
            foreach (string target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] arguments = new string[] { "fetch", target };
                if (prune == true)
                {
                    arguments = new string[] { "fetch", "--prune", target };
                }
                GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
                if (result.ExitCode == 0)
                {
                    succeeded.Add(target);
                }
                else
                {
                    string error = await SanitizeErrorAsync(repository, target, result, cancellationToken);
                    failed.Add(new GitFetchFailure(target, error, result.ExitCode));
                }
            }

            Dictionary<string, string> after = await ReadReferencesAsync(repository, cancellationToken);
            ArrayQueue<string> updated = [];
            ArrayQueue<string> removed = [];
            foreach (KeyValuePair<string, string> reference in after)
            {
                if (before.TryGetValue(reference.Key, out string oldHash) == false || oldHash != reference.Value)
                {
                    updated.Add(reference.Key);
                }
            }
            foreach (string name in before.Keys)
            {
                if (after.ContainsKey(name) == false)
                {
                    removed.Add(name);
                }
            }
            return new GitFetchResult(updated.ToArray(), removed.ToArray(), succeeded.ToArray(), failed.ToArray());
        }

        public async Task PullAsync(GitRepository repository, GitRemoteState expected, string remoteName, string branchName, GitPullStrategy strategy, CancellationToken cancellationToken = default)
        {
            await PullWithProgressAsync(repository, expected, remoteName, branchName, strategy, null, cancellationToken);
        }

        public async Task<IReadOnlyList<GitRemoteMessage>> PullWithProgressAsync(GitRepository repository, GitRemoteState expected, string remoteName, string branchName, GitPullStrategy strategy, IProgress<GitPullProgress> progress, CancellationToken cancellationToken = default)
        {
            GitRemoteState current = await VerifyStateAsync(repository, expected, cancellationToken);
            if (current.IsDetached == true)
            {
                throw new GitException("RemotePullDetachedHead", null, Array.Empty<object>());
            }
            if (Enum.IsDefined(strategy) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(strategy));
            }

            string remote = ResolveRemote(current, remoteName);
            string branch = ResolveBranch(current, remote, branchName);
            await VerifyBranchNameAsync(repository, branch, cancellationToken);
            progress?.Report(new GitPullProgress(GitPullStage.Fetching, null, null));
            IProgress<string> transferProgress = new Progress<string>(line =>
            {
                GitRemoteMessage transferStatus = ParseTransferProgress(line);
                if (transferStatus != null)
                {
                    progress?.Report(new GitPullProgress(GitPullStage.Fetching, transferStatus, null));
                }
            });
            GitCommandResult fetch = await _runner.RunWithProgressAsync(repository.RootPath, new string[] { "fetch", "--progress", remote, branch }, transferProgress, true, cancellationToken);
            if (fetch.ExitCode != 0)
            {
                throw await CreateCommandFailureAsync("RemotePullFetchFailed", repository, remote, branch, fetch, cancellationToken);
            }

            progress?.Report(new GitPullProgress(GitPullStage.Inspecting, null, null));
            GitCommandResult incoming = await _runner.RunAsync(repository.RootPath, _fetchHeadArguments, false, cancellationToken);
            string incomingHash = incoming.Output.Trim();
            IReadOnlyList<GitRemoteMessage> summary = await ReadIncomingSummaryAsync(repository, current.HeadHash, incomingHash, cancellationToken);
            progress?.Report(new GitPullProgress(GitPullStage.Inspecting, null, summary));
            await VerifyStateAsync(repository, current, cancellationToken);

            string[] arguments = new string[] { "merge", "--ff-only", incomingHash };
            if (strategy == GitPullStrategy.Merge)
            {
                arguments = new string[] { "merge", "--no-edit", incomingHash };
            }
            if (strategy == GitPullStrategy.Rebase)
            {
                arguments = new string[] { "rebase", incomingHash };
            }
            progress?.Report(new GitPullProgress(GitPullStage.Applying, null, summary));
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw await CreateCommandFailureAsync("RemotePullApplyFailed", repository, remote, branch, result, cancellationToken);
            }
            return summary;
        }

        private static GitRemoteMessage ParseTransferProgress(string line)
        {
            string value = line.Trim();
            if (value.StartsWith("remote: ", StringComparison.Ordinal) == true)
            {
                value = value.Substring(8);
            }
            Match match = Regex.Match(value, @"^(?<kind>Enumerating objects|Counting objects|Compressing objects|Receiving objects|Resolving deltas|Writing objects):\s*(?<value>\d{1,3}%\s*\(\d+/\d+\)|\d+)", RegexOptions.CultureInvariant);
            if (match.Success == false)
            {
                return null;
            }
            string kind = match.Groups["kind"].Value;
            string key = kind switch
            {
                "Enumerating objects" => "RemoteTransferEnumerating",
                "Counting objects" => "RemoteTransferCounting",
                "Compressing objects" => "RemoteTransferCompressing",
                "Receiving objects" => "RemoteTransferReceiving",
                "Resolving deltas" => "RemoteTransferResolving",
                "Writing objects" => "RemoteTransferWriting",
                _ => string.Empty
            };
            if (key.Length == 0)
            {
                return null;
            }
            return new GitRemoteMessage(key, match.Groups["value"].Value);
        }

        private async Task<IReadOnlyList<GitRemoteMessage>> ReadIncomingSummaryAsync(GitRepository repository, string localHash, string incomingHash, CancellationToken cancellationToken)
        {
            GitCommandResult countResult = await _runner.RunAsync(repository.RootPath, new string[] { "rev-list", "--count", $"{localHash}..{incomingHash}" }, true, cancellationToken);
            if (countResult.ExitCode != 0)
            {
                return new GitRemoteMessage[] { new("RemoteIncomingSummaryUnavailable") };
            }
            if (long.TryParse(countResult.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long count) == false)
            {
                return new GitRemoteMessage[] { new("RemoteIncomingCountInvalid") };
            }

            List<GitRemoteMessage> summary = [new("RemoteIncomingCommitCount", count)];
            if (count > 0)
            {
                GitCommandResult log = await _runner.RunAsync(repository.RootPath, new string[] { "log", "-z", "--max-count=8", "--format=%h%x09%s", $"{localHash}..{incomingHash}" }, true, cancellationToken);
                if (log.ExitCode == 0)
                {
                    foreach (string entry in log.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    {
                        summary.Add(new GitRemoteMessage(null, entry.TrimEnd('\r', '\n')));
                    }
                    if (count > 8)
                    {
                        summary.Add(new GitRemoteMessage("RemoteSummaryMore", count - 8));
                    }
                }
            }

            GitCommandResult baseResult = await _runner.RunAsync(repository.RootPath, new string[] { "merge-base", localHash, incomingHash }, true, cancellationToken);
            if (baseResult.ExitCode != 0)
            {
                summary.Add(new GitRemoteMessage("RemoteChangedFilesNoCommonAncestor"));
                return summary;
            }
            GitCommandResult files = await _runner.RunAsync(repository.RootPath, new string[] { "diff", "--name-only", "-z", baseResult.Output.Trim(), incomingHash }, true, cancellationToken);
            if (files.ExitCode != 0)
            {
                summary.Add(new GitRemoteMessage("RemoteChangedFilesUnavailable"));
                return summary;
            }
            string[] paths = files.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            summary.Add(new GitRemoteMessage("RemoteChangedFileCount", paths.Length));
            int shown = Math.Min(paths.Length, 8);
            for (int index = 0; index < shown; index++)
            {
                string path = paths[index].Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
                summary.Add(new GitRemoteMessage(null, path));
            }
            if (paths.Length > shown)
            {
                summary.Add(new GitRemoteMessage("RemoteSummaryMore", paths.Length - shown));
            }
            return summary;
        }

        public async Task<bool> PushAsync(GitRepository repository, GitRemoteState expected, string remoteName, string targetBranch, bool firstPushConfirmed, CancellationToken cancellationToken = default)
        {
            GitRemoteState current = await VerifyStateAsync(repository, expected, cancellationToken);
            if (current.IsDetached == true)
            {
                throw new GitException("RemotePushDetachedHead", null, Array.Empty<object>());
            }
            if (current.HeadHash.Length == 0)
            {
                throw new GitException("RemotePushNoCommit", null, Array.Empty<object>());
            }

            bool firstPush = current.HasUpstream == false;
            string remote = ResolveRemote(current, remoteName);
            string branch = ResolveBranch(current, remote, targetBranch);
            bool destinationChanged = firstPush == true || remote != current.UpstreamRemote || branch != current.UpstreamBranch;
            if (destinationChanged == false && current.Ahead == 0)
            {
                return false;
            }

            await VerifyBranchNameAsync(repository, branch, cancellationToken);
            if (destinationChanged == true && firstPushConfirmed == false)
            {
                throw new GitException("RemotePushTargetConfirmationRequired", null, remote, branch);
            }

            string refspec = $"refs/heads/{current.BranchName}:refs/heads/{branch}";
            string[] arguments = new string[] { "push", remote, refspec };
            if (firstPush == true)
            {
                arguments = new string[] { "push", "--set-upstream", remote, refspec };
            }
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw await CreateCommandFailureAsync("RemotePushFailed", repository, remote, branch, result, cancellationToken);
            }
            return true;
        }

        private async Task<GitRemoteState> VerifyStateAsync(GitRepository repository, GitRemoteState expected, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(expected);
            if (repository.RootPath != expected.RepositoryRoot)
            {
                throw new GitException("RemoteRepositoryChanged", null, Array.Empty<object>());
            }
            GitRemoteState current = await GetStateAsync(repository, cancellationToken);
            if (current.BranchName != expected.BranchName)
            {
                throw new GitException("RemoteBranchChanged", null, expected.BranchName, current.BranchName);
            }
            if (current.HeadHash != expected.HeadHash)
            {
                throw new GitException("RemoteHeadChanged", null, Array.Empty<object>());
            }
            if (current.UpstreamName != expected.UpstreamName)
            {
                throw new GitException("RemoteUpstreamChanged", null, expected.UpstreamName, current.UpstreamName);
            }
            return current;
        }

        private static string ResolveRemote(GitRemoteState state, string selected)
        {
            string remote = selected;
            if (state.HasUpstream == true && string.IsNullOrWhiteSpace(remote) == true)
            {
                remote = state.UpstreamRemote;
            }
            if (string.IsNullOrWhiteSpace(remote) == true)
            {
                throw new GitException("RemoteSelectionRequired", null, Array.Empty<object>());
            }
            if (state.Remotes.Contains(remote, StringComparer.Ordinal) == false)
            {
                throw new GitException("RemoteSelectionNotFound", null, remote);
            }
            return remote;
        }

        private static string ResolveBranch(GitRemoteState state, string remote, string selected)
        {
            if (string.IsNullOrWhiteSpace(selected) == false)
            {
                return selected.Trim();
            }
            if (state.HasUpstream == true && state.UpstreamRemote == remote)
            {
                return state.UpstreamBranch;
            }
            throw new GitException("RemoteTargetBranchSelectionRequired", null, Array.Empty<object>());
        }

        private async Task VerifyBranchNameAsync(GitRepository repository, string name, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "check-ref-format", "--branch", name }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("RemoteBranchNameInvalid", null, name);
            }
        }

        private async Task<Dictionary<string, string>> ReadReferencesAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _remoteReferencesArguments, false, cancellationToken);
            Dictionary<string, string> references = new(StringComparer.Ordinal);
            foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.TrimEnd('\r').Split('\0');
                if (fields.Length == 2)
                {
                    references[fields[0]] = fields[1];
                }
            }
            return references;
        }

        private async Task<string> SanitizeErrorAsync(GitRepository repository, string remote, GitCommandResult result, CancellationToken cancellationToken)
        {
            string message = result.Error.Trim();
            GitCommandResult urlResult = await _runner.RunAsync(repository.RootPath, new string[] { "remote", "get-url", remote }, true, cancellationToken);
            if (urlResult.ExitCode == 0)
            {
                string url = urlResult.Output.Trim();
                if (url.Length > 0)
                {
                    message = message.Replace(url, GitReferenceService.SanitizeUrl(url), StringComparison.Ordinal);
                }
            }
            message = Regex.Replace(message, @"(?i)(https?://)[^/@\s]+@", "$1***@");
            message = Regex.Replace(message, @"(?i)([?&](token|access_token|password|passwd|secret|key)=)[^&\s]+", "$1***");
            if (message.Length > 1000)
            {
                message = message.Substring(0, 1000);
            }
            return message;
        }

        private async Task<GitException> CreateCommandFailureAsync(string code, GitRepository repository, string remote, string branch, GitCommandResult result, CancellationToken cancellationToken)
        {
            string error = await SanitizeErrorAsync(repository, remote, result, cancellationToken);
            if (error.Length == 0)
            {
                return new GitException($"{code}WithoutOutput", null, remote, branch, result.ExitCode);
            }
            return new GitException(code, null, remote, branch, error);
        }
    }
}
