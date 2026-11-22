using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.Core.Git
{
    public class GitRemoteOperationService
    {
        private readonly GitCommandRunner _runner;
        private readonly GitReferenceService _referenceService;
        private readonly GitOperationQueue _operationQueue;
        private static readonly string[] _operationMarkers = ["MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer", "BISECT_START", "index.lock", "HEAD.lock"];
        private static readonly string[] _remoteArguments = new string[] { "remote" };
        private static readonly string[] _currentBranchArguments = new string[] { "symbolic-ref", "--quiet", "--short", "HEAD" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "HEAD" };
        private static readonly string[] _fetchHeadArguments = new string[] { "rev-parse", "--verify", "FETCH_HEAD^{commit}" };
        private static readonly string[] _remoteReferencesArguments = new string[] { "for-each-ref", "--format=%(refname:short)%00%(objectname)", "refs/remotes", "refs/tags" };

        public GitRemoteOperationService(GitCommandRunner runner, GitReferenceService referenceService, GitOperationQueue operationQueue)
        {
            _runner = runner;
            _referenceService = referenceService;
            _operationQueue = operationQueue;
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

        public Task<GitFetchResult> FetchAsync(GitRepository repository, GitRemoteState expected, string remoteName, bool fetchAll, bool prune, CancellationToken cancellationToken = default)
        {
            return FetchCoreAsync(repository, expected, remoteName, fetchAll, prune, false, cancellationToken);
        }

        public Task<GitFetchResult> FetchAutomaticallyAsync(GitRepository repository, GitRemoteState expected, CancellationToken cancellationToken)
        {
            return FetchCoreAsync(repository, expected, expected.UpstreamRemote, false, false, true, cancellationToken);
        }

        private async Task<GitFetchResult> FetchCoreAsync(GitRepository repository, GitRemoteState expected, string remoteName, bool fetchAll, bool prune, bool nonInteractive, CancellationToken cancellationToken)
        {
            GitRemoteState current = await VerifyStateAsync(repository, expected, cancellationToken);
            bool useDefaultSshBatchMode = false;
            if (nonInteractive == true)
            {
                if (current.HasUpstream == false)
                {
                    throw new GitException("RemoteNoUpstreamHint", null, Array.Empty<object>());
                }
                if (current.UpstreamRemote != expected.UpstreamRemote)
                {
                    throw new GitException("RemoteRepositoryChanged", null, Array.Empty<object>());
                }
                if (current.UpstreamBranch != expected.UpstreamBranch)
                {
                    throw new GitException("RemoteRepositoryChanged", null, Array.Empty<object>());
                }
                if (current.UpstreamName != remoteName + "/" + current.UpstreamBranch)
                {
                    throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
                }
                useDefaultSshBatchMode = await EnsureAutomaticFetchTransportAsync(repository, remoteName, cancellationToken);
            }
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
                GitCommandResult result;
                if (nonInteractive == true)
                {
                    arguments = new string[] { "fetch", "--no-prune", "--no-prune-tags", "--no-tags", "--recurse-submodules=no", "--refmap=", target,
                        $"+refs/heads/{current.UpstreamBranch}:refs/remotes/{target}/{current.UpstreamBranch}" };
                    result = await _runner.RunNonInteractiveAsync(repository.RootPath, arguments, cancellationToken, useDefaultSshBatchMode);
                }
                else
                {
                    result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
                }
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

        private async Task<bool> EnsureAutomaticFetchTransportAsync(GitRepository repository, string remoteName, CancellationToken cancellationToken)
        {
            GitCommandResult remote = await _runner.RunAsync(repository.RootPath, new string[] { "remote", "get-url", remoteName }, false, cancellationToken);
            string address = remote.Output.Trim();
            bool ssh = false;
            if (Uri.TryCreate(address, UriKind.Absolute, out Uri uri) == true)
            {
                if (uri.Scheme == "http" || uri.Scheme == "https" || uri.IsFile == true)
                {
                    return false;
                }
                ssh = uri.Scheme == "ssh";
            }
            if (address.Contains("::", StringComparison.Ordinal) == true)
            {
                throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
            }
            if (address.Contains("://", StringComparison.Ordinal) == false)
            {
                if (Path.IsPathFullyQualified(address) == true)
                {
                    if (Directory.Exists(address) == true)
                    {
                        return false;
                    }
                }
                int separator = address.IndexOf(':');
                if (separator > 1)
                {
                    if (address[..separator].Contains('/') == false)
                    {
                        if (address[..separator].Contains('\\') == false)
                        {
                            ssh = true;
                        }
                    }
                }
            }
            if (ssh == false)
            {
                if (address.Contains(':') == false)
                {
                    if (Directory.Exists(Path.GetFullPath(address, repository.RootPath)) == true)
                    {
                        return false;
                    }
                }
                throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
            }
            // Do not replace a user's SSH executable/command or host-key policy for automatic checking.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GIT_SSH")) == false)
            {
                throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
            }
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GIT_SSH_COMMAND")) == false)
            {
                throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
            }
            string variant = Environment.GetEnvironmentVariable("GIT_SSH_VARIANT");
            if (string.IsNullOrWhiteSpace(variant) == false)
            {
                if (variant != "ssh")
                {
                    throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
                }
            }
            GitCommandResult command = await _runner.RunAsync(repository.RootPath, new string[] { "config", "--get", "core.sshCommand" }, true, cancellationToken);
            if (command.ExitCode != 0)
            {
                if (command.ExitCode != 1)
                {
                    throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
                }
            }
            if (string.IsNullOrWhiteSpace(command.Output) == false)
            {
                throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
            }
            GitCommandResult configuredVariant = await _runner.RunAsync(repository.RootPath, new string[] { "config", "--get", "ssh.variant" }, true, cancellationToken);
            if (configuredVariant.ExitCode != 0)
            {
                if (configuredVariant.ExitCode != 1)
                {
                    throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
                }
            }
            if (string.IsNullOrWhiteSpace(configuredVariant.Output) == false)
            {
                if (configuredVariant.Output.Trim() != "ssh")
                {
                    throw new GitException("RemoteAutoFetchConfigurationUnavailable", null, Array.Empty<object>());
                }
            }
            return true;
        }
        public async Task PullAsync(GitRepository repository, GitRemoteState expected, string remoteName, string branchName, GitPullStrategy strategy, CancellationToken cancellationToken = default)
        {
            await PullWithProgressAsync(repository, expected, remoteName, branchName, strategy, null, cancellationToken);
        }

        public async Task<GitPullResult> PullWithProgressAsync(GitRepository repository, GitRemoteState expected, string remoteName, string branchName, GitPullStrategy strategy, IProgress<GitPullProgress> progress, CancellationToken cancellationToken = default, bool includeSubmodules = false, string operationName = null)
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
            Dictionary<string, string> previousLinks = null;
            Exception submodulePreparationError = null;
            if (includeSubmodules)
            {
                try
                {
                    previousLinks = await ReadPullGitlinksAsync(repository.RootPath, current.HeadHash, null, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    submodulePreparationError = GetSafePullSubmoduleError(exception);
                }
            }
            progress?.Report(new GitPullProgress(GitPullStage.Fetching, null));
            IProgress<string> transferProgress = new Progress<string>(line =>
            {
                GitRemoteMessage transferStatus = ParseTransferProgress(line);
                if (transferStatus != null)
                {
                    progress?.Report(new GitPullProgress(GitPullStage.Fetching, transferStatus));
                }
            });
            GitCommandResult fetch = await _runner.RunWithProgressAsync(repository.RootPath, new string[] { "fetch", "--progress", "--no-recurse-submodules", remote, branch }, transferProgress, true, cancellationToken);
            if (fetch.ExitCode != 0)
            {
                throw await CreateCommandFailureAsync("RemotePullFetchFailed", repository, remote, branch, fetch, cancellationToken);
            }

            progress?.Report(new GitPullProgress(GitPullStage.Inspecting, null));
            GitCommandResult incoming = await _runner.RunAsync(repository.RootPath, _fetchHeadArguments, false, cancellationToken);
            string incomingHash = incoming.Output.Trim();
            await VerifyStateAsync(repository, current, cancellationToken);

            string[] arguments = new string[] { "-c", "submodule.recurse=false", "merge", "--ff-only", incomingHash };
            if (strategy == GitPullStrategy.Merge)
            {
                arguments = new string[] { "-c", "submodule.recurse=false", "merge", "--no-edit", incomingHash };
            }
            if (strategy == GitPullStrategy.Rebase)
            {
                arguments = new string[] { "-c", "submodule.recurse=false", "rebase", incomingHash };
            }
            progress?.Report(new GitPullProgress(GitPullStage.Applying, null));
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                if (IsTrackedLocalChangesOverwrite(result.Error))
                {
                    string details = await SanitizeErrorAsync(repository, remote, result, cancellationToken);
                    throw new GitRemoteOperationException("RemotePullLocalChangesWouldBeOverwritten", details, Array.Empty<object>());
                }
                if (strategy == GitPullStrategy.FastForwardOnly)
                {
                    if (result.Error.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase))
                    {
                        string details = await SanitizeErrorAsync(repository, remote, result, cancellationToken);
                        throw new GitRemoteOperationException("RemotePullFastForwardUnavailable", details);
                    }
                }

                throw await CreateCommandFailureAsync("RemotePullApplyFailed", repository, remote, branch, result, cancellationToken);
            }

            if (includeSubmodules == false)
            {
                return new GitPullResult(Array.Empty<GitPullSubmoduleResult>());
            }
            if (submodulePreparationError != null)
            {
                return new GitPullResult(Array.Empty<GitPullSubmoduleResult>(), submoduleError: submodulePreparationError);
            }
            progress?.Report(new GitPullProgress(GitPullStage.UpdatingSubmodules, null));
            return await UpdatePullSubmodulesAsync(repository, current, previousLinks, operationName, cancellationToken);
        }

        private async Task<GitPullResult> UpdatePullSubmodulesAsync(GitRepository repository, GitRemoteState beforePull, Dictionary<string, string> previousLinks, string operationName, CancellationToken cancellationToken)
        {
            List<GitPullSubmoduleResult> results = new();
            try
            {
                string parentHead = (await ReadPullSubmoduleCommandAsync(repository.RootPath, _verifyHeadArguments, cancellationToken)).Trim();
                await VerifyPullParentAsync(repository.RootPath, beforePull.BranchName, parentHead, cancellationToken);
                string modules = await ReadPullSubmoduleCommandAsync(repository.RootPath, ["ls-tree", "-z", parentHead, "--", ".gitmodules"], cancellationToken);
                if (modules.Length == 0)
                {
                    return new GitPullResult(results);
                }

                Dictionary<string, string> gitlinks = await ReadPullGitlinksAsync(repository.RootPath, parentHead, null, cancellationToken);
                GitCommandResult registered = await _runner.RunWithoutRemoteAccessAsync(repository.RootPath, ["config", "--no-includes", "--null", "--blob", parentHead + ":.gitmodules", "--get-regexp", "^submodule\\..*\\.path$"], cancellationToken);
                if (registered.ExitCode == 1)
                {
                    return new GitPullResult(results);
                }
                if (registered.ExitCode != 0)
                {
                    throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", registered);
                }

                HashSet<string> paths = new(StringComparer.Ordinal);
                foreach (string entry in registered.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    int separator = entry.IndexOf('\n');
                    if (separator < 0)
                    {
                        throw new GitException("ReferenceSubmoduleConfigInvalid", null, Array.Empty<object>());
                    }
                    string path = entry[(separator + 1)..];
                    if (paths.Add(path) == false)
                    {
                        throw new GitException("ReferenceSubmoduleConfigInvalid", null, Array.Empty<object>());
                    }
                }

                string index = await ReadPullSubmoduleCommandAsync(repository.RootPath, ["ls-files", "--stage", "-z"], cancellationToken);
                HashSet<string> indexEntries = new(index.Split('\0', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                foreach (string path in paths.Order(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (gitlinks.TryGetValue(path, out string target) == false)
                        {
                            results.Add(SkipPullSubmodule(path, "RemotePullSubmoduleUncertain"));
                            continue;
                        }
                        if (indexEntries.Contains(GetStageZeroGitlinkEntry(path, target)) == false)
                        {
                            results.Add(SkipPullSubmodule(path, "RemotePullSubmoduleParentChanged"));
                            continue;
                        }
                        string childRoot = GetSafePullSubmodulePath(repository.RootPath, path);
                        GitOperationQueueState queueState = _operationQueue.GetState(childRoot);
                        if (queueState.IsRunning)
                        {
                            results.Add(SkipPullSubmodule(path, "RemotePullSubmoduleBusy"));
                            continue;
                        }
                        if (queueState.PendingCount > 0)
                        {
                            results.Add(SkipPullSubmodule(path, "RemotePullSubmoduleBusy"));
                            continue;
                        }
                        previousLinks.TryGetValue(path, out string previousTarget);
                        string childOperation = string.IsNullOrWhiteSpace(operationName) ? path : operationName + " · " + path;
                        GitPullSubmoduleResult result = await _operationQueue.EnqueueAsync(childRoot, childOperation, token => CheckoutPullSubmoduleAsync(repository, beforePull, parentHead, path, childRoot, previousTarget, target, token), cancellationToken);
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new GitPullSubmoduleResult(path, GitPullSubmoduleOutcome.Canceled));
                        return new GitPullResult(results, submodulesCanceled: true);
                    }
                    catch (GitException exception) when (exception.ErrorCode == "RemotePullSubmoduleParentChanged")
                    {
                        return new GitPullResult(results, submoduleError: exception);
                    }
                    catch (GitException exception) when (exception.ErrorCode is "SubmoduleOpenUninitialized" or "SubmoduleOpenDirectoryUnavailable" or "SubmoduleOpenNotRegistered" or "RemotePullSubmoduleUncertain")
                    {
                        results.Add(new GitPullSubmoduleResult(path, GitPullSubmoduleOutcome.Skipped, exception));
                    }
                    catch (Exception exception)
                    {
                        results.Add(new GitPullSubmoduleResult(path, GitPullSubmoduleOutcome.Failed, GetSafePullSubmoduleError(exception)));
                    }
                }
                return new GitPullResult(results);
            }
            catch (OperationCanceledException)
            {
                return new GitPullResult(results, submodulesCanceled: true);
            }
            catch (Exception exception)
            {
                return new GitPullResult(results, submoduleError: GetSafePullSubmoduleError(exception));
            }
        }

        private async Task<GitPullSubmoduleResult> CheckoutPullSubmoduleAsync(GitRepository repository, GitRemoteState beforePull, string parentHead, string path, string childRoot, string previousTarget, string target, CancellationToken cancellationToken)
        {
            await VerifyPullParentAsync(repository.RootPath, beforePull.BranchName, parentHead, cancellationToken);
            await VerifyPullGitlinkAsync(repository.RootPath, path, target, cancellationToken);
            string gitDirectory = await VerifyPullSubmoduleRootAsync(repository, path, childRoot, cancellationToken);
            string childHead = (await ReadPullSubmoduleCommandAsync(childRoot, _verifyHeadArguments, cancellationToken)).Trim();
            string childBranch = await ReadPullSubmoduleBranchAsync(childRoot, cancellationToken);
            if (await IsPullSubmoduleCleanAsync(childRoot, gitDirectory, cancellationToken) == false)
            {
                return SkipPullSubmodule(path, "RemotePullSubmoduleDirty");
            }
            if (childHead == target)
            {
                return new GitPullSubmoduleResult(path, GitPullSubmoduleOutcome.AlreadyCurrent);
            }
            bool previouslyRecorded = childHead == previousTarget;
            if (previouslyRecorded == false)
            {
                if (childBranch.Length > 0)
                {
                    return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
                }
                // A previous parent-only pull can leave the detached checkout at the previous recorded gitlink.
                // Prove that exact older gitlink in the first-parent history of the pinned parent commit.
                string history = await ReadPullSubmoduleCommandAsync(repository.RootPath, ["rev-list", "--first-parent", "--max-count=2", parentHead, "--", ":(literal)" + path], cancellationToken);
                string[] commits = history.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (commits.Length != 2)
                {
                    return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
                }
                Dictionary<string, string> earlierLinks = await ReadPullGitlinksAsync(repository.RootPath, commits[1], path, cancellationToken);
                if (earlierLinks.TryGetValue(path, out string earlierTarget) == false)
                {
                    return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
                }
                if (earlierTarget != childHead)
                {
                    return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
                }
            }

            GitCommandResult localTarget = await _runner.RunWithoutRemoteAccessAsync(childRoot, ["--no-replace-objects", "cat-file", "-t", target], cancellationToken);
            if (localTarget.ExitCode != 0)
            {
                GitCommandResult remotes = await _runner.RunAsync(childRoot, _remoteArguments, true, cancellationToken);
                if (remotes.ExitCode != 0)
                {
                    throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", remotes);
                }
                string[] remoteNames = remotes.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string remote = remoteNames.Contains("origin", StringComparer.Ordinal) ? "origin" : remoteNames.Length == 1 ? remoteNames[0] : null;
                if (remote == null)
                {
                    return SkipPullSubmodule(path, "RemotePullSubmoduleCommitUnavailable");
                }
                await VerifyPullParentAsync(repository.RootPath, beforePull.BranchName, parentHead, cancellationToken);
                await VerifyPullGitlinkAsync(repository.RootPath, path, target, cancellationToken);
                if (await VerifyPullSubmoduleRootAsync(repository, path, childRoot, cancellationToken) != gitDirectory)
                {
                    return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
                }

                // Fetch just this object. Do not advance remote refs or FETCH_HEAD, fetch tags, initialize or recurse.
                GitCommandResult fetch = await _runner.RunAsync(childRoot, ["fetch", "--no-recurse-submodules", "--no-tags", "--no-prune", "--no-prune-tags", "--no-write-fetch-head", "--no-auto-maintenance", "--refmap=", "--", remote, target], true, cancellationToken);
                if (fetch.ExitCode != 0)
                {
                    string details = await SanitizeErrorAsync(new GitRepository(childRoot, string.Empty, string.Empty), remote, fetch, cancellationToken);
                    throw new GitRemoteOperationException("RemotePullSubmoduleFetchFailed", details, fetch.ExitCode);
                }
                localTarget = await _runner.RunWithoutRemoteAccessAsync(childRoot, ["--no-replace-objects", "cat-file", "-t", target], cancellationToken);
            }
            if (localTarget.ExitCode != 0)
            {
                throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", localTarget);
            }
            if (localTarget.Output.Trim() != "commit")
            {
                return SkipPullSubmodule(path, "RemotePullSubmoduleCommitUnavailable");
            }
            if (previouslyRecorded == false)
            {
                GitCommandResult ancestor = await _runner.RunWithoutRemoteAccessAsync(childRoot, ["--no-replace-objects", "merge-base", "--is-ancestor", childHead, target], cancellationToken);
                if (ancestor.ExitCode == 1)
                {
                    return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
                }
                if (ancestor.ExitCode != 0)
                {
                    throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", ancestor);
                }
            }

            await VerifyPullParentAsync(repository.RootPath, beforePull.BranchName, parentHead, cancellationToken);
            await VerifyPullGitlinkAsync(repository.RootPath, path, target, cancellationToken);
            if (await VerifyPullSubmoduleRootAsync(repository, path, childRoot, cancellationToken) != gitDirectory)
            {
                return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
            }
            if (await IsPullSubmoduleCleanAsync(childRoot, gitDirectory, cancellationToken) == false)
            {
                return SkipPullSubmodule(path, "RemotePullSubmoduleDirty");
            }
            if (await ReadPullSubmoduleBranchAsync(childRoot, cancellationToken) != childBranch)
            {
                return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
            }
            string latestChildHead = (await ReadPullSubmoduleCommandAsync(childRoot, _verifyHeadArguments, cancellationToken)).Trim();
            if (latestChildHead != childHead)
            {
                return SkipPullSubmodule(path, "RemotePullSubmoduleUncertain");
            }
            await VerifyPullParentAsync(repository.RootPath, beforePull.BranchName, parentHead, cancellationToken);
            await VerifyPullGitlinkAsync(repository.RootPath, path, target, cancellationToken);
            if (HasPullOperationMarker(gitDirectory))
            {
                return SkipPullSubmodule(path, "RemotePullSubmoduleDirty");
            }
            cancellationToken.ThrowIfCancellationRequested();

            GitCommandResult checkout = await _runner.RunWithoutRemoteAccessAsync(childRoot, ["--no-replace-objects", "checkout", "--detach", "--no-recurse-submodules", "--no-overwrite-ignore", target, "--"], cancellationToken);
            if (checkout.ExitCode != 0)
            {
                throw CreatePullSubmoduleCommandError("RemotePullSubmoduleCheckoutFailed", checkout);
            }
            return new GitPullSubmoduleResult(path, GitPullSubmoduleOutcome.Updated);
        }

        private async Task VerifyPullParentAsync(string parentRoot, string branch, string head, CancellationToken cancellationToken)
        {
            string parentDirectory = await ReadPullGitDirectoryAsync(parentRoot, cancellationToken);
            string conflicts = await ReadPullSubmoduleCommandAsync(parentRoot, ["ls-files", "--unmerged", "-z"], cancellationToken);
            if (conflicts.Length > 0)
            {
                throw new GitException("RemotePullSubmoduleParentChanged", null, Array.Empty<object>());
            }
            if (HasPullOperationMarker(parentDirectory))
            {
                throw new GitException("RemotePullSubmoduleParentChanged", null, Array.Empty<object>());
            }
            string latestBranch = (await ReadPullSubmoduleCommandAsync(parentRoot, _currentBranchArguments, cancellationToken)).Trim();
            if (latestBranch != branch)
            {
                throw new GitException("RemotePullSubmoduleParentChanged", null, Array.Empty<object>());
            }
            string latestHead = (await ReadPullSubmoduleCommandAsync(parentRoot, _verifyHeadArguments, cancellationToken)).Trim();
            if (latestHead != head)
            {
                throw new GitException("RemotePullSubmoduleParentChanged", null, Array.Empty<object>());
            }
        }

        private async Task VerifyPullGitlinkAsync(string parentRoot, string path, string target, CancellationToken cancellationToken)
        {
            string link = await ReadPullSubmoduleCommandAsync(parentRoot, ["ls-files", "--stage", "-z", "--", ":(literal)" + path], cancellationToken);
            if (link != GetStageZeroGitlinkEntry(path, target) + "\0")
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
        }

        private static string GetStageZeroGitlinkEntry(string path, string target)
        {
            return "160000 " + target + " 0\t" + path;
        }

        private async Task<Dictionary<string, string>> ReadPullGitlinksAsync(string root, string commit, string path, CancellationToken cancellationToken)
        {
            List<string> arguments = ["ls-tree", "-r", "-z", commit];
            if (path != null)
            {
                arguments.Add("--");
                arguments.Add(":(literal)" + path);
            }
            string entries = await ReadPullSubmoduleCommandAsync(root, arguments, cancellationToken);
            Dictionary<string, string> links = new(StringComparer.Ordinal);
            foreach (string entry in entries.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                const string prefix = "160000 commit ";
                if (entry.StartsWith(prefix, StringComparison.Ordinal) == false)
                {
                    continue;
                }
                int separator = entry.IndexOf('\t');
                if (separator <= prefix.Length)
                {
                    throw new GitException("ReferenceSubmoduleConfigInvalid", null, Array.Empty<object>());
                }
                string relativePath = entry[(separator + 1)..];
                if (path != null)
                {
                    if (relativePath != path)
                    {
                        continue;
                    }
                }
                string objectId = entry[prefix.Length..separator];
                if (Regex.IsMatch(objectId, @"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z") == false)
                {
                    throw new GitException("ReferenceSubmoduleConfigInvalid", null, Array.Empty<object>());
                }
                links.Add(relativePath, objectId);
            }
            return links;
        }

        private static string GetSafePullSubmodulePath(string parentRoot, string path)
        {
            if (Path.IsPathRooted(path))
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            if (path.IndexOfAny(['\\', '\r', '\n', '\uFFFD']) >= 0)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            string childRoot = parentRoot;
            foreach (string part in path.Split('/'))
            {
                if (part is "" or "." or "..")
                {
                    throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                }
                if (string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase))
                {
                    throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                }
                if (OperatingSystem.IsWindows())
                {
                    if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                    }
                    if (part.TrimEnd(' ', '.') != part)
                    {
                        throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                    }
                    if (part.Contains('~'))
                    {
                        throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                    }
                }
                childRoot = Path.GetFullPath(Path.Combine(childRoot, part));
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(childRoot);
                }
                catch (FileNotFoundException)
                {
                    throw new GitException("SubmoduleOpenDirectoryUnavailable", null, path);
                }
                catch (DirectoryNotFoundException)
                {
                    throw new GitException("SubmoduleOpenDirectoryUnavailable", null, path);
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                }
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    throw new GitException("SubmoduleOpenDirectoryUnavailable", null, path);
                }
            }
            string canonical = Path.GetRelativePath(parentRoot, childRoot);
            if (OperatingSystem.IsWindows())
            {
                canonical = canonical.Replace('\\', '/');
            }
            if (canonical != path)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            return childRoot;
        }

        private async Task<string> VerifyPullSubmoduleRootAsync(GitRepository repository, string path, string childRoot, CancellationToken cancellationToken)
        {
            if (GetSafePullSubmodulePath(repository.RootPath, path) != childRoot)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            string opened = await _referenceService.GetSubmoduleRepositoryPathAsync(repository, path, cancellationToken);
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(childRoot, opened, comparison) == false)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            string superproject = (await ReadPullSubmoduleCommandAsync(childRoot, ["rev-parse", "--show-superproject-working-tree"], cancellationToken)).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(superproject))
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            if (Path.IsPathFullyQualified(superproject) == false)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            string expectedSuperprojectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.RootPath));
            string actualSuperprojectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(superproject));
            if (string.Equals(expectedSuperprojectRoot, actualSuperprojectRoot, comparison) == false)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            string marker = Path.Combine(childRoot, ".git");
            if ((File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            string gitDirectory = await ReadPullGitDirectoryAsync(childRoot, cancellationToken);
            string commonDirectory = (await ReadPullSubmoduleCommandAsync(childRoot, ["rev-parse", "--path-format=absolute", "--git-common-dir"], cancellationToken)).TrimEnd('\r', '\n');
            if (Path.IsPathFullyQualified(commonDirectory) == false)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            if (string.Equals(gitDirectory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonDirectory)), comparison) == false)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }

            // A normal submodule has its own .git directory or a gitfile into the parent's modules directory.
            if (string.Equals(gitDirectory, marker, comparison) == false)
            {
                string parentModules = (await ReadPullSubmoduleCommandAsync(repository.RootPath, ["rev-parse", "--path-format=absolute", "--git-path", "modules"], cancellationToken)).TrimEnd('\r', '\n');
                if (Path.IsPathFullyQualified(parentModules) == false)
                {
                    throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                }
                string modulesPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentModules)) + Path.DirectorySeparatorChar;
                if (gitDirectory.StartsWith(modulesPrefix, comparison) == false)
                {
                    throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
                }
            }
            GitCommandResult promisorSettings = await _runner.RunWithoutRemoteAccessAsync(childRoot, ["config", "--includes", "--name-only", "--get-regexp", @"^(extensions\.partialclone|remote\..*\.(promisor|partialclonefilter))$"], cancellationToken);
            if (promisorSettings.ExitCode == 0)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            if (promisorSettings.ExitCode != 1)
            {
                throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", promisorSettings);
            }
            if (string.IsNullOrWhiteSpace(promisorSettings.Error) == false)
            {
                throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", promisorSettings);
            }
            return gitDirectory;
        }

        private async Task<string> ReadPullGitDirectoryAsync(string root, CancellationToken cancellationToken)
        {
            string directory = (await ReadPullSubmoduleCommandAsync(root, ["rev-parse", "--absolute-git-dir"], cancellationToken)).TrimEnd('\r', '\n');
            if (Path.IsPathFullyQualified(directory) == false)
            {
                throw new GitException("RemotePullSubmoduleUncertain", null, Array.Empty<object>());
            }
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }

        private static bool HasPullOperationMarker(string gitDirectory)
        {
            foreach (string marker in _operationMarkers)
            {
                try
                {
                    File.GetAttributes(Path.Combine(gitDirectory, marker));
                    return true;
                }
                catch (FileNotFoundException)
                {
                }
            }
            return false;
        }

        private async Task<bool> IsPullSubmoduleCleanAsync(string root, string gitDirectory, CancellationToken cancellationToken)
        {
            if (HasPullOperationMarker(gitDirectory))
            {
                return false;
            }
            string indexFlags = await ReadPullSubmoduleCommandAsync(root, ["ls-files", "-v", "-z"], cancellationToken);
            if (indexFlags.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(entry => entry.StartsWith("H ", StringComparison.Ordinal) == false))
            {
                return false;
            }
            string status = await ReadPullSubmoduleCommandAsync(root, ["--no-optional-locks", "-c", "core.fsmonitor=false", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], cancellationToken);
            return status.Length == 0;
        }

        private async Task<string> ReadPullSubmoduleBranchAsync(string root, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(root, ["symbolic-ref", "--quiet", "HEAD"], true, cancellationToken);
            if (result.ExitCode == 1)
            {
                return string.Empty;
            }
            if (result.ExitCode != 0)
            {
                throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", result);
            }
            return result.Output.Trim();
        }

        private async Task<string> ReadPullSubmoduleCommandAsync(string root, IEnumerable<string> arguments, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunWithoutRemoteAccessAsync(root, arguments, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw CreatePullSubmoduleCommandError("RemotePullSubmoduleReadFailed", result);
            }
            return result.Output;
        }

        private static GitRemoteOperationException CreatePullSubmoduleCommandError(string key, GitCommandResult result)
        {
            return new GitRemoteOperationException(key, RedactRemoteDiagnostic(result.Error.Trim()), result.ExitCode);
        }

        private static Exception GetSafePullSubmoduleError(Exception exception)
        {
            if (exception is GitException gitException)
            {
                if (string.IsNullOrEmpty(gitException.ErrorCode) == false)
                {
                    if (gitException.ErrorCode != GitException.ProcessStartFailedCode)
                    {
                        return gitException;
                    }
                }
            }
            return new GitException("RemotePullSubmoduleUnexpected", exception, Array.Empty<object>());
        }

        private static GitPullSubmoduleResult SkipPullSubmodule(string path, string reason)
        {
            return new GitPullSubmoduleResult(path, GitPullSubmoduleOutcome.Skipped, new GitException(reason, null, Array.Empty<object>()));
        }

        private static bool IsTrackedLocalChangesOverwrite(string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
            {
                return false;
            }
            if (diagnostic.Contains("Your local changes to the following files would be overwritten by merge", StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }
            return diagnostic.Contains("Please commit your changes or stash them before you merge.", StringComparison.OrdinalIgnoreCase);
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
            return RedactRemoteDiagnostic(message);
        }

        private static string RedactRemoteDiagnostic(string message)
        {
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
            return new GitRemoteOperationException(code + "Summary", error, remote, branch);
        }
    }
}
