using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class GitReferenceService
    {
        private static readonly string[] _remoteArguments = new string[] { "remote" };
        private static readonly string[] _allReferencesArguments = new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(HEAD)%00%(objecttype)%00%(*objectname)%00%(*objecttype)%00%(upstream)%00%(upstream:remotename)%00%(upstream:remoteref)%00%(symref)", "refs/heads", "refs/remotes", "refs/tags" };
        private static readonly string[] _currentBranchArguments = new string[] { "symbolic-ref", "--quiet", "--short", "HEAD" };
        private static readonly string[] _localReferencesArguments = new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(HEAD)%00%(upstream)%00%(upstream:remotename)%00%(upstream:remoteref)", "refs/heads" };
        private static readonly string[] _submoduleConfigArguments = new string[] { "config", "--null", "--file", ".gitmodules", "--get-regexp", "^submodule\\..*\\.(path|url)$" };
        private static readonly string[] _submoduleTreeArguments = new string[] { "ls-tree", "-r", "-z", "HEAD" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "HEAD" };
        private static readonly string[] _renameTagTransactionArguments = new string[] { "update-ref", "--no-deref", "--stdin" };
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
            Dictionary<string, string> remoteDefaultBranches = new(StringComparer.Ordinal);
            foreach (string remoteName in remoteNames)
            {
                remoteBranches.Add(remoteName, []);
            }

            ArrayQueue<GitTag> tags = [];
            GitCommandResult referenceResult = await _runner.RunAsync(repository.RootPath,
                _allReferencesArguments, false, cancellationToken);
            bool foundCurrentBranch = false;
            foreach (string[] fields in ParseRows(referenceResult.Output, 10))
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
                    tags.Add(new GitTag(fields[0]["refs/tags/".Length..], commitHash, fields[1]));
                    continue;
                }

                const string prefix = "refs/remotes/";
                if (fields[0].StartsWith(prefix, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string fullName = fields[0][prefix.Length..];
                int separator = fullName.IndexOf('/');
                if (separator < 1)
                {
                    continue;
                }

                string remoteName = fullName[..separator];
                if (fullName.EndsWith("/HEAD", StringComparison.Ordinal) == true)
                {
                    const string remotePrefix = "refs/remotes/";
                    string symbolicReference = fields[9];
                    string expectedPrefix = remotePrefix + remoteName + "/";
                    if (symbolicReference.StartsWith(expectedPrefix, StringComparison.Ordinal) == true)
                    {
                        remoteDefaultBranches[remoteName] = symbolicReference[expectedPrefix.Length..];
                    }
                    continue;
                }
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
                remoteDefaultBranches.TryGetValue(remoteName, out string defaultBranch);
                remotes.Add(new GitRemote(remoteName, url, remoteBranches[remoteName].ToArray(), defaultBranch));
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

        public async Task DeleteLocalBranchAsync(GitRepository repository, GitLocalBranch branch, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(branch);

            GitCommandResult current = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            if (current.ExitCode == 0)
            {
                if (string.Equals(current.Output.Trim(), branch.Name, StringComparison.Ordinal))
                {
                    throw new GitException("ReferenceLocalBranchDeleteCurrent", null, branch.Name);
                }
            }

            string reference = $"refs/heads/{branch.Name}";
            GitCommandResult existing = await _runner.RunAsync(repository.RootPath,
                new string[] { "show-ref", "--verify", "--hash", reference }, true, cancellationToken);
            if (existing.ExitCode == 1)
            {
                throw new GitException("ReferenceLocalBranchDeleteMissing", null, branch.Name);
            }
            if (existing.ExitCode != 0)
            {
                string error = existing.Error.Trim();
                if (error.Length == 0)
                {
                    throw new GitException("ReferenceLocalBranchDeleteFailedWithoutOutput", null, branch.Name, existing.ExitCode);
                }
                throw new GitException("ReferenceLocalBranchDeleteFailed", null, branch.Name, error);
            }
            if (string.Equals(existing.Output.Trim(), branch.CommitHash, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new GitException("ReferenceLocalBranchDeleteChanged", null, branch.Name);
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "branch", "-d", "--", branch.Name }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                string error = result.Error.Trim();
                if (error.Length == 0)
                {
                    throw new GitException("ReferenceLocalBranchDeleteFailedWithoutOutput", null, branch.Name, result.ExitCode);
                }
                throw new GitException("ReferenceLocalBranchDeleteFailed", null, branch.Name, error);
            }
        }

        public async Task DeleteRemoteBranchAsync(GitRepository repository, GitRemoteBranch branch, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(branch);

            string branchReference = $"refs/heads/{branch.Name}";
            GitCommandResult remote = await _runner.RunAsync(repository.RootPath,
                new string[] { "ls-remote", "--symref", branch.RemoteName, "HEAD", branchReference }, false, cancellationToken);
            string currentHash = string.Empty;
            foreach (string line in remote.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line == $"ref: {branchReference}\tHEAD")
                {
                    throw new GitException("ReferenceRemoteBranchDeleteDefault", null, branch.FullName);
                }
                string suffix = $"\t{branchReference}";
                if (line.EndsWith(suffix, StringComparison.Ordinal))
                {
                    currentHash = line[..^suffix.Length];
                }
            }
            if (currentHash.Length == 0)
            {
                throw new GitException("ReferenceRemoteBranchDeleteMissing", null, branch.FullName);
            }
            if (string.Equals(currentHash, branch.CommitHash, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new GitException("ReferenceRemoteBranchDeleteChanged", null, branch.FullName);
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "push", "--porcelain", $"--force-with-lease={branchReference}:{branch.CommitHash}", branch.RemoteName, $":{branchReference}" }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                string error = result.Error.Trim();
                if (error.Length == 0)
                {
                    throw new GitException("ReferenceRemoteBranchDeleteFailedWithoutOutput", null, branch.FullName, result.ExitCode);
                }
                throw new GitException("ReferenceRemoteBranchDeleteFailed", null, branch.FullName, error);
            }

            string trackingReference = $"refs/remotes/{branch.FullName}";
            await _runner.RunAsync(repository.RootPath,
                new string[] { "update-ref", "-d", trackingReference, branch.CommitHash }, true, cancellationToken);
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
            GitRepository updated = await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
            if (updated.CurrentBranch != branchName)
            {
                throw new GitException("ReferenceSwitchUnexpectedCurrent", null, branchName, updated.CurrentBranch);
            }
            return updated;
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

        public async Task<GitReferenceRenameResult> RenameLocalBranchAsync(GitRepository repository,
            GitReferenceRenameRequest request, CancellationToken cancellationToken = default)
        {
            string oldName = await ValidateReferenceRenameAsync(repository, request, "refs/heads/", cancellationToken);
            if (oldName == request.NewName)
            {
                return new GitReferenceRenameResult(await _repositoryService.OpenAsync(repository.RootPath, cancellationToken), false);
            }
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "branch", "-m", "--", oldName, request.NewName }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("ReferenceRenameFailed", null, request.ReferenceName, "refs/heads/" + request.NewName, result.Error.Trim());
            }
            return await ReadReferenceRenameResultAsync(repository, oldName, request.NewName, cancellationToken);
        }

        public async Task<GitReferenceRenameResult> RenameLocalTagAsync(GitRepository repository,
            GitReferenceRenameRequest request, CancellationToken cancellationToken = default)
        {
            string oldName = await ValidateReferenceRenameAsync(repository, request, "refs/tags/", cancellationToken);
            if (oldName == request.NewName)
            {
                return new GitReferenceRenameResult(await _repositoryService.OpenAsync(repository.RootPath, cancellationToken), false);
            }
            string destination = "refs/tags/" + request.NewName;
            string transaction = $"start\ncreate {destination} {request.ObjectId}\ndelete {request.ReferenceName} {request.ObjectId}\nprepare\ncommit\n";
            try
            {
                await _runner.RunWithInputAsync(repository.RootPath, _renameTagTransactionArguments, transaction, cancellationToken);
            }
            catch (GitException exception)
            {
                if (exception.ErrorCode != null)
                {
                    throw;
                }
                throw new GitException("ReferenceRenameFailed", exception, request.ReferenceName, destination, exception.Message);
            }
            return await ReadReferenceRenameResultAsync(repository, oldName, request.NewName, cancellationToken);
        }

        private async Task<string> ValidateReferenceRenameAsync(GitRepository repository, GitReferenceRenameRequest request,
            string prefix, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.RepositoryRoot))
            {
                throw new GitException("ReferenceRenameRepositoryChanged", null, Array.Empty<object>());
            }
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(Path.GetFullPath(repository.RootPath), Path.GetFullPath(request.RepositoryRoot), comparison) == false)
            {
                throw new GitException("ReferenceRenameRepositoryChanged", null, Array.Empty<object>());
            }
            if (string.IsNullOrWhiteSpace(request.ReferenceName))
            {
                throw new GitException("ReferenceRenameSourceInvalid", null, request.ReferenceName ?? string.Empty);
            }
            if (request.ReferenceName.StartsWith(prefix, StringComparison.Ordinal) == false)
            {
                throw new GitException("ReferenceRenameSourceInvalid", null, request.ReferenceName);
            }
            GitCommandResult sourceFormat = await _runner.RunAsync(repository.RootPath,
                new string[] { "check-ref-format", request.ReferenceName }, true, cancellationToken);
            if (sourceFormat.ExitCode != 0)
            {
                throw new GitException("ReferenceRenameSourceInvalid", null, request.ReferenceName);
            }
            ValidateReferenceRenameObjectId(request);
            if (string.IsNullOrWhiteSpace(request.NewName))
            {
                throw new GitException("ReferenceRenameNameRequired", null, Array.Empty<object>());
            }
            if (request.NewName.StartsWith("-", StringComparison.Ordinal))
            {
                throw new GitException("ReferenceRenameNameInvalid", null, request.NewName);
            }
            string destination = prefix + request.NewName;
            GitCommandResult nameFormat = await _runner.RunAsync(repository.RootPath,
                new string[] { "check-ref-format", destination }, true, cancellationToken);
            if (nameFormat.ExitCode != 0)
            {
                throw new GitException("ReferenceRenameNameInvalid", null, request.NewName);
            }
            if (prefix == "refs/heads/")
            {
                GitCommandResult branchFormat = await _runner.RunAsync(repository.RootPath,
                    new string[] { "check-ref-format", "--branch", request.NewName }, true, cancellationToken);
                if (branchFormat.ExitCode != 0)
                {
                    throw new GitException("ReferenceRenameNameInvalid", null, request.NewName);
                }
                if (branchFormat.Output.Trim() != request.NewName)
                {
                    throw new GitException("ReferenceRenameNameInvalid", null, request.NewName);
                }
            }
            string oldName = request.ReferenceName[prefix.Length..];
            if (oldName != request.NewName)
            {
                string existing = await ReadRenameReferenceObjectIdAsync(repository, destination, false, cancellationToken);
                if (existing.Length > 0)
                {
                    throw new GitException("ReferenceRenameDestinationExists", null, destination);
                }
            }
            string current = await ReadRenameReferenceObjectIdAsync(repository, request.ReferenceName, true, cancellationToken);
            if (current.Length == 0)
            {
                throw new GitException("ReferenceRenameSourceMissing", null, request.ReferenceName);
            }
            if (current != request.ObjectId)
            {
                throw new GitException("ReferenceRenameSourceChanged", null, request.ReferenceName);
            }
            return oldName;
        }

        private static void ValidateReferenceRenameObjectId(GitReferenceRenameRequest request)
        {
            if (request.ObjectId == null)
            {
                throw new GitException("ReferenceRenameInvalidObjectId", null, request.ReferenceName);
            }
            if (request.ObjectId.Length != 40 && request.ObjectId.Length != 64)
            {
                throw new GitException("ReferenceRenameInvalidObjectId", null, request.ReferenceName);
            }
            if (request.ObjectId.All(Uri.IsHexDigit) == false)
            {
                throw new GitException("ReferenceRenameInvalidObjectId", null, request.ReferenceName);
            }
        }

        private async Task<string> ReadRenameReferenceObjectIdAsync(GitRepository repository, string reference,
            bool requireDirect, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(symref)", reference }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("ReferenceRenameLookupFailed", null, reference, result.Error.Trim());
            }
            foreach (string line in SplitLines(result.Output))
            {
                string[] fields = line.Split('\0');
                if (fields.Length != 3)
                {
                    continue;
                }
                if (fields[0] != reference)
                {
                    continue;
                }
                if (requireDirect)
                {
                    if (fields[2].Length > 0)
                    {
                        throw new GitException("ReferenceRenameSourceChanged", null, reference);
                    }
                }
                return fields[1];
            }
            return string.Empty;
        }

        private async Task<GitReferenceRenameResult> ReadReferenceRenameResultAsync(GitRepository repository, string oldName,
            string newName, CancellationToken cancellationToken)
        {
            try
            {
                GitRepository updated = await _repositoryService.OpenAsync(repository.RootPath, cancellationToken);
                return new GitReferenceRenameResult(updated, true);
            }
            catch (GitException exception)
            {
                throw new GitException("ReferenceRenameStateReadFailed", exception, oldName, newName);
            }
        }

        public async Task DeleteLocalTagAsync(GitRepository repository, GitTag tag, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(tag);
            await ValidateTagDeletionNameAsync(repository, tag.Name, cancellationToken);
            ValidateTagObjectId(tag.Name, tag.ObjectId);
            string current = await ReadLocalTagObjectIdAsync(repository, tag, cancellationToken);
            if (current.Length == 0)
            {
                throw new GitException("TagDeleteLocalMissing", null, tag.Name);
            }
            if (current != tag.ObjectId)
            {
                throw new GitException("TagDeleteLocalChanged", null, tag.Name);
            }
            GitCommandResult deleted = await _runner.RunAsync(repository.RootPath,
                new string[] { "update-ref", "--no-deref", "-d", tag.ReferenceName, tag.ObjectId }, true, cancellationToken);
            if (deleted.ExitCode == 0)
            {
                return;
            }
            current = await ReadLocalTagObjectIdAsync(repository, tag, cancellationToken);
            if (current != tag.ObjectId)
            {
                throw new GitException("TagDeleteLocalChanged", null, tag.Name);
            }
            throw new GitException("TagDeleteLocalFailed", null, tag.Name, deleted.Error.Trim());
        }

        public async Task<GitRemoteTagDeletionPreview> GetRemoteTagDeletionPreviewAsync(GitRepository repository, string tagName,
            string remoteName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            await ValidateTagDeletionNameAsync(repository, tagName, cancellationToken);
            string pushUrl = await ReadTagDeletionPushUrlAsync(repository, remoteName, cancellationToken);
            string objectId = await ReadRemoteTagObjectIdAsync(repository, remoteName, pushUrl, tagName, cancellationToken);
            return new GitRemoteTagDeletionPreview(repository.RootPath, remoteName, pushUrl, tagName, objectId);
        }

        public async Task DeleteRemoteTagAsync(GitRepository repository, GitRemoteTagDeletionPreview preview,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(preview);
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(Path.GetFullPath(repository.RootPath), Path.GetFullPath(preview.RepositoryRoot), comparison) == false)
            {
                throw new GitException("TagDeleteRepositoryChanged", null, Array.Empty<object>());
            }
            await ValidateTagDeletionNameAsync(repository, preview.TagName, cancellationToken);
            ValidateTagObjectId(preview.TagName, preview.ObjectId);
            string pushUrl = await ReadTagDeletionPushUrlAsync(repository, preview.RemoteName, cancellationToken);
            if (pushUrl != preview.PushUrl)
            {
                throw new GitException("TagDeleteRemoteUrlChanged", null, preview.RemoteName);
            }
            string current = await ReadRemoteTagObjectIdAsync(repository, preview.RemoteName, preview.PushUrl, preview.TagName, cancellationToken);
            if (current != preview.ObjectId)
            {
                throw new GitException("TagDeleteRemoteChanged", null, preview.RemoteName, preview.TagName);
            }
            GitCommandResult deleted = await _runner.RunAsync(repository.RootPath,
                new string[] { "push", "--porcelain", "--no-follow-tags", $"--force-with-lease={preview.ReferenceName}:{preview.ObjectId}", "--", preview.PushUrl, $":{preview.ReferenceName}" }, true, cancellationToken);
            if (deleted.ExitCode != 0)
            {
                string error = RedactTagDeletionOutput(deleted.Error.Trim(), preview.PushUrl);
                throw new GitException("TagDeleteRemoteFailed", null, preview.RemoteName, preview.TagName, error);
            }
        }

        private async Task ValidateTagDeletionNameAsync(GitRepository repository, string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new GitException("TagDeleteInvalidName", null, name ?? string.Empty);
            }
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "check-ref-format", "refs/tags/" + name }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("TagDeleteInvalidName", null, name);
            }
        }

        private static void ValidateTagObjectId(string name, string objectId)
        {
            if (objectId == null)
            {
                throw new GitException("TagDeleteInvalidObjectId", null, name);
            }
            if (objectId.Length != 40 && objectId.Length != 64)
            {
                throw new GitException("TagDeleteInvalidObjectId", null, name);
            }
            if (objectId.All(Uri.IsHexDigit) == false)
            {
                throw new GitException("TagDeleteInvalidObjectId", null, name);
            }
        }

        private async Task<string> ReadLocalTagObjectIdAsync(GitRepository repository, GitTag tag, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(symref)", tag.ReferenceName }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("TagDeleteLocalFailed", null, tag.Name, result.Error.Trim());
            }
            foreach (string line in SplitLines(result.Output))
            {
                string[] fields = line.Split('\0');
                if (fields.Length != 3)
                {
                    continue;
                }
                if (fields[0] != tag.ReferenceName)
                {
                    continue;
                }
                if (fields[2].Length > 0)
                {
                    throw new GitException("TagDeleteLocalChanged", null, tag.Name);
                }
                return fields[1];
            }
            return string.Empty;
        }

        private async Task<string> ReadTagDeletionPushUrlAsync(GitRepository repository, string remoteName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(remoteName))
            {
                throw new GitException("TagDeleteRemoteUnavailable", null, remoteName ?? string.Empty);
            }
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "remote", "get-url", "--push", "--all", "--", remoteName }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("TagDeleteRemoteUnavailable", null, remoteName);
            }
            string[] urls = SplitLines(result.Output);
            if (urls.Length > 1)
            {
                throw new GitException("TagDeleteRemoteMultiplePushUrls", null, remoteName);
            }
            if (urls.Length == 0)
            {
                throw new GitException("TagDeleteRemoteUnavailable", null, remoteName);
            }
            return urls[0];
        }

        private async Task<string> ReadRemoteTagObjectIdAsync(GitRepository repository, string remoteName, string pushUrl,
            string tagName, CancellationToken cancellationToken)
        {
            string reference = "refs/tags/" + tagName;
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                new string[] { "ls-remote", "--refs", "--exit-code", "--", pushUrl, reference }, true, cancellationToken);
            if (result.ExitCode == 2)
            {
                throw new GitException("TagDeleteRemoteTagMissing", null, remoteName, tagName);
            }
            if (result.ExitCode != 0)
            {
                throw new GitException("TagDeleteRemoteLookupFailed", null, remoteName, tagName, RedactTagDeletionOutput(result.Error.Trim(), pushUrl));
            }
            foreach (string line in SplitLines(result.Output))
            {
                int tab = line.IndexOf('\t');
                if (tab < 0)
                {
                    continue;
                }
                if (line[(tab + 1)..] != reference)
                {
                    continue;
                }
                string objectId = line[..tab];
                ValidateTagObjectId(tagName, objectId);
                return objectId;
            }
            throw new GitException("TagDeleteRemoteTagMissing", null, remoteName, tagName);
        }

        private static string RedactTagDeletionOutput(string output, string pushUrl)
        {
            string safe = output.Replace(pushUrl, SanitizeUrl(pushUrl), StringComparison.Ordinal);
            if (Uri.TryCreate(pushUrl, UriKind.Absolute, out Uri parsed))
            {
                if (parsed.UserInfo.Length > 0)
                {
                    safe = safe.Replace(parsed.UserInfo, "***", StringComparison.Ordinal);
                    safe = safe.Replace(Uri.UnescapeDataString(parsed.UserInfo), "***", StringComparison.Ordinal);
                }
            }
            return safe;
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
