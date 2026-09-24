using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.Core.Git
{
    public class GitCommitInspectionService
    {
        private const int MaximumFileBytes = 1024 * 1024;
        private const int MaximumDiffBytes = 2 * 1024 * 1024;
        private readonly GitCommandRunner _runner;
        private readonly UTF8Encoding _strictUtf8;

        public GitCommitInspectionService(GitCommandRunner runner)
        {
            _runner = runner;
            _strictUtf8 = new UTF8Encoding(false, true);
        }

        public async Task<GitCommitInspection> GetCommitAsync(GitRepository repository, string commitHash, CancellationToken cancellationToken = default)
        {
            ValidateHash(commitHash);
            string format = "--format=format:%H%x00%P%x00%an%x00%ae%x00%aI%x00%s%x00%b%x00";
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "show", "-s", format, commitHash }, false, cancellationToken);
            string[] fields = result.Output.Split('\0');
            if (fields.Length != 8 || fields[7].Length != 0)
            {
                throw new GitException($"Git returned an unexpected commit format for {commitHash}.");
            }

            if (DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset authoredAt) == false)
            {
                throw new GitException($"Git returned an invalid author date for {commitHash}.");
            }

            GitCommandResult referencesResult = await _runner.RunAsync(repository.RootPath, new string[] { "for-each-ref", "--points-at", commitHash, "--format=%(refname)" }, false, cancellationToken);
            string[] references = referencesResult.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string[] parents = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new GitCommitInspection(repository.RootPath, fields[0], parents, references, fields[2], fields[3], authoredAt, fields[5], fields[6]);
        }

        public async Task<GitCommitFileChanges> GetChangedFilesAsync(GitRepository repository, string commitHash, string parentHash = null, CancellationToken cancellationToken = default)
        {
            string comparisonParent = await ResolveParentAsync(repository, commitHash, parentHash, cancellationToken);
            List<string> arguments = CreateDiffArguments(comparisonParent, commitHash, "--name-status");
            arguments.Insert(arguments.Count - 1, "-z");
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
            string[] parts = SplitNul(result.Output, $"changed files for {commitHash}");
            ArrayQueue<GitCommitChangedFile> files = [];
            for (int index = 0; index < parts.Length;)
            {
                string status = parts[index++];
                if (status.Length == 0 || index >= parts.Length)
                {
                    throw new GitException($"Git returned an invalid changed-file record for {commitHash}.");
                }

                string previousPath = string.Empty;
                string path = parts[index++];
                if (status[0] == 'R' || status[0] == 'C')
                {
                    if (index >= parts.Length)
                    {
                        throw new GitException($"Git returned an incomplete rename record for {commitHash}.");
                    }

                    previousPath = path;
                    path = parts[index++];
                }

                files.Add(new GitCommitChangedFile(status, path, previousPath));
            }

            return new GitCommitFileChanges(repository.RootPath, commitHash, comparisonParent, files.ToArray());
        }

        public async Task<GitCommitFileDiff> GetFileDiffAsync(GitRepository repository, string commitHash, string parentHash, GitCommitChangedFile file, CancellationToken cancellationToken = default)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            ValidatePath(file.Path);
            string comparisonParent = await ResolveParentAsync(repository, commitHash, parentHash, cancellationToken);
            string previousPath = file.PreviousPath;
            if (previousPath.Length == 0)
            {
                previousPath = file.Path;
            }

            ValidatePath(previousPath);
            GitCommitFileContent after = await GetFileContentAsync(repository, commitHash, file.Path, cancellationToken);
            GitCommitFileContent before = null;
            if (comparisonParent.Length > 0)
            {
                before = await GetFileContentAsync(repository, comparisonParent, previousPath, cancellationToken);
            }

            string reason = GetDiffReason(after, before);
            if (reason.Length > 0)
            {
                return new GitCommitFileDiff(repository.RootPath, commitHash, comparisonParent, file.Path, file.PreviousPath, string.Empty, reason, Array.Empty<GitUnifiedDiffHunk>());
            }

            List<string> arguments = CreateDiffArguments(comparisonParent, commitHash, "--patch");
            arguments.Insert(arguments.Count - 1, "--no-ext-diff");
            arguments.Insert(arguments.Count - 1, "--no-color");
            arguments.Insert(arguments.Count - 1, "--unified=3");
            arguments.Add(LiteralPath(previousPath));
            if (file.Path != previousPath)
            {
                arguments.Add(LiteralPath(file.Path));
            }

            try
            {
                byte[] bytes = await _runner.RunBytesAsync(repository.RootPath, arguments, MaximumDiffBytes, cancellationToken);
                string text = _strictUtf8.GetString(bytes);
                if (text.Length == 0)
                {
                    return new GitCommitFileDiff(repository.RootPath, commitHash, comparisonParent, file.Path, file.PreviousPath, string.Empty, "No changes against the selected parent.", Array.Empty<GitUnifiedDiffHunk>());
                }

                return new GitCommitFileDiff(repository.RootPath, commitHash, comparisonParent, file.Path, file.PreviousPath, text, string.Empty, ParseHunks(text));
            }
            catch (GitOutputLimitException)
            {
                return new GitCommitFileDiff(repository.RootPath, commitHash, comparisonParent, file.Path, file.PreviousPath, string.Empty, $"Diff exceeds {MaximumDiffBytes} bytes.", Array.Empty<GitUnifiedDiffHunk>());
            }
            catch (DecoderFallbackException)
            {
                return new GitCommitFileDiff(repository.RootPath, commitHash, comparisonParent, file.Path, file.PreviousPath, string.Empty, "Diff is not valid UTF-8 text.", Array.Empty<GitUnifiedDiffHunk>());
            }
        }

        public async Task<GitCommitTreeListing> GetTreeEntriesAsync(GitRepository repository, string commitHash, string directoryPath = "", CancellationToken cancellationToken = default)
        {
            ValidateHash(commitHash);
            if (directoryPath == null)
            {
                throw new ArgumentNullException(nameof(directoryPath));
            }

            string treeish = commitHash;
            if (directoryPath.Length > 0)
            {
                ValidatePath(directoryPath);
                treeish = $"{commitHash}:{directoryPath}";
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "ls-tree", "-z", treeish }, false, cancellationToken);
            ArrayQueue<GitCommitTreeEntry> entries = [];
            foreach (string record in SplitNul(result.Output, $"tree {treeish}"))
            {
                GitCommitTreeEntry entry = ParseTreeEntry(record, directoryPath);
                entries.Add(entry);
            }

            return new GitCommitTreeListing(repository.RootPath, commitHash, directoryPath, entries.ToArray());
        }

        public async Task<GitCommitFileContent> GetFileContentAsync(GitRepository repository, string commitHash, string path, CancellationToken cancellationToken = default)
        {
            ValidateHash(commitHash);
            ValidatePath(path);
            GitCommitTreeEntry entry = await FindTreeEntryAsync(repository, commitHash, path, cancellationToken);
            if (entry == null)
            {
                return new GitCommitFileContent(repository.RootPath, commitHash, path, string.Empty, "File is not present in this commit.", 0, string.Empty);
            }

            if (entry.IsGitlink == true)
            {
                return new GitCommitFileContent(repository.RootPath, commitHash, path, string.Empty, "Submodule gitlink; target commit is shown as the object hash.", 0, entry.ObjectHash);
            }

            if (entry.ObjectType != "blob")
            {
                return new GitCommitFileContent(repository.RootPath, commitHash, path, string.Empty, "Path is a directory, not a file.", 0, entry.ObjectHash);
            }

            GitCommandResult sizeResult = await _runner.RunAsync(repository.RootPath, new string[] { "cat-file", "-s", entry.ObjectHash }, false, cancellationToken);
            if (long.TryParse(sizeResult.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long size) == false)
            {
                throw new GitException($"Git returned an invalid blob size for {path} at {commitHash}.");
            }

            if (size > MaximumFileBytes)
            {
                return new GitCommitFileContent(repository.RootPath, commitHash, path, string.Empty, $"File exceeds {MaximumFileBytes} bytes.", size, entry.ObjectHash);
            }

            byte[] bytes = await _runner.RunBytesAsync(repository.RootPath, new string[] { "cat-file", "blob", entry.ObjectHash }, MaximumFileBytes, cancellationToken);
            if (bytes.Contains((byte)0) == true)
            {
                return new GitCommitFileContent(repository.RootPath, commitHash, path, string.Empty, "Binary file.", size, entry.ObjectHash);
            }

            try
            {
                string text = _strictUtf8.GetString(bytes);
                if (text.StartsWith("version https://git-lfs.github.com/spec/v1\n", StringComparison.Ordinal) == true)
                {
                    return new GitCommitFileContent(repository.RootPath, commitHash, path, text, "Git LFS pointer, not the file contents.", size, entry.ObjectHash);
                }

                return new GitCommitFileContent(repository.RootPath, commitHash, path, text, string.Empty, size, entry.ObjectHash);
            }
            catch (DecoderFallbackException)
            {
                return new GitCommitFileContent(repository.RootPath, commitHash, path, string.Empty, "File is not valid UTF-8 text.", size, entry.ObjectHash);
            }
        }

        public async Task<GitBlamePage> GetBlameAsync(GitRepository repository, string commitHash, string path, int startLine, int lineCount, CancellationToken cancellationToken = default)
        {
            ValidateHash(commitHash);
            ValidatePath(path);
            if (startLine < 1 || lineCount < 1 || lineCount > 200 || startLine > int.MaxValue - lineCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lineCount), $"Blame range must contain 1 to 200 lines: start={startLine}, count={lineCount}.");
            }

            GitCommitFileContent content = await GetFileContentAsync(repository, commitHash, path, cancellationToken);
            if (content.HasText == false)
            {
                throw new GitException($"Cannot blame {path} at {commitHash}: {content.Reason}");
            }

            int totalLines = content.Text.Count(character => character == '\n');
            if (content.Text.Length > 0 && content.Text[content.Text.Length - 1] != '\n')
            {
                totalLines++;
            }

            if (startLine > totalLines)
            {
                return new GitBlamePage(repository.RootPath, commitHash, path, startLine, Array.Empty<GitBlameLine>());
            }

            int endLine = Math.Min(startLine + lineCount - 1, totalLines);
            string range = $"{startLine},{endLine}";
            byte[] bytes = await _runner.RunBytesAsync(repository.RootPath, new string[] { "-c", "core.quotepath=false", "blame", "--line-porcelain", "--encoding=UTF-8", "-L", range, commitHash, "--", path }, 4 * MaximumFileBytes, cancellationToken);
            string output;
            try
            {
                output = _strictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException($"Git blame output is not valid UTF-8 for {path} at {commitHash}.", exception);
            }

            ArrayQueue<GitBlameLine> lines = [];
            string[] records = output.Split('\n');
            int index = 0;
            while (index < records.Length && records[index].Length > 0)
            {
                string[] header = records[index++].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (header.Length < 3 || int.TryParse(header[1], NumberStyles.None, CultureInfo.InvariantCulture, out int originalLine) == false || int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out int finalLine) == false)
                {
                    throw new GitException($"Git returned a malformed blame header for {path} at {commitHash}.");
                }

                string sourceHash = header[0].TrimStart('^');
                ValidateHash(sourceHash);
                string author = string.Empty;
                string summary = string.Empty;
                string originalPath = path;
                string timezone = "+0000";
                long authorTime = 0;
                bool hasAuthorTime = false;
                bool hasText = false;
                string sourceText = string.Empty;
                while (index < records.Length)
                {
                    string record = records[index++];
                    if (record.StartsWith("\t", StringComparison.Ordinal) == true)
                    {
                        sourceText = record.Substring(1);
                        hasText = true;
                        break;
                    }

                    if (record.StartsWith("author ", StringComparison.Ordinal) == true)
                    {
                        author = record.Substring(7);
                    }
                    else if (record.StartsWith("author-time ", StringComparison.Ordinal) == true)
                    {
                        hasAuthorTime = long.TryParse(record.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out authorTime);
                    }
                    else if (record.StartsWith("author-tz ", StringComparison.Ordinal) == true)
                    {
                        timezone = record.Substring(10);
                    }
                    else if (record.StartsWith("summary ", StringComparison.Ordinal) == true)
                    {
                        summary = record.Substring(8);
                    }
                    else if (record.StartsWith("filename ", StringComparison.Ordinal) == true)
                    {
                        originalPath = record.Substring(9);
                    }
                }

                if (hasText == false || hasAuthorTime == false)
                {
                    throw new GitException($"Git returned an incomplete blame record for {path} at {commitHash}.");
                }

                DateTimeOffset authoredAt = DateTimeOffset.FromUnixTimeSeconds(authorTime).ToOffset(ParseTimezone(timezone));
                lines.Add(new GitBlameLine(finalLine, originalLine, sourceHash, author, authoredAt, summary, originalPath, sourceText));
            }

            return new GitBlamePage(repository.RootPath, commitHash, path, startLine, lines.ToArray());
        }

        public async Task<GitFileHistoryPage> GetFileHistoryAsync(GitRepository repository, string commitHash, string path, int limit, CancellationToken cancellationToken = default)
        {
            ValidateHash(commitHash);
            ValidatePath(path);
            if (limit < 1 || limit > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }

            string format = "--format=format:%H%x00%an%x00%aI%x00%s%x00";
            byte[] bytes = await _runner.RunBytesAsync(repository.RootPath, new string[] { "log", "--follow", "--name-status", "-z", format, $"--max-count={limit}", commitHash, "--", LiteralPath(path) }, 2 * MaximumFileBytes, cancellationToken);
            string output;
            try
            {
                output = _strictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException($"Git file history output is not valid UTF-8 for {path} at {commitHash}.", exception);
            }

            string[] parts = SplitNul(output, $"history of {path}");
            ArrayQueue<GitFileHistoryEntry> entries = [];
            string currentPath = path;
            int index = 0;
            while (index < parts.Length)
            {
                string historyHash = parts[index++].TrimStart('\r', '\n');
                if (historyHash.Length == 0)
                {
                    continue;
                }

                ValidateHash(historyHash);
                if (index + 2 >= parts.Length)
                {
                    throw new GitException($"Git returned an incomplete history record for {path}.");
                }

                string author = parts[index++];
                string date = parts[index++];
                string title = parts[index++];
                if (DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset authoredAt) == false)
                {
                    throw new GitException($"Git returned an invalid history date for {historyHash}.");
                }

                string previousPath = string.Empty;
                while (index < parts.Length)
                {
                    string status = parts[index++].TrimStart('\r', '\n');
                    if (status.Length == 0)
                    {
                        break;
                    }

                    if (index >= parts.Length)
                    {
                        throw new GitException($"Git returned an incomplete history status for {historyHash}.");
                    }

                    string changedPath = parts[index++];
                    if (status[0] == 'R' || status[0] == 'C')
                    {
                        if (index >= parts.Length)
                        {
                            throw new GitException($"Git returned an incomplete rename history for {historyHash}.");
                        }

                        string newPath = parts[index++];
                        if (newPath == currentPath)
                        {
                            previousPath = changedPath;
                        }
                    }
                }

                entries.Add(new GitFileHistoryEntry(historyHash, author, authoredAt, title, currentPath, previousPath));
                if (previousPath.Length > 0)
                {
                    currentPath = previousPath;
                }
            }

            return new GitFileHistoryPage(repository.RootPath, commitHash, path, entries.ToArray());
        }

        private static TimeSpan ParseTimezone(string timezone)
        {
            if (timezone.Length != 5 || (timezone[0] != '+' && timezone[0] != '-') || int.TryParse(timezone.Substring(1, 2), out int hours) == false || int.TryParse(timezone.Substring(3, 2), out int minutes) == false || hours > 14 || minutes > 59)
            {
                throw new GitException($"Git returned an invalid author timezone: {timezone}.");
            }

            TimeSpan offset = new(hours, minutes, 0);
            if (timezone[0] == '-')
            {
                offset = -offset;
            }

            return offset;
        }

        private async Task<string> ResolveParentAsync(GitRepository repository, string commitHash, string parentHash, CancellationToken cancellationToken)
        {
            GitCommitInspection commit = await GetCommitAsync(repository, commitHash, cancellationToken);
            if (parentHash == null)
            {
                return commit.DefaultParent;
            }

            if (parentHash.Length == 0 && commit.Parents.Count == 0)
            {
                return string.Empty;
            }

            if (commit.Parents.Contains(parentHash, StringComparer.OrdinalIgnoreCase) == false)
            {
                throw new ArgumentException($"{parentHash} is not a parent of {commitHash}.", nameof(parentHash));
            }

            return parentHash;
        }

        private async Task<GitCommitTreeEntry> FindTreeEntryAsync(GitRepository repository, string commitHash, string path, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "ls-tree", "--full-tree", "-z", commitHash, "--", LiteralPath(path) }, false, cancellationToken);
            foreach (string record in SplitNul(result.Output, $"tree path {path}"))
            {
                GitCommitTreeEntry entry = ParseTreeEntry(record, string.Empty);
                if (entry.Path == path)
                {
                    return entry;
                }
            }

            return null;
        }

        private static GitCommitTreeEntry ParseTreeEntry(string record, string directoryPath)
        {
            int tab = record.IndexOf('\t');
            if (tab < 0)
            {
                throw new GitException("Git returned a malformed tree record.");
            }

            string[] fields = record.Substring(0, tab).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3)
            {
                throw new GitException("Git returned a malformed tree entry header.");
            }

            string path = record.Substring(tab + 1);
            if (directoryPath.Length > 0)
            {
                path = directoryPath + "/" + path;
            }

            return new GitCommitTreeEntry(path, fields[0], fields[1], fields[2]);
        }

        private static List<string> CreateDiffArguments(string parentHash, string commitHash, string format)
        {
            if (parentHash.Length == 0)
            {
                return ["diff-tree", "--root", "--no-commit-id", "-r", "--find-renames", format, commitHash, "--"];
            }

            return ["diff", "--find-renames", format, parentHash, commitHash, "--"];
        }

        private static string GetDiffReason(GitCommitFileContent after, GitCommitFileContent before)
        {
            if (after.Reason.Length > 0 && after.ObjectHash.Length > 0)
            {
                return after.Reason;
            }

            if (before != null && before.Reason.Length > 0 && before.ObjectHash.Length > 0)
            {
                return before.Reason;
            }

            return string.Empty;
        }

        private static IReadOnlyList<GitUnifiedDiffHunk> ParseHunks(string diff)
        {
            ArrayQueue<GitUnifiedDiffHunk> hunks = [];
            List<GitUnifiedDiffLine> lines = [];
            string header = null;
            int oldStart = 0;
            int newStart = 0;
            int oldLine = 0;
            int newLine = 0;
            foreach (string line in diff.Split('\n'))
            {
                if (line.StartsWith("diff --git ", StringComparison.Ordinal) == true && header != null)
                {
                    hunks.Add(new GitUnifiedDiffHunk(header, oldStart, newStart, lines));
                    lines.Clear();
                    header = null;
                }

                Match match = Regex.Match(line, @"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.CultureInvariant);
                if (match.Success == true)
                {
                    if (header != null)
                    {
                        hunks.Add(new GitUnifiedDiffHunk(header, oldStart, newStart, lines));
                        lines.Clear();
                    }

                    header = line;
                    oldStart = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    newStart = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    oldLine = oldStart;
                    newLine = newStart;
                    continue;
                }

                if (header == null || line.Length == 0)
                {
                    continue;
                }

                char kind = line[0];
                if (kind == ' ')
                {
                    lines.Add(new GitUnifiedDiffLine(kind, oldLine, newLine, line.Substring(1)));
                    oldLine++;
                    newLine++;
                }
                else if (kind == '-')
                {
                    lines.Add(new GitUnifiedDiffLine(kind, oldLine, 0, line.Substring(1)));
                    oldLine++;
                }
                else if (kind == '+')
                {
                    lines.Add(new GitUnifiedDiffLine(kind, 0, newLine, line.Substring(1)));
                    newLine++;
                }
                else if (kind == '\\')
                {
                    lines.Add(new GitUnifiedDiffLine(kind, 0, 0, line.Substring(1)));
                }
            }

            if (header != null)
            {
                hunks.Add(new GitUnifiedDiffHunk(header, oldStart, newStart, lines));
            }

            return Array.AsReadOnly(hunks.ToArray());
        }

        private static string[] SplitNul(string output, string context)
        {
            if (output.Length == 0)
            {
                return Array.Empty<string>();
            }

            if (output[output.Length - 1] != '\0')
            {
                throw new GitException($"Git returned an unterminated NUL record for {context}.");
            }

            string[] parts = output.Split('\0');
            return parts.Take(parts.Length - 1).ToArray();
        }

        private static void ValidateHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash) == true || Regex.IsMatch(hash, "^([0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant) == false)
            {
                throw new ArgumentException($"A full commit hash is required: {hash}.", nameof(hash));
            }
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) == true || path.IndexOf('\0') >= 0 || path.StartsWith("/", StringComparison.Ordinal) == true)
            {
                throw new ArgumentException($"A repository-relative Git path is required: {path}.", nameof(path));
            }

            string[] parts = path.Split('/');
            if (parts.Any(part => part.Length == 0 || part == "." || part == "..") == true)
            {
                throw new ArgumentException($"A normalized Git path is required: {path}.", nameof(path));
            }
        }

        private static string LiteralPath(string path)
        {
            return ":(literal)" + path;
        }
    }
}
