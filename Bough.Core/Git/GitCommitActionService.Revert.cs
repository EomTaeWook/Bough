using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.Core.Git
{
    public partial class GitCommitActionService
    {
        private static readonly string[] _revertHeadPathArguments = new string[] { "rev-parse", "--git-path", "REVERT_HEAD" };
        private static readonly string[] _revertSequencerPathArguments = new string[] { "rev-parse", "--git-path", "sequencer" };
        private static readonly string[] _revertContinueArguments = new string[] { "-c", "core.editor=true", "revert", "--continue" };
        private static readonly string[] _revertAbortArguments = new string[] { "revert", "--abort" };
        private static readonly string[] _revertWorktreeStatusArguments = new string[] { "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none" };
        private static readonly string[] _revertStagedPatchArguments = new string[] { "diff", "--cached", "--binary", "--no-ext-diff", "--no-textconv" };
        private static readonly string[] _revertWorkingPatchArguments = new string[] { "diff", "--binary", "--no-ext-diff", "--no-textconv" };
        private static readonly string[] _revertOtherHeads = new string[] { "MERGE_HEAD", "CHERRY_PICK_HEAD" };
        private static readonly string[] _revertIndexLockArguments = new string[] { "rev-parse", "--git-path", "index.lock" };

        public async Task<GitRevertPreview> GetRevertPreviewAsync(GitRepository repository, string commitHash, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitRepository opened = await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
            RequireRevertRoot(opened, repository.RootPath);
            string target = await VerifyCommitAsync(repository, commitHash, cancellationToken);
            await RequireRevertStartReadyAsync(repository, cancellationToken);
            string branch = await ReadRevertBranchAsync(repository, cancellationToken);
            string head = await ReadRefAsync(repository, "HEAD", cancellationToken);
            GitCommandResult details = await _runner.RunAsync(repository.RootPath,
                new string[] { "show", "-s", "--format=%P%x00%s", target }, false, cancellationToken);
            int boundary = details.Output.IndexOf('\0');
            if (boundary < 0)
            {
                throw new GitException("CommitRevertDetailsFailed", null, Array.Empty<object>());
            }
            string[] parentHashes = details.Output.Substring(0, boundary).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<GitRevertParent> parents = new();
            for (int index = 0; index < parentHashes.Length; index++)
            {
                GitCommandResult parent = await _runner.RunAsync(repository.RootPath,
                    new string[] { "show", "-s", "--format=%s", parentHashes[index] }, false, cancellationToken);
                parents.Add(new GitRevertParent(index + 1, parentHashes[index], parent.Output.TrimEnd('\r', '\n')));
            }
            return new GitRevertPreview(repository.RootPath, branch, head, target,
                details.Output.Substring(boundary + 1).TrimEnd('\r', '\n'), parents.AsReadOnly());
        }

        public async Task<GitRevertState> GetRevertStateAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            string markerPath = await ReadRevertMetadataPathAsync(repository, _revertHeadPathArguments, cancellationToken);
            string sequencerPath = await ReadRevertMetadataPathAsync(repository, _revertSequencerPathArguments, cancellationToken);
            string marker = await ReadRevertMetadataAsync(markerPath, cancellationToken);
            string todo = await ReadRevertMetadataAsync(Path.Combine(sequencerPath, "todo"), cancellationToken);
            string target = marker.Trim();
            if (target.Length == 0)
            {
                string instruction = todo.Split('\n').FirstOrDefault(line => string.IsNullOrWhiteSpace(line) == false && line.TrimStart().StartsWith('#') == false);
                if (instruction != null)
                {
                    string[] parts = instruction.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        if (parts[0] == "revert")
                        {
                            if (parts[1].All(Uri.IsHexDigit) == false)
                            {
                                throw new GitException("CommitRevertStateInvalid", null, Array.Empty<object>());
                            }
                            GitCommandResult resolved = await _runner.RunAsync(repository.RootPath,
                                new string[] { "rev-parse", "--verify", "--quiet", parts[1] + "^{commit}" }, false, cancellationToken);
                            target = resolved.Output.Trim();
                        }
                    }
                }
            }
            if (target.Length == 0)
            {
                return new GitRevertState(repository.RootPath, repository.CurrentBranch, null, null, null, Array.Empty<string>());
            }
            target = await VerifyCommitAsync(repository, target, cancellationToken);
            string sequencerHead = await ReadRevertMetadataAsync(Path.Combine(sequencerPath, "head"), cancellationToken);
            string options = await ReadRevertMetadataAsync(Path.Combine(sequencerPath, "opts"), cancellationToken);
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(marker + "\0" + todo + "\0" + sequencerHead + "\0" + options)));
            string head = await ReadRefAsync(repository, "HEAD", cancellationToken);
            GitCommandResult branchResult = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            string branch = "Detached HEAD";
            if (branchResult.ExitCode == 0)
            {
                branch = branchResult.Output.Trim();
            }
            IReadOnlyList<string> conflicts = await _repositoryService.GetConflictPathsAsync(repository, cancellationToken);
            string worktreeFingerprint = await ReadRevertWorktreeFingerprintAsync(repository, cancellationToken);
            return new GitRevertState(repository.RootPath, branch, head, target, fingerprint, conflicts, worktreeFingerprint);
        }

        public async Task<GitRevertResult> RevertAsync(GitRepository repository, GitRevertPreview preview, int mainlineParent, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preview);
            RequireRevertRoot(repository, preview.RepositoryRoot);
            GitRevertPreview current = await GetRevertPreviewAsync(repository, preview.TargetHash, cancellationToken);
            if (current.BranchName != preview.BranchName)
            {
                throw new GitException("CommitRevertTargetChanged", null, Array.Empty<object>());
            }
            if (current.HeadHash != preview.HeadHash)
            {
                throw new GitException("CommitRevertTargetChanged", null, Array.Empty<object>());
            }
            List<string> arguments = new() { "revert", "--no-edit" };
            if (current.Parents.Count > 1)
            {
                if (mainlineParent < 1)
                {
                    throw new GitException("CommitRevertParentRequired", null, Array.Empty<object>());
                }
                if (mainlineParent > current.Parents.Count)
                {
                    throw new GitException("CommitRevertParentRequired", null, Array.Empty<object>());
                }
                arguments.Add("--mainline");
                arguments.Add(mainlineParent.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                if (mainlineParent != 0)
                {
                    throw new GitException("CommitRevertParentRequired", null, Array.Empty<object>());
                }
            }
            arguments.Add(current.TargetHash);
            return await RunRevertCommandAsync(repository, arguments, GitRevertOutcome.Completed, cancellationToken);
        }

        public async Task<GitRevertResult> ContinueRevertAsync(GitRepository repository, GitRevertState expectedState, CancellationToken cancellationToken = default)
        {
            GitRevertState current = await RequireCurrentRevertAsync(repository, expectedState, cancellationToken);
            if (current.ConflictPaths.Count > 0)
            {
                throw new GitException("CommitRevertUnresolvedConflicts", null, current.ConflictPaths.Count);
            }
            GitCommandResult working = await _runner.RunAsync(repository.RootPath, _revertWorktreeStatusArguments, false, cancellationToken);
            string[] entries = working.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < entries.Length; index++)
            {
                string entry = entries[index];
                if (entry.Length < 3)
                {
                    throw new GitException("CommitRevertStateInvalid", null, Array.Empty<object>());
                }
                if (entry[1] != ' ')
                {
                    throw new GitException("CommitRevertUnstagedChanges", null, Array.Empty<object>());
                }
                if (entry[0] == 'R' || entry[0] == 'C')
                {
                    index++;
                }
            }
            return await RunRevertCommandAsync(repository, _revertContinueArguments, GitRevertOutcome.Completed, cancellationToken);
        }

        public async Task<GitRevertResult> AbortRevertAsync(GitRepository repository, GitRevertState expectedState, CancellationToken cancellationToken = default)
        {
            await RequireCurrentRevertAsync(repository, expectedState, cancellationToken);
            return await RunRevertCommandAsync(repository, _revertAbortArguments, GitRevertOutcome.Aborted, cancellationToken);
        }

        private async Task RequireRevertStartReadyAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitRevertState state = await GetRevertStateAsync(repository, cancellationToken);
            if (state.IsInProgress)
            {
                throw new GitException("CommitRevertAlreadyInProgress", null, Array.Empty<object>());
            }
            string sequencerPath = await ReadRevertMetadataPathAsync(repository, _revertSequencerPathArguments, cancellationToken);
            if (Directory.Exists(sequencerPath))
            {
                throw new GitException("CommitRevertOtherOperation", null, Array.Empty<object>());
            }
            if (await _repositoryService.IsRebaseInProgressAsync(repository, cancellationToken))
            {
                throw new GitException("CommitRevertOtherOperation", null, Array.Empty<object>());
            }
            foreach (string otherHead in _revertOtherHeads)
            {
                GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                    new string[] { "rev-parse", "--verify", "--quiet", otherHead }, true, cancellationToken);
                if (result.ExitCode == 0)
                {
                    throw new GitException("CommitRevertOtherOperation", null, Array.Empty<object>());
                }
                if (result.ExitCode != 1)
                {
                    throw new GitException("CommitRevertStateInvalid", null, Array.Empty<object>());
                }
            }
            string indexLock = await ReadRevertMetadataPathAsync(repository, _revertIndexLockArguments, cancellationToken);
            if (File.Exists(indexLock))
            {
                throw new GitException("CommitRevertOtherOperation", null, Array.Empty<object>());
            }
            GitCommandResult status = await _runner.RunAsync(repository.RootPath, _revertWorktreeStatusArguments, false, cancellationToken);
            if (status.Output.Length != 0)
            {
                throw new GitException("CommitRevertCleanRequired", null, Array.Empty<object>());
            }
        }

        private async Task<string> ReadRevertBranchAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult branch = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            if (branch.ExitCode != 0)
            {
                throw new GitException("CommitRevertDetachedHead", null, Array.Empty<object>());
            }
            return branch.Output.Trim();
        }

        private async Task<GitRevertState> RequireCurrentRevertAsync(GitRepository repository, GitRevertState expectedState, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(expectedState);
            RequireRevertRoot(repository, expectedState.RepositoryRoot);
            GitRepository opened = await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
            RequireRevertRoot(opened, expectedState.RepositoryRoot);
            if (await _repositoryService.IsRebaseInProgressAsync(opened, cancellationToken))
            {
                throw new GitException("CommitRevertOtherOperation", null, Array.Empty<object>());
            }
            foreach (string otherHead in _revertOtherHeads)
            {
                GitCommandResult other = await _runner.RunAsync(repository.RootPath,
                    new string[] { "rev-parse", "--verify", "--quiet", otherHead }, true, cancellationToken);
                if (other.ExitCode == 0)
                {
                    throw new GitException("CommitRevertOtherOperation", null, Array.Empty<object>());
                }
                if (other.ExitCode != 1)
                {
                    throw new GitException("CommitRevertStateInvalid", null, Array.Empty<object>());
                }
            }
            GitRevertState current = await GetRevertStateAsync(repository, cancellationToken);
            if (current.IsInProgress == false)
            {
                throw new GitException("CommitRevertNoLongerInProgress", null, Array.Empty<object>());
            }
            if (current.TargetHash != expectedState.TargetHash)
            {
                throw new GitException("CommitRevertStateChanged", null, Array.Empty<object>());
            }
            if (current.HeadHash != expectedState.HeadHash)
            {
                throw new GitException("CommitRevertStateChanged", null, Array.Empty<object>());
            }
            if (current.BranchName != expectedState.BranchName)
            {
                throw new GitException("CommitRevertStateChanged", null, Array.Empty<object>());
            }
            if (current.OperationFingerprint != expectedState.OperationFingerprint)
            {
                throw new GitException("CommitRevertStateChanged", null, Array.Empty<object>());
            }
            if (current.WorktreeFingerprint != expectedState.WorktreeFingerprint)
            {
                throw new GitException("CommitRevertStateChanged", null, Array.Empty<object>());
            }
            return current;
        }

        private async Task<GitRevertResult> RunRevertCommandAsync(GitRepository repository, IEnumerable<string> arguments,
            GitRevertOutcome completedOutcome, CancellationToken cancellationToken)
        {
            GitCommandResult command = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
            GitRevertOutcome outcome = completedOutcome;
            Exception operationError = null;
            if (command.ExitCode != 0)
            {
                outcome = GitRevertOutcome.Failed;
                operationError = new GitException("CommitRevertFailed", new GitException(command.Error), command.ExitCode);
            }
            GitRepository updated = null;
            Exception readError = null;
            try
            {
                updated = await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
            }
            catch (Exception exception)
            {
                readError = exception;
            }
            GitRevertState state = null;
            try
            {
                state = await GetRevertStateAsync(repository, cancellationToken);
                if (command.ExitCode != 0)
                {
                    if (state.IsInProgress)
                    {
                        outcome = GitRevertOutcome.Paused;
                    }
                }
            }
            catch (Exception exception)
            {
                if (readError == null)
                {
                    readError = exception;
                }
                else
                {
                    readError = new AggregateException(readError, exception);
                }
            }
            return new GitRevertResult(repository.RootPath, outcome, updated, state, readError, operationError);
        }

        private async Task<string> ReadRevertWorktreeFingerprintAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult status = await _runner.RunAsync(repository.RootPath, _revertWorktreeStatusArguments, false, cancellationToken);
            GitCommandResult staged = await _runner.RunAsync(repository.RootPath, _revertStagedPatchArguments, false, cancellationToken);
            GitCommandResult working = await _runner.RunAsync(repository.RootPath, _revertWorkingPatchArguments, false, cancellationToken);
            string content = status.Output + "\0" + staged.Output + "\0" + working.Output;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        }

        private static void RequireRevertRoot(GitRepository repository, string expectedRoot)
        {
            ArgumentNullException.ThrowIfNull(repository);
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(repository.RootPath, expectedRoot, comparison) == false)
            {
                throw new GitException("CommitRevertRepositoryChanged", null, Array.Empty<object>());
            }
        }

        private async Task<string> ReadRevertMetadataPathAsync(GitRepository repository, string[] arguments, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
            return Path.GetFullPath(Path.Combine(repository.RootPath, result.Output.Trim()));
        }

        private static async Task<string> ReadRevertMetadataAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                return await File.ReadAllTextAsync(path, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return string.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return string.Empty;
            }
        }
    }
}
