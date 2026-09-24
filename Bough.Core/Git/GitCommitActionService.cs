using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bough.Core.Git
{
    public enum GitResetMode
    {
        Soft,
        Mixed,
        Hard
    }

    public class GitResetPreview
    {
        public GitResetPreview(string repositoryRoot, string branchName, string headHash, string targetHash, string targetSubject, string statusSnapshot, string stateFingerprint, string statusDescription, bool isAncestor)
        {
            RepositoryRoot = repositoryRoot;
            BranchName = branchName;
            HeadHash = headHash;
            TargetHash = targetHash;
            TargetSubject = targetSubject;
            StatusSnapshot = statusSnapshot;
            StateFingerprint = stateFingerprint;
            StatusDescription = statusDescription;
            IsAncestor = isAncestor;
        }

        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string HeadHash { get; }
        public string TargetHash { get; }
        public string TargetSubject { get; }
        public string StatusSnapshot { get; }
        public string StateFingerprint { get; }
        public string StatusDescription { get; }
        public bool IsAncestor { get; }
        public string ShortHash { get { return TargetHash.Substring(0, 8); } }
    }

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
                throw new GitException($"브랜치가 메뉴를 연 뒤 변경되었습니다: {branchName}");
            }

            await _runner.RunAsync(repository.RootPath, new string[] { "switch", branchName }, false, cancellationToken);
            return await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
        }

        public async Task<GitRepository> CreateFromBranchAsync(GitRepository repository, string sourceBranch, string expectedHash, string newBranch, bool switchToBranch, CancellationToken cancellationToken = default)
        {
            string currentHash = await ReadRefAsync(repository, $"refs/heads/{sourceBranch}", cancellationToken);
            if (currentHash != expectedHash)
            {
                throw new GitException($"시작 브랜치가 메뉴를 연 뒤 변경되었습니다: {sourceBranch}");
            }
            return await CreateBranchAsync(repository, newBranch, expectedHash, switchToBranch, cancellationToken);
        }

        public async Task<GitRepository> TrackRemoteAsync(GitRepository repository, GitRemoteBranch branch, string localName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(branch);
            string currentHash = await ReadRefAsync(repository, $"refs/remotes/{branch.FullName}", cancellationToken);
            if (currentHash != branch.CommitHash)
            {
                throw new GitException($"원격 브랜치가 메뉴를 연 뒤 변경되었습니다: {branch.FullName}");
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
                throw new GitException("Detached HEAD에서는 현재 브랜치를 초기화할 수 없습니다.");
            }

            string branch = branchResult.Output.Trim();
            string head = await ReadRefAsync(repository, "HEAD", cancellationToken);
            if (head == target)
            {
                throw new GitException("대상 커밋이 현재 HEAD와 같습니다.");
            }

            GitCommandResult ancestor = await _runner.RunAsync(repository.RootPath, new string[] { "merge-base", "--is-ancestor", target, head }, true, cancellationToken);
            if (ancestor.ExitCode != 0 && ancestor.ExitCode != 1)
            {
                throw new GitException($"커밋 관계를 확인하지 못했습니다: {ancestor.Error.Trim()}");
            }

            GitCommandResult subject = await _runner.RunAsync(repository.RootPath, new string[] { "show", "-s", "--format=%s", target }, false, cancellationToken);
            GitCommandResult status = await _runner.RunAsync(repository.RootPath, _statusArguments, false, cancellationToken);
            GitCommandResult staged = await _runner.RunAsync(repository.RootPath, _stagedNamesArguments, false, cancellationToken);
            GitCommandResult working = await _runner.RunAsync(repository.RootPath, _workingNamesArguments, false, cancellationToken);
            GitCommandResult untracked = await _runner.RunAsync(repository.RootPath, _untrackedNamesArguments, false, cancellationToken);
            string fingerprint = await ReadStateFingerprintAsync(repository, status.Output, cancellationToken);
            string description = $"Staged {CountNames(staged.Output)}, working files {CountNames(working.Output)}, untracked {CountNames(untracked.Output)}";
            return new GitResetPreview(repository.RootPath, branch, head, target, subject.Output.Trim(), status.Output, fingerprint, description, ancestor.ExitCode == 0);
        }

        public async Task<GitRepository> ResetAsync(GitRepository repository, GitResetPreview preview, GitResetMode mode, bool hardConfirmed, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(preview);
            if (repository.RootPath != preview.RepositoryRoot)
            {
                throw new GitException("초기화 대상 저장소가 바뀌었습니다.");
            }
            if (mode == GitResetMode.Hard && hardConfirmed == false)
            {
                throw new GitException("Hard 초기화에는 추가 확인이 필요합니다.");
            }
            if (Enum.IsDefined(mode) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            GitCommandResult branch = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            if (branch.ExitCode != 0)
            {
                throw new GitException("현재 HEAD가 브랜치를 가리키지 않습니다.");
            }
            if (branch.Output.Trim() != preview.BranchName)
            {
                throw new GitException("현재 브랜치가 확인 이후 변경되었습니다.");
            }
            string head = await ReadRefAsync(repository, "HEAD", cancellationToken);
            if (head != preview.HeadHash)
            {
                throw new GitException("HEAD가 확인 이후 변경되었습니다.");
            }
            string target = await VerifyCommitAsync(repository, preview.TargetHash, cancellationToken);
            if (target != preview.TargetHash)
            {
                throw new GitException("대상 커밋이 확인 이후 변경되었습니다.");
            }
            GitCommandResult status = await _runner.RunAsync(repository.RootPath, _statusArguments, false, cancellationToken);
            if (status.Output != preview.StatusSnapshot)
            {
                throw new GitException("작업 트리 또는 인덱스가 확인 이후 변경되었습니다.");
            }
            string fingerprint = await ReadStateFingerprintAsync(repository, status.Output, cancellationToken);
            if (fingerprint != preview.StateFingerprint)
            {
                throw new GitException("작업 파일 또는 인덱스 내용이 확인 이후 변경되었습니다.");
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
                throw new ArgumentException("커밋 해시가 없습니다.", nameof(commitHash));
            }
            if (commitHash.Length != 40 && commitHash.Length != 64)
            {
                throw new ArgumentException($"커밋 해시 길이가 올바르지 않습니다: {commitHash.Length}", nameof(commitHash));
            }
            if (commitHash.All(Uri.IsHexDigit) == false)
            {
                throw new ArgumentException($"커밋 해시가 16진수 형식이 아닙니다: {commitHash}", nameof(commitHash));
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "rev-parse", "--verify", "--quiet", $"{commitHash}^{{commit}}" }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException($"대상 커밋을 찾을 수 없습니다: {commitHash}");
            }
            return result.Output.Trim();
        }

        private async Task VerifyBranchNameAsync(GitRepository repository, string branchName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "check-ref-format", "--branch", branchName }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException($"브랜치 이름이 올바르지 않습니다: {branchName}. {result.Error.Trim()}");
            }
        }

        private async Task<string> ReadRefAsync(GitRepository repository, string reference, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "rev-parse", "--verify", "--quiet", reference }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException($"참조를 찾을 수 없습니다: {reference}");
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
