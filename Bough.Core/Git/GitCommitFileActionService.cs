using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class GitCommitFileActionService
    {
        private readonly GitCommandRunner _runner;
        private readonly GitCommitInspectionService _inspectionService;

        public GitCommitFileActionService(GitCommandRunner runner, GitCommitInspectionService inspectionService)
        {
            _runner = runner;
            _inspectionService = inspectionService;
        }

        public string GetWorkingPath(GitRepository repository, string path)
        {
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                throw new GitException("CommitFilePathInvalid", null, path);
            }
            if (Path.IsPathRooted(path) == true)
            {
                throw new GitException("CommitFilePathInvalid", null, path);
            }
            if (path.Contains('\\') == true)
            {
                throw new GitException("CommitFilePathInvalid", null, path);
            }
            string root = Path.GetFullPath(repository.RootPath);
            string absolute = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(root, absolute);
            if (relative == "..")
            {
                throw new GitException("CommitFileOutsideRepository", null, path);
            }
            if (relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) == true)
            {
                throw new GitException("CommitFileOutsideRepository", null, path);
            }
            if (Path.IsPathRooted(relative) == true)
            {
                throw new GitException("CommitFileOutsideRepository", null, path);
            }
            return absolute;
        }

        public async Task<byte[]> GetSnapshotBytesAsync(GitRepository repository, string commitHash, string path, CancellationToken cancellationToken = default)
        {
            GetWorkingPath(repository, path);
            GitCommitFileContent content = await _inspectionService.GetFileContentAsync(repository, commitHash, path, cancellationToken);
            if (content.ObjectHash.Length == 0)
            {
                throw new GitException("CommitFileAbsentAtRevision", null, path, commitHash);
            }
            if (content.ReasonCode == "HistoryPreviewFileAbsent")
            {
                throw new GitException("CommitFileAbsentAtRevision", null, path, commitHash);
            }
            if (content.ReasonCode == "HistorySubmoduleGitlink")
            {
                throw new GitException("CommitFileNotRegularAtRevision", null, path, commitHash);
            }
            if (content.ReasonCode == "HistoryPathIsDirectory")
            {
                throw new GitException("CommitFileNotRegularAtRevision", null, path, commitHash);
            }
            if (content.Size > 50 * 1024 * 1024)
            {
                throw new GitException("CommitFileExportLimitExceeded", null, path, 50);
            }
            try
            {
                return await _runner.RunBytesAsync(repository.RootPath, new string[] { "cat-file", "blob", content.ObjectHash }, 50 * 1024 * 1024, cancellationToken);
            }
            catch (GitOutputLimitException exception)
            {
                throw new GitException("CommitFileExportLimitExceeded", exception, path, 50);
            }
        }
    }

}
