using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignus.DependencyInjection.Attributes;

namespace Bough.Core.Git
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class GitRepositoryService
    {
        private static readonly string[] _repositoryRootArguments = new string[] { "rev-parse", "--show-toplevel" };
        private static readonly string[] _currentBranchArguments = new string[] { "branch", "--show-current" };
        private static readonly string[] _conflictPathsArguments = new string[] { "diff", "--name-only", "--diff-filter=U", "-z" };
        private static readonly string[] _incomingRevisions = new string[] { "MERGE_HEAD", "REBASE_HEAD", "CHERRY_PICK_HEAD" };
        private readonly GitCommandRunner _runner;

        public GitRepositoryService(GitCommandRunner runner)
        {
            _runner = runner;
        }

        public async Task<GitRepository> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            string candidate;
            if (File.Exists(path) == true)
            {
                string parentPath = Path.GetDirectoryName(Path.GetFullPath(path));
                if (parentPath == null)
                {
                    throw new GitException("RepositorySelectedFileNoParent", null, path);
                }

                candidate = parentPath;
            }
            else
            {
                candidate = Path.GetFullPath(path);
            }

            GitCommandResult rootResult = await _runner.RunAsync(candidate, _repositoryRootArguments, false, cancellationToken);
            string root = Path.GetFullPath(rootResult.Output.Trim());
            GitCommandResult branchResult = await _runner.RunAsync(root, _currentBranchArguments, true, cancellationToken);
            string branch = branchResult.Output.Trim();

            if (branch.Length == 0)
            {
                branch = "Detached HEAD";
            }

            return new GitRepository(root, Path.GetFileName(root), branch);
        }

        public async Task<IReadOnlyList<string>> GetConflictPathsAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _conflictPathsArguments, false, cancellationToken);

            List<string> paths = result.Output
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return paths;
        }

        public async Task<GitConflictFile> LoadConflictAsync(GitRepository repository, string relativePath,
            string incomingChangeLabel, string incomingIndexStageLabel, CancellationToken cancellationToken = default)
        {
            string fullPath = ResolvePath(repository.RootPath, relativePath);
            byte[] originalBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            bool hasUtf8Bom = originalBytes.Length >= 3 &&
                originalBytes[0] == 0xEF &&
                originalBytes[1] == 0xBB &&
                originalBytes[2] == 0xBF;
            int textOffset = 0;
            if (hasUtf8Bom == true)
            {
                textOffset = 3;
            }

            string workingText;
            try
            {
                UTF8Encoding strictEncoding = new(false, true);
                workingText = strictEncoding.GetString(originalBytes, textOffset, originalBytes.Length - textOffset);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException("RepositoryConflictInvalidUtf8", exception, relativePath);
            }
            string baseText = await ReadStageAsync(repository, 1, relativePath, cancellationToken);
            string oursText = await ReadStageAsync(repository, 2, relativePath, cancellationToken);
            string theirsText = await ReadStageAsync(repository, 3, relativePath, cancellationToken);
            string oursSource = await DescribeRevisionAsync(repository, "HEAD", repository.CurrentBranch, cancellationToken);
            string theirsSource = await DescribeIncomingRevisionAsync(repository, incomingChangeLabel, incomingIndexStageLabel, cancellationToken);

            return new GitConflictFile(relativePath, workingText, baseText, oursText, theirsText, oursSource, theirsSource, Convert.ToHexString(SHA256.HashData(originalBytes)), hasUtf8Bom);
        }

        public async Task SaveAndStageAsync(GitRepository repository, GitConflictFile conflict, string resolvedText, CancellationToken cancellationToken = default)
        {
            string fullPath = ResolvePath(repository.RootPath, conflict.RelativePath);
            byte[] currentBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            string currentHash = Convert.ToHexString(SHA256.HashData(currentBytes));
            if (currentHash != conflict.OriginalContentHash)
            {
                throw new GitException("RepositoryConflictFileChanged", null, conflict.RelativePath);
            }

            UTF8Encoding encoding = new(conflict.HasUtf8Bom, true);
            await File.WriteAllTextAsync(fullPath, resolvedText, encoding, cancellationToken);
            await _runner.RunAsync(repository.RootPath, new string[] { "add", "--", conflict.RelativePath }, false, cancellationToken);
        }

        private async Task<string> ReadStageAsync(GitRepository repository, int stage, string relativePath, CancellationToken cancellationToken)
        {
            string gitPath = relativePath.Replace('\\', '/');
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "show", $":{stage}:{gitPath}" }, true, cancellationToken);

            if (result.ExitCode == 0)
            {
                return result.Output;
            }

            return string.Empty;
        }

        private async Task<string> DescribeIncomingRevisionAsync(GitRepository repository, string incomingChangeLabel,
            string incomingIndexStageLabel, CancellationToken cancellationToken)
        {
            foreach (string revision in _incomingRevisions)
            {
                GitCommandResult verifyResult = await _runner.RunAsync(repository.RootPath, new string[] { "rev-parse", "--verify", "-q", revision }, true, cancellationToken);

                if (verifyResult.ExitCode == 0)
                {
                    return await DescribeRevisionAsync(repository, revision, incomingChangeLabel, cancellationToken);
                }
            }

            return incomingIndexStageLabel;
        }

        private async Task<string> DescribeRevisionAsync(GitRepository repository, string revision, string defaultDescription, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "show", "-s", "--format=%h%x09%s", revision }, true, cancellationToken);
            string description = result.Output.Trim();
            if (description.Length == 0)
            {
                return defaultDescription;
            }

            return $"{defaultDescription} · {description.Replace('\t', ' ')}";
        }

        private static string ResolvePath(string repositoryRoot, string relativePath)
        {
            if (Path.IsPathRooted(relativePath) == true)
            {
                throw new GitException("RepositoryRelativePathRequired", null, relativePath);
            }

            string root = Path.GetFullPath(repositoryRoot);
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            string relative = Path.GetRelativePath(root, fullPath);

            if (relative == "..")
            {
                throw new GitException("RepositoryPathOutsideRoot", null, relativePath);
            }

            string parentPrefix = $"..{Path.DirectorySeparatorChar}";
            if (relative.StartsWith(parentPrefix, StringComparison.Ordinal) == true)
            {
                throw new GitException("RepositoryPathOutsideRoot", null, relativePath);
            }

            return fullPath;
        }
    }
}
