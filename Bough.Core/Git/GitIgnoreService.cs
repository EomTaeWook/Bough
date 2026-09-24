using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bough.Core.Git
{
    public class GitIgnoreService
    {
        private const long _maximumIgnoreFileBytes = 8 * 1024 * 1024;
        private static readonly string[] _excludePathArguments = new string[] { "rev-parse", "--git-path", "info/exclude" };
        private static readonly UTF8Encoding _strictUtf8 = new(false, true);
        private readonly GitWorkingTreeService _workingTreeService;
        private readonly GitCommandRunner _runner;

        public GitIgnoreService(GitWorkingTreeService workingTreeService, GitCommandRunner runner)
        {
            ArgumentNullException.ThrowIfNull(workingTreeService);
            ArgumentNullException.ThrowIfNull(runner);
            _workingTreeService = workingTreeService;
            _runner = runner;
        }

        public async Task<GitIgnorePlan> PrepareAsync(GitRepository repository, IReadOnlyList<GitWorktreeFile> files, GitIgnoreLocation location, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(files);
            if (files.Count == 0)
            {
                throw new GitException("Select at least one untracked file to ignore.");
            }

            string targetPath = await GetTargetPathAsync(repository, location, cancellationToken);
            GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, cancellationToken);
            Dictionary<string, GitWorktreeFile> currentFiles = status.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
            List<GitIgnoreEntry> entries = [];
            HashSet<string> paths = new(StringComparer.Ordinal);
            foreach (GitWorktreeFile file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file == null)
                {
                    throw new GitException("A selected ignore file is missing.");
                }

                if (paths.Add(file.Path) == false)
                {
                    throw new GitException($"A file was selected more than once: {file.Path}.");
                }

                if (file.IsUntracked == false)
                {
                    throw new GitException($"Only untracked files can be ignored: {file.Path}.");
                }

                if (currentFiles.TryGetValue(file.Path, out GitWorktreeFile current) == false)
                {
                    throw new GitException($"The selected file changed before ignore: {file.Path}.");
                }

                if (current.IsUntracked == false)
                {
                    throw new GitException($"The selected file is now tracked: {file.Path}.");
                }

                string fullPath = ResolveFilePath(repository, file.Path);
                FileInfo information = new(fullPath);
                if (information.Exists == false)
                {
                    throw new GitException($"The selected untracked file is missing: {file.Path}.");
                }

                if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitException($"A symbolic link cannot be ignored safely: {file.Path}.");
                }

                if (Path.GetFullPath(fullPath).Equals(targetPath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new GitException($"The ignore file cannot ignore itself: {file.Path}.");
                }

                entries.Add(new GitIgnoreEntry(file.Path, CreateRule(file.Path)));
            }

            byte[] originalBytes = await ReadTargetAsync(targetPath, cancellationToken);
            string content = DecodeIgnoreFile(originalBytes);
            HashSet<string> existingRules = new(content.Split('\n').Select(line => line.TrimEnd('\r')), StringComparer.Ordinal);
            foreach (GitIgnoreEntry entry in entries)
            {
                if (existingRules.Contains(entry.Rule) == true)
                {
                    throw new GitException($"An exact ignore rule already exists, but the file is still untracked: {entry.Path}.");
                }
            }

            string newLine = "\n";
            int lastNewLine = content.LastIndexOf('\n');
            if (lastNewLine > 0)
            {
                if (content[lastNewLine - 1] == '\r')
                {
                    newLine = "\r\n";
                }
            }

            return new GitIgnorePlan(location, targetPath, entries, originalBytes, newLine);
        }

        public async Task ApplyAsync(GitRepository repository, GitIgnorePlan plan, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(plan);
            List<GitWorktreeFile> files = plan.Entries.Select(entry => new GitWorktreeFile(entry.Path, null, '?', '?')).ToList();
            GitIgnorePlan current = await PrepareAsync(repository, files, plan.Location, cancellationToken);
            if (current.TargetPath != plan.TargetPath)
            {
                throw new GitException("The Git ignore location changed before confirmation.");
            }

            if (OriginalBytesMatch(current.OriginalBytes, plan.OriginalBytes) == false)
            {
                throw new GitException("The ignore file changed before confirmation. Refresh and try again.");
            }

            if (current.Entries.Count != plan.Entries.Count)
            {
                throw new GitException("The selected files changed before confirmation.");
            }

            for (int index = 0; index < current.Entries.Count; index++)
            {
                if (current.Entries[index].Path != plan.Entries[index].Path)
                {
                    throw new GitException($"The selected file changed before ignore: {plan.Entries[index].Path}.");
                }
            }

            await Task.Run(() => AppendRules(current, cancellationToken), cancellationToken);
        }

        private async Task<string> GetTargetPathAsync(GitRepository repository, GitIgnoreLocation location, CancellationToken cancellationToken)
        {
            if (location == GitIgnoreLocation.Repository)
            {
                return Path.GetFullPath(Path.Combine(repository.RootPath, ".gitignore"));
            }

            if (location != GitIgnoreLocation.Local)
            {
                throw new ArgumentOutOfRangeException(nameof(location));
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _excludePathArguments, false, cancellationToken);
            string gitPath = result.Output.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(gitPath) == true)
            {
                throw new GitException("Git did not provide its local exclude path.");
            }

            string targetPath = Path.GetFullPath(Path.Combine(repository.RootPath, gitPath));
            if (Path.GetFileName(targetPath) != "exclude")
            {
                throw new GitException($"Git returned an unexpected exclude path: {targetPath}.");
            }

            if (Path.GetFileName(Path.GetDirectoryName(targetPath)) != "info")
            {
                throw new GitException($"Git returned an unexpected exclude path: {targetPath}.");
            }

            return targetPath;
        }

        private static string ResolveFilePath(GitRepository repository, string path)
        {
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                throw new GitException("The selected file has no repository-relative path.");
            }

            if (Path.IsPathRooted(path) == true)
            {
                throw new GitException($"The selected path is outside the repository: {path}.");
            }

            string root = Path.GetFullPath(repository.RootPath);
            string fullPath = Path.GetFullPath(Path.Combine(root, path));
            string relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == ".")
            {
                throw new GitException("The repository root cannot be ignored.");
            }

            if (relativePath == "..")
            {
                throw new GitException($"The selected path is outside the repository: {path}.");
            }

            if (Path.IsPathRooted(relativePath) == true)
            {
                throw new GitException($"The selected path is outside the repository: {path}.");
            }

            if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) == true)
            {
                throw new GitException($"The selected path is outside the repository: {path}.");
            }

            string parent = root;
            string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index].Equals(".git", StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new GitException($"Git metadata cannot be ignored: {path}.");
                }

                if (index == segments.Length - 1)
                {
                    break;
                }

                parent = Path.Combine(parent, segments[index]);
                if (Directory.Exists(parent) == false)
                {
                    throw new GitException($"A selected file parent no longer exists: {path}.");
                }

                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitException($"The selected path crosses a symbolic link: {path}.");
                }
            }

            if (Directory.Exists(fullPath) == true)
            {
                throw new GitException($"Only files can be ignored: {path}.");
            }

            return fullPath;
        }

        private static string CreateRule(string path)
        {
            StringBuilder rule = new("/");
            foreach (char character in path)
            {
                if (char.IsControl(character) == true)
                {
                    throw new GitException($"A file name cannot be represented safely in gitignore: {path}.");
                }

                switch (character)
                {
                    case '/':
                        rule.Append('/');
                        continue;
                    case '\\':
                    case '*':
                    case '?':
                    case '[':
                    case ']':
                    case '#':
                    case '!':
                    case ' ':
                        rule.Append('\\');
                        break;
                }

                rule.Append(character);
            }

            return rule.ToString();
        }

        private static async Task<byte[]> ReadTargetAsync(string targetPath, CancellationToken cancellationToken)
        {
            string parent = Path.GetDirectoryName(targetPath);
            if (Directory.Exists(parent) == false)
            {
                throw new GitException($"The ignore directory does not exist: {parent}.");
            }

            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            {
                throw new GitException($"The ignore directory is a symbolic link: {parent}.");
            }

            if (Directory.Exists(targetPath) == true)
            {
                throw new GitException($"The ignore target is a directory: {targetPath}.");
            }

            if (File.Exists(targetPath) == false)
            {
                return null;
            }

            FileInfo information = new(targetPath);
            if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new GitException($"The ignore target is a symbolic link: {targetPath}.");
            }

            if (information.Length > _maximumIgnoreFileBytes)
            {
                throw new GitException($"The ignore file is too large to edit safely: {targetPath}.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(targetPath, cancellationToken);
            if (bytes.Length > _maximumIgnoreFileBytes)
            {
                throw new GitException($"The ignore file grew too large to edit safely: {targetPath}.");
            }

            return bytes;
        }

        private static string DecodeIgnoreFile(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }

            try
            {
                string content = _strictUtf8.GetString(bytes);
                if (content.Length > 0)
                {
                    if (content[0] == '\uFEFF')
                    {
                        return content.Substring(1);
                    }
                }

                return content;
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException("The ignore file is not UTF-8 and cannot be edited safely.", exception);
            }
        }

        private static bool OriginalBytesMatch(byte[] expected, byte[] current)
        {
            if (expected == null)
            {
                return current == null;
            }

            if (current == null)
            {
                return false;
            }

            return expected.SequenceEqual(current);
        }

        private static void AppendRules(GitIgnorePlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] expectedBytes = plan.OriginalBytes;
            string expectedText = DecodeIgnoreFile(expectedBytes);
            HashSet<string> existingRules = new(expectedText.Split('\n').Select(line => line.TrimEnd('\r')), StringComparer.Ordinal);
            List<string> additions = [];
            foreach (GitIgnoreEntry entry in plan.Entries)
            {
                if (existingRules.Add(entry.Rule) == false)
                {
                    throw new GitException($"An exact ignore rule already exists: {entry.Path}.");
                }

                additions.Add(entry.Rule);
            }

            string prefix = string.Empty;
            if (expectedBytes != null)
            {
                if (expectedBytes.Length > 0)
                {
                    if (expectedBytes[expectedBytes.Length - 1] != '\n')
                    {
                        prefix = plan.NewLine;
                    }
                }
            }

            byte[] additionBytes = new UTF8Encoding(false).GetBytes(prefix + string.Join(plan.NewLine, additions) + plan.NewLine);
            bool existed = plan.OriginalBytes != null;
            FileMode mode = FileMode.CreateNew;
            if (existed == true)
            {
                mode = FileMode.Open;
            }

            string parent = Path.GetDirectoryName(plan.TargetPath);
            if (Directory.Exists(parent) == false)
            {
                throw new GitException($"The ignore directory no longer exists: {parent}.");
            }

            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            {
                throw new GitException($"The ignore directory is a symbolic link: {parent}.");
            }

            if (existed == true)
            {
                if (File.Exists(plan.TargetPath) == false)
                {
                    throw new GitException("The ignore file disappeared before writing.");
                }

                if ((File.GetAttributes(plan.TargetPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitException("The ignore file became a symbolic link before writing.");
                }
            }

            bool created = false;
            try
            {
                using FileStream stream = new(plan.TargetPath, mode, FileAccess.ReadWrite, FileShare.None);
                created = existed == false;
                if (existed == true)
                {
                    if ((File.GetAttributes(plan.TargetPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new GitException("The ignore file became a symbolic link before writing.");
                    }
                }

                if (stream.Length > _maximumIgnoreFileBytes)
                {
                    throw new GitException($"The ignore file is too large to edit safely: {plan.TargetPath}.");
                }

                byte[] currentBytes = new byte[(int)stream.Length];
                stream.ReadExactly(currentBytes);
                if (existed == false)
                {
                    if (currentBytes.Length != 0)
                    {
                        throw new GitException("The ignore target appeared before writing.");
                    }
                }
                else if (OriginalBytesMatch(expectedBytes, currentBytes) == false)
                {
                    throw new GitException("The ignore file changed before writing. Refresh and try again.");
                }

                stream.Seek(0, SeekOrigin.End);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    stream.Write(additionBytes);
                    stream.Flush(true);
                }
                catch
                {
                    if (existed == true)
                    {
                        stream.SetLength(currentBytes.Length);
                        stream.Flush(true);
                    }

                    throw;
                }
            }
            catch
            {
                if (created == true)
                {
                    File.Delete(plan.TargetPath);
                }

                throw;
            }
        }
    }
}
