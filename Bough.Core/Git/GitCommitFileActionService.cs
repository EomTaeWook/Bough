using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class GitCommitFileActionService
    {
        private const string _temporaryDirectoryPrefix = "Bough-snapshot-";
        private static readonly char[] _invalidFileNameCharacters = Path.GetInvalidFileNameChars();
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

        public async Task<GitTemporarySnapshotFile> PrepareSnapshotFileAsync(GitRepository repository, string commitHash, string path, CancellationToken cancellationToken = default)
        {
            byte[] bytes = await GetSnapshotBytesAsync(repository, commitHash, path, cancellationToken);
            return await Task.Run(async () =>
            {
                GitTemporarySnapshotFile snapshot = null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string directory = Path.Combine(Path.GetTempPath(), _temporaryDirectoryPrefix + Guid.NewGuid().ToString("N"));
                    string fileName = GetTemporaryFileName(path);
                    Directory.CreateDirectory(directory);
                    snapshot = new GitTemporarySnapshotFile(directory, Path.Combine(directory, fileName));
                    await using (FileStream stream = new(snapshot.FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, FileOptions.Asynchronous))
                    {
                        await stream.WriteAsync(bytes, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    File.SetAttributes(snapshot.FilePath, File.GetAttributes(snapshot.FilePath) | FileAttributes.ReadOnly);
                    return snapshot;
                }
                catch (OperationCanceledException)
                {
                    DiscardSnapshotFile(snapshot);
                    throw;
                }
                catch (Exception exception)
                {
                    DiscardSnapshotFile(snapshot);
                    throw new GitException("CommitFileTemporarySnapshotFailed", exception, path);
                }
            }, cancellationToken);
        }

        private static string GetTemporaryFileName(string path)
        {
            string name = Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
            foreach (char character in _invalidFileNameCharacters)
            {
                name = name.Replace(character, '_');
            }
            if (OperatingSystem.IsWindows())
            {
                name = name.TrimEnd(' ', '.');
            }
            if (name.Length == 0)
            {
                throw new GitException("CommitFilePathInvalid", null, path);
            }
            if (OperatingSystem.IsWindows())
            {
                string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
                switch (stem)
                {
                    case "CON":
                    case "PRN":
                    case "AUX":
                    case "NUL":
                    case "CONIN$":
                    case "CONOUT$":
                        name = "_" + name;
                        break;
                    default:
                        if (stem.Length == 4)
                        {
                            if (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                            {
                                if ("123456789¹²³".Contains(stem[3]))
                                {
                                    name = "_" + name;
                                }
                            }
                        }
                        break;
                }
            }
            return name;
        }

        public Task DiscardSnapshotFileAsync(GitTemporarySnapshotFile snapshot)
        {
            return Task.Run(() => DiscardSnapshotFile(snapshot));
        }

        private static void DiscardSnapshotFile(GitTemporarySnapshotFile snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            try
            {
                StringComparison comparison = StringComparison.Ordinal;
                if (OperatingSystem.IsWindows())
                {
                    comparison = StringComparison.OrdinalIgnoreCase;
                }
                string directory = Path.GetFullPath(snapshot.DirectoryPath);
                string temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
                if (string.Equals(Path.GetDirectoryName(directory), temporaryRoot, comparison) == false)
                {
                    return;
                }
                string name = Path.GetFileName(directory);
                if (name.StartsWith(_temporaryDirectoryPrefix, StringComparison.Ordinal) == false)
                {
                    return;
                }
                if (Guid.TryParseExact(name.Substring(_temporaryDirectoryPrefix.Length), "N", out _) == false)
                {
                    return;
                }
                string file = Path.GetFullPath(snapshot.FilePath);
                if (string.Equals(Path.GetDirectoryName(file), directory, comparison) == false)
                {
                    return;
                }
                if (File.Exists(file))
                {
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                    File.Delete(file);
                }
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, false);
                }
            }
            catch (IOException)
            {
                // Preserve the preparation error if the owned temporary file cannot be removed.
            }
            catch (UnauthorizedAccessException)
            {
                // Do not expand cleanup beyond the file and empty directory owned by this request.
            }
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
