using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.Core.Git
{
    public class GitCommitActionService
    {
        private readonly GitCommandRunner _runner;
        private readonly GitRepositoryService _repositoryService;
        private readonly GitReferenceService _referenceService;
        private static readonly string[] _currentBranchArguments = new string[] { "symbolic-ref", "--quiet", "--short", "HEAD" };
        private static readonly string[] _statusArguments = new string[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" };
        private static readonly string[] _stagedNamesArguments = new string[] { "diff", "--cached", "--name-only", "-z" };
        private static readonly string[] _workingNamesArguments = new string[] { "diff", "--name-only", "-z" };
        private static readonly string[] _untrackedNamesArguments = new string[] { "ls-files", "--others", "--exclude-standard", "-z" };
        private static readonly string[] _stagedPatchArguments = new string[] { "diff", "--cached", "--binary" };
        private static readonly string[] _workingPatchArguments = new string[] { "diff", "--binary" };

        public GitCommitActionService(GitCommandRunner runner, GitRepositoryService repositoryService, GitReferenceService referenceService)
        {
            _runner = runner;
            _repositoryService = repositoryService;
            _referenceService = referenceService;
        }

        public Task CreateLightweightTagAsync(GitRepository repository, string tagName, string commitHash, CancellationToken cancellationToken = default)
        {
            return _referenceService.CreateLightweightTagAsync(repository, tagName, commitHash, cancellationToken);
        }

        public async Task<GitRepository> SwitchDetachedAsync(GitRepository repository, string commitHash, CancellationToken cancellationToken = default)
        {
            string target = await VerifyCommitAsync(repository, commitHash, cancellationToken);
            await _runner.RunAsync(repository.RootPath, new string[] { "switch", "--detach", target }, false, cancellationToken);
            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        public async Task<GitRepository> CreateBranchAsync(GitRepository repository, string branchName, string startHash, bool switchToBranch, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
            string target = await VerifyCommitAsync(repository, startHash, cancellationToken);
            await VerifyBranchNameAsync(repository, branchName, cancellationToken);
            if (switchToBranch == true)
            {
                await _runner.RunAsync(repository.RootPath, new string[] { "switch", "-c", branchName, target }, false, cancellationToken);
            }
            else
            {
                await _runner.RunAsync(repository.RootPath, new string[] { "branch", branchName, target }, false, cancellationToken);
            }

            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        public async Task<GitRepository> SwitchBranchAsync(GitRepository repository, string branchName, string expectedHash, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            string currentHash = await ReadRefAsync(repository, $"refs/heads/{branchName}", cancellationToken);
            if (currentHash != expectedHash)
            {
                throw new GitException("CommitBranchChanged", null, branchName);
            }

            await _runner.RunAsync(repository.RootPath, new string[] { "switch", branchName }, false, cancellationToken);
            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        public async Task<GitRepository> CreateFromBranchAsync(GitRepository repository, string sourceBranch, string expectedHash, string newBranch, bool switchToBranch, CancellationToken cancellationToken = default)
        {
            string currentHash = await ReadRefAsync(repository, $"refs/heads/{sourceBranch}", cancellationToken);
            if (currentHash != expectedHash)
            {
                throw new GitException("CommitSourceBranchChanged", null, sourceBranch);
            }
            return await CreateBranchAsync(repository, newBranch, expectedHash, switchToBranch, cancellationToken);
        }

        public async Task<GitRepository> TrackRemoteAsync(GitRepository repository, GitRemoteBranch branch, string localName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(branch);
            string currentHash = await ReadRefAsync(repository, $"refs/remotes/{branch.FullName}", cancellationToken);
            if (currentHash != branch.CommitHash)
            {
                throw new GitException("CommitRemoteBranchChanged", null, branch.FullName);
            }

            await VerifyBranchNameAsync(repository, localName, cancellationToken);
            await _runner.RunAsync(repository.RootPath, new string[] { "switch", "--track", "-c", localName, branch.FullName }, false, cancellationToken);
            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        public async Task<GitResetPreview> GetResetPreviewAsync(GitRepository repository, string commitHash, CancellationToken cancellationToken = default)
        {
            string target = await VerifyCommitAsync(repository, commitHash, cancellationToken);
            GitCommandResult branchResult = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            if (branchResult.ExitCode != 0)
            {
                throw new GitException("CommitResetDetachedHead", null, Array.Empty<object>());
            }

            string branch = branchResult.Output.Trim();
            string head = await ReadRefAsync(repository, "HEAD", cancellationToken);
            if (head == target)
            {
                throw new GitException("CommitResetTargetIsHead", null, Array.Empty<object>());
            }

            GitCommandResult ancestor = await _runner.RunAsync(repository.RootPath, new string[] { "merge-base", "--is-ancestor", target, head }, true, cancellationToken);
            if (ancestor.ExitCode != 0 && ancestor.ExitCode != 1)
            {
                throw new GitException("CommitAncestryCheckFailed", null, ancestor.Error.Trim());
            }

            GitCommandResult subject = await _runner.RunAsync(repository.RootPath, new string[] { "show", "-s", "--format=%s", target }, false, cancellationToken);
            GitCommandResult status = await _runner.RunAsync(repository.RootPath, _statusArguments, false, cancellationToken);
            GitCommandResult staged = await _runner.RunAsync(repository.RootPath, _stagedNamesArguments, false, cancellationToken);
            GitCommandResult working = await _runner.RunAsync(repository.RootPath, _workingNamesArguments, false, cancellationToken);
            GitCommandResult untracked = await _runner.RunAsync(repository.RootPath, _untrackedNamesArguments, false, cancellationToken);
            string fingerprint = await ReadStateFingerprintAsync(repository, status.Output, cancellationToken);
            return new GitResetPreview(repository.RootPath, branch, head, target, subject.Output.Trim(), status.Output, fingerprint,
                CountNames(staged.Output), CountNames(working.Output), CountNames(untracked.Output), ancestor.ExitCode == 0);
        }

        public async Task<GitRepository> ResetAsync(GitRepository repository, GitResetPreview preview, GitResetMode mode, bool hardConfirmed, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(preview);
            if (repository.RootPath != preview.RepositoryRoot)
            {
                throw new GitException("CommitResetRepositoryChanged", null, Array.Empty<object>());
            }
            if (mode == GitResetMode.Hard && hardConfirmed == false)
            {
                throw new GitException("CommitResetHardConfirmationRequired", null, Array.Empty<object>());
            }
            if (Enum.IsDefined(mode) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            GitCommandResult branch = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            if (branch.ExitCode != 0)
            {
                throw new GitException("CommitResetHeadNotBranch", null, Array.Empty<object>());
            }
            if (branch.Output.Trim() != preview.BranchName)
            {
                throw new GitException("CommitResetBranchChanged", null, Array.Empty<object>());
            }
            string head = await ReadRefAsync(repository, "HEAD", cancellationToken);
            if (head != preview.HeadHash)
            {
                throw new GitException("CommitResetHeadChanged", null, Array.Empty<object>());
            }
            string target = await VerifyCommitAsync(repository, preview.TargetHash, cancellationToken);
            if (target != preview.TargetHash)
            {
                throw new GitException("CommitResetTargetChanged", null, Array.Empty<object>());
            }
            GitCommandResult status = await _runner.RunAsync(repository.RootPath, _statusArguments, false, cancellationToken);
            if (status.Output != preview.StatusSnapshot)
            {
                throw new GitException("CommitResetStatusChanged", null, Array.Empty<object>());
            }
            string fingerprint = await ReadStateFingerprintAsync(repository, status.Output, cancellationToken);
            if (fingerprint != preview.StateFingerprint)
            {
                throw new GitException("CommitResetContentChanged", null, Array.Empty<object>());
            }

            string option = "--soft";
            if (mode == GitResetMode.Mixed)
            {
                option = "--mixed";
            }
            if (mode == GitResetMode.Hard)
            {
                option = "--hard";
            }
            await _runner.RunAsync(repository.RootPath, new string[] { "reset", option, target }, false, cancellationToken);
            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        private async Task<string> VerifyCommitAsync(GitRepository repository, string commitHash, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (commitHash == null)
            {
                throw new GitException("CommitHashRequired", null, Array.Empty<object>());
            }
            if (commitHash.Length != 40 && commitHash.Length != 64)
            {
                throw new GitException("CommitHashLengthInvalid", null, commitHash.Length);
            }
            if (commitHash.All(Uri.IsHexDigit) == false)
            {
                throw new GitException("CommitHashHexInvalid", null, commitHash);
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "rev-parse", "--verify", "--quiet", $"{commitHash}^{{commit}}" }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("CommitTargetNotFound", null, commitHash);
            }
            return result.Output.Trim();
        }

        private async Task VerifyBranchNameAsync(GitRepository repository, string branchName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "check-ref-format", "--branch", branchName }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("CommitBranchNameInvalid", null, branchName, result.Error.Trim());
            }
        }

        private async Task<string> ReadRefAsync(GitRepository repository, string reference, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "rev-parse", "--verify", "--quiet", reference }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("CommitReferenceNotFound", null, reference);
            }
            return result.Output.Trim();
        }

        private static int CountNames(string output)
        {
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private async Task<string> ReadStateFingerprintAsync(GitRepository repository, string status, CancellationToken cancellationToken)
        {
            GitCommandResult index = await _runner.RunAsync(repository.RootPath, _stagedPatchArguments, false, cancellationToken);
            GitCommandResult worktree = await _runner.RunAsync(repository.RootPath, _workingPatchArguments, false, cancellationToken);
            byte[] bytes = Encoding.UTF8.GetBytes(status + "\0" + index.Output + "\0" + worktree.Output);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
    }
}
