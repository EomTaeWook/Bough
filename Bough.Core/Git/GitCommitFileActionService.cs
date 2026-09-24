using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
            if (string.IsNullOrWhiteSpace(path) == true || Path.IsPathRooted(path) == true || path.Contains('\\') == true)
            {
                throw new GitException($"Invalid repository-relative path: {path}");
            }
            string root = Path.GetFullPath(repository.RootPath);
            string absolute = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(root, absolute);
            if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) == true || Path.IsPathRooted(relative) == true)
            {
                throw new GitException($"Path is outside the repository: {path}");
            }
            return absolute;
        }

        public async Task<byte[]> GetSnapshotBytesAsync(GitRepository repository, string commitHash, string path, CancellationToken cancellationToken = default)
        {
            GetWorkingPath(repository, path);
            GitCommitFileContent content = await _inspectionService.GetFileContentAsync(repository, commitHash, path, cancellationToken);
            if (content.ObjectHash.Length == 0 || content.Reason.StartsWith("File is not present", StringComparison.Ordinal) == true)
            {
                throw new GitException($"{path} is not present in {commitHash}.");
            }
            if (content.Reason.StartsWith("Submodule", StringComparison.Ordinal) == true || content.Reason.StartsWith("Path is a directory", StringComparison.Ordinal) == true)
            {
                throw new GitException($"{path} is not a regular file in {commitHash}.");
            }
            if (content.Size > 50 * 1024 * 1024)
            {
                throw new GitException($"{path} exceeds the 50 MB export limit.");
            }
            return await _runner.RunBytesAsync(repository.RootPath, new string[] { "cat-file", "blob", content.ObjectHash }, 50 * 1024 * 1024, cancellationToken);
        }
    }

}
