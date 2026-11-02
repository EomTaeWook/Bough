using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class GitWorkingTreeService
    {
        private const long _largeNewFileThreshold = 10 * 1024 * 1024;
        private static readonly string[] _statusArguments = new string[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" };
        private static readonly string[] _restoreBatchArguments = new string[] { "restore", "--worktree", "--pathspec-from-file=-", "--pathspec-file-nul" };
        private static readonly string[] _stageBatchArguments = new string[] { "add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul" };
        private static readonly string[] _headMessageArguments = new string[] { "rev-parse", "--verify", "HEAD^{commit}" };
        private static readonly string[] _commitHeadArguments = new string[] { "rev-parse", "--verify", "--quiet", "HEAD^{commit}" };
        private static readonly string[] _userNameArguments = new string[] { "config", "--get", "user.name" };
        private static readonly string[] _userEmailArguments = new string[] { "config", "--get", "user.email" };
        private static readonly string[] _headArguments = new string[] { "rev-parse", "HEAD" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "HEAD" };
        private static readonly string[] _mergeMessagePathArguments = new string[] { "rev-parse", "--git-path", "MERGE_MSG" };
        private static readonly string[] _hunkDiffOptions = new string[] { "--patch", "--no-textconv", "--full-index", "--unified=3", "--inter-hunk-context=0", "--diff-algorithm=myers", "--no-indent-heuristic", "--no-renames", "--src-prefix=a/", "--dst-prefix=b/", "--no-relative", "--submodule=short" };
        private static readonly string[] _stageHunkArguments = new string[] { "-c", "apply.ignoreWhitespace=no", "apply", "--cached", "--whitespace=nowarn", "-p1", "-" };
        private static readonly string[] _unstageHunkArguments = new string[] { "-c", "apply.ignoreWhitespace=no", "apply", "--cached", "--reverse", "--whitespace=nowarn", "-p1", "-" };
        private static readonly Encoding _mergeMessageEncoding = new UTF8Encoding(false, true);
        private readonly GitCommandRunner _runner;

        public GitWorkingTreeService(GitCommandRunner runner)
        {
            _runner = runner;
            Ignore = new GitIgnoreService(this, runner);
        }

        public GitIgnoreService Ignore { get; }

        public void ValidateSelectedFiles(IReadOnlyList<GitWorktreeFile> expected, IReadOnlyList<GitWorktreeFile> current, bool staged)
        {
            if (expected.Count != current.Count)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }

            Dictionary<string, GitWorktreeFile> currentFiles = current.ToDictionary(file => file.Path, StringComparer.Ordinal);
            foreach (GitWorktreeFile file in expected)
            {
                if (currentFiles.TryGetValue(file.Path, out GitWorktreeFile item) == false)
                {
                    throw new GitException("LocalSelectedFileChanged", null, file.Path);
                }

                bool available = item.IsUnstaged;
                if (staged == true)
                {
                    available = item.IsStaged;
                }
                if (available == false)
                {
                    throw new GitException("LocalSelectedFileUnavailable", null, file.Path);
                }

                if (file.OriginalPath != item.OriginalPath)
                {
                    throw new GitException("LocalSelectedFileChanged", null, file.Path);
                }
                if (file.IndexStatus != item.IndexStatus)
                {
                    throw new GitException("LocalIndexChanged", null, file.Path);
                }
                if (file.WorktreeStatus != item.WorktreeStatus)
                {
                    throw new GitException("LocalWorkingFileChanged", null, file.Path);
                }
            }
        }

        public async Task<GitWorktreeStatus> GetStatusAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            string headCommitHash = await ReadCommitHeadAsync(repository, cancellationToken);
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _statusArguments, false, cancellationToken);
            ArrayQueue<GitWorktreeFile> files = [];
            string[] entries = result.Output.Split('\0');
            if (entries[entries.Length - 1].Length != 0)
            {
                throw new GitException("WorkingStatusMissingNul", null, Array.Empty<object>());
            }

            for (int index = 0; index < entries.Length - 1; index++)
            {
                string entry = entries[index];
                if (entry.Length < 4 || entry[2] != ' ')
                {
                    throw new GitException("WorkingStatusEntryInvalid", null, index);
                }

                char indexStatus = entry[0];
                char worktreeStatus = entry[1];
                string path = entry.Substring(3);
                string originalPath = null;
                if (indexStatus == 'R' || indexStatus == 'C' || worktreeStatus == 'R' || worktreeStatus == 'C')
                {
                    index++;
                    if (index >= entries.Length - 1)
                    {
                        throw new GitException("WorkingRenameOriginalMissing", null, path);
                    }

                    originalPath = entries[index];
                }

                files.Add(new GitWorktreeFile(path, originalPath, indexStatus, worktreeStatus));
            }

            string mergeCommitMessage = await ReadMergeCommitMessageAsync(repository, cancellationToken);
            GitWorktreeFile[] stagedFiles = files.Where(file => file.IsStaged).ToArray();
            Dictionary<string, string> indexEntries = await GetDiscardIndexEntriesAsync(repository, stagedFiles, cancellationToken);
            return new GitWorktreeStatus(files, mergeCommitMessage, headCommitHash, indexEntries);
        }

        private async Task<string> ReadCommitHeadAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _commitHeadArguments, true, cancellationToken);
            if (result.ExitCode == 0)
            {
                return result.Output.Trim();
            }
            string error = result.Error.Trim();
            if (error.Length > 0)
            {
                throw new GitException(error);
            }
            if (result.ExitCode == 1)
            {
                return string.Empty;
            }
            throw new GitException(GitException.ExitWithoutErrorMessageCode, null, result.ExitCode);
        }

        private async Task<string> ReadMergeCommitMessageAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult pathResult = await _runner.RunAsync(repository.RootPath, _mergeMessagePathArguments, false, cancellationToken);
            string messagePath = Path.GetFullPath(pathResult.Output.TrimEnd('\r', '\n'), repository.RootPath);
            string mergeHeadPath = Path.Combine(Path.GetDirectoryName(messagePath), "MERGE_HEAD");
            if (File.Exists(mergeHeadPath) == false)
            {
                return string.Empty;
            }

            try
            {
                string message = await File.ReadAllTextAsync(messagePath, _mergeMessageEncoding, cancellationToken);
                if (File.Exists(mergeHeadPath) == false)
                {
                    return string.Empty;
                }

                return message;
            }
            catch (FileNotFoundException)
            {
                return string.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return string.Empty;
            }
            catch (IOException exception)
            {
                throw new GitException("WorkingMergeMessageReadFailed", exception, messagePath, exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new GitException("WorkingMergeMessageReadFailed", exception, messagePath, exception.Message);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException("WorkingMergeMessageReadFailed", exception, messagePath, exception.Message);
            }
        }

        public async Task<GitFilePreview> GetPreviewAsync(GitRepository repository, GitWorktreeFile file, bool staged, CancellationToken cancellationToken = default)
        {
            if (file.IsConflict == true)
            {
                return new GitFilePreview(string.Empty, "WorktreePreviewConflict", false, 0);
            }

            if (file.IsUntracked == true && staged == false)
            {
                return await ReadUntrackedAsync(repository, file.Path, cancellationToken);
            }

            List<string> arguments = ["diff", "--no-ext-diff", "--no-color"];
            List<string> numstatArguments = ["diff", "--numstat", "--no-ext-diff", "--no-textconv", "--no-color"];
            bool hunkCandidate = IsHunkFile(file, staged);
            string headCommitHash = null;
            string indexEntry = null;
            string worktreeHash = null;
            if (hunkCandidate == true)
            {
                arguments.AddRange(_hunkDiffOptions);
            }
            if (staged == true)
            {
                arguments.Add("--cached");
                numstatArguments.Add("--cached");
            }

            arguments.Add("--");
            numstatArguments.Add("--");
            arguments.Add(LiteralPath(file.Path));
            numstatArguments.Add(LiteralPath(file.Path));
            if (string.IsNullOrEmpty(file.OriginalPath) == false)
            {
                arguments.Add(LiteralPath(file.OriginalPath));
                numstatArguments.Add(LiteralPath(file.OriginalPath));
            }

            GitCommandResult numstat = await _runner.RunAsync(repository.RootPath, numstatArguments, false, cancellationToken);
            bool isBinary = numstat.Output.StartsWith("-\t-\t", StringComparison.Ordinal);
            long size = GetWorkingFileSize(repository, file.Path);
            if (isBinary == true)
            {
                return new GitFilePreview(string.Empty, "WorktreePreviewBinaryChanged", true, size, size);
            }
            if (hunkCandidate == true)
            {
                headCommitHash = await ReadCommitHeadAsync(repository, cancellationToken);
                indexEntry = await ReadHunkIndexEntryAsync(repository, file, cancellationToken);
                worktreeHash = await ReadHunkWorktreeHashAsync(repository, file.Path, cancellationToken);
            }

            string diffText;
            bool exactText = true;
            if (hunkCandidate == true)
            {
                byte[] patchBytes = await _runner.RunBytesAsync(repository.RootPath, arguments, int.MaxValue, cancellationToken);
                try
                {
                    diffText = _mergeMessageEncoding.GetString(patchBytes);
                }
                catch (DecoderFallbackException)
                {
                    diffText = Encoding.UTF8.GetString(patchBytes);
                    exactText = false;
                }
            }
            else
            {
                GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
                diffText = result.Output;
            }
            if (diffText.Length == 0)
            {
                return new GitFilePreview(string.Empty, "WorktreePreviewNoTextDiff", false, size);
            }

            GitFilePreview preview = new(diffText, $"{file.StatusCode}Preview", false, size, size);
            if (hunkCandidate == false)
            {
                return preview;
            }
            if (exactText == false)
            {
                return preview;
            }
            if (string.IsNullOrEmpty(headCommitHash) == true)
            {
                return preview;
            }
            if (indexEntry == null)
            {
                return preview;
            }
            if (worktreeHash == null)
            {
                return preview;
            }

            IReadOnlyList<GitDiffHunk> hunks = ReadDiffHunks(diffText);
            if (hunks.Count == 0)
            {
                return preview;
            }
            List<string> summaryArguments = ["diff", "--summary", "--no-ext-diff", "--no-textconv", "--no-renames", headCommitHash, "--", LiteralPath(file.Path)];
            GitCommandResult summary = await _runner.RunAsync(repository.RootPath, summaryArguments, false, cancellationToken);
            if (summary.Output.Length != 0)
            {
                return preview;
            }
            if (headCommitHash != await ReadCommitHeadAsync(repository, cancellationToken))
            {
                throw new GitException("LocalSelectedFileChanged", null, file.Path);
            }
            if (indexEntry != await ReadHunkIndexEntryAsync(repository, file, cancellationToken))
            {
                throw new GitException("LocalSelectedFileChanged", null, file.Path);
            }
            if (worktreeHash != await ReadHunkWorktreeHashAsync(repository, file.Path, cancellationToken))
            {
                throw new GitException("LocalSelectedFileChanged", null, file.Path);
            }
            preview.RepositoryRoot = repository.RootPath;
            preview.File = file;
            preview.IsStaged = staged;
            preview.HeadCommitHash = headCommitHash;
            preview.IndexEntry = indexEntry;
            preview.WorktreeHash = worktreeHash;
            preview.Hunks = hunks;
            return preview;
        }

        public async Task ApplySelectedLinesAsync(GitRepository repository, GitFilePreview preview, IReadOnlyList<int> selectedLineOffsets, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(preview);
            ArgumentNullException.ThrowIfNull(selectedLineOffsets);
            cancellationToken.ThrowIfCancellationRequested();
            if (preview.RepositoryRoot != repository.RootPath)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            if (preview.File == null)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            if (preview.Hunks.Count == 0)
            {
                throw new GitException("LocalSelectedFileChanged", null, preview.File.Path);
            }
            string patch = BuildSelectedLinesPatch(preview, selectedLineOffsets, cancellationToken);

            GitWorktreeStatus status = await GetStatusAsync(repository, cancellationToken);
            GitWorktreeFile currentFile = status.Files.FirstOrDefault(file => file.Path == preview.File.Path);
            if (currentFile == null)
            {
                throw new GitException("LocalSelectedFileChanged", null, preview.File.Path);
            }
            ValidateSelectedFiles([preview.File], [currentFile], preview.IsStaged);
            GitFilePreview current = await GetPreviewAsync(repository, currentFile, preview.IsStaged, cancellationToken);
            if (current.Hunks.Count == 0)
            {
                throw new GitException("LocalSelectedFileChanged", null, preview.File.Path);
            }
            if (preview.HeadCommitHash != current.HeadCommitHash)
            {
                throw new GitException("LocalSelectedFileChanged", null, preview.File.Path);
            }
            if (preview.IndexEntry != current.IndexEntry)
            {
                throw new GitException("LocalSelectedFileChanged", null, preview.File.Path);
            }
            if (preview.WorktreeHash != current.WorktreeHash)
            {
                throw new GitException("LocalSelectedFileChanged", null, preview.File.Path);
            }
            if (preview.Text != current.Text)
            {
                throw new GitException("LocalSelectedFileChanged", null, preview.File.Path);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string[] arguments = _stageHunkArguments;
            if (preview.IsStaged == true)
            {
                arguments = _unstageHunkArguments;
            }
            await _runner.RunWithInputAsync(repository.RootPath, arguments, patch, cancellationToken);
        }

        private static bool IsHunkFile(GitWorktreeFile file, bool staged)
        {
            if (file.IsConflict == true)
            {
                return false;
            }
            if (file.IsUntracked == true)
            {
                return false;
            }
            if (string.IsNullOrEmpty(file.OriginalPath) == false)
            {
                return false;
            }
            if (file.IndexStatus != ' ')
            {
                if (file.IndexStatus != 'M')
                {
                    return false;
                }
            }
            if (file.WorktreeStatus != ' ')
            {
                if (file.WorktreeStatus != 'M')
                {
                    return false;
                }
            }
            if (staged == true)
            {
                return file.IndexStatus == 'M';
            }
            return file.WorktreeStatus == 'M';
        }

        private async Task<string> ReadHunkIndexEntryAsync(GitRepository repository, GitWorktreeFile file, CancellationToken cancellationToken)
        {
            Dictionary<string, string> entries = await GetDiscardIndexEntriesAsync(repository, [file], cancellationToken);
            if (entries.TryGetValue(file.Path, out string entry) == false)
            {
                return null;
            }
            int separator = entry.IndexOf('\t');
            if (separator < 0)
            {
                return null;
            }
            string[] fields = entry.Substring(0, separator).Split(' ');
            if (fields.Length != 3)
            {
                return null;
            }
            if (fields[2] != "0")
            {
                return null;
            }
            if (fields[0] != "100644")
            {
                if (fields[0] != "100755")
                {
                    return null;
                }
            }
            return entry;
        }

        private static async Task<string> ReadHunkWorktreeHashAsync(GitRepository repository, string path, CancellationToken cancellationToken)
        {
            string fullPath = ResolveDiscardPath(repository, path);
            FileInfo information = new(fullPath);
            if (information.Exists == false)
            {
                return null;
            }
            if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }
            using FileStream stream = File.OpenRead(fullPath);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        private static IReadOnlyList<GitDiffHunk> ReadDiffHunks(string text)
        {
            if (text.IndexOf('\0') >= 0)
            {
                return Array.Empty<GitDiffHunk>();
            }
            List<int> starts = [];
            bool hasFileHeader = false;
            bool hasIndexHeader = false;
            bool hasPreviousPath = false;
            bool hasCurrentPath = false;
            int position = 0;
            while (position < text.Length)
            {
                int end = text.IndexOf('\n', position);
                if (end < 0)
                {
                    end = text.Length;
                }
                ReadOnlySpan<char> line = text.AsSpan(position, end - position);
                if (line.StartsWith("diff --git ", StringComparison.Ordinal) == true)
                {
                    if (hasFileHeader == true)
                    {
                        return Array.Empty<GitDiffHunk>();
                    }
                    hasFileHeader = true;
                }
                else if (line.StartsWith("@@ -", StringComparison.Ordinal) == true)
                {
                    starts.Add(position);
                }
                else if (starts.Count == 0)
                {
                    if (line.StartsWith("index ", StringComparison.Ordinal) == true)
                    {
                        bool regularMode = line.EndsWith(" 100644", StringComparison.Ordinal);
                        if (line.EndsWith(" 100755", StringComparison.Ordinal) == true)
                        {
                            regularMode = true;
                        }
                        if (regularMode == false)
                        {
                            return Array.Empty<GitDiffHunk>();
                        }
                        hasIndexHeader = true;
                    }
                    else if (line.StartsWith("--- ", StringComparison.Ordinal) == true)
                    {
                        hasPreviousPath = true;
                    }
                    else if (line.StartsWith("+++ ", StringComparison.Ordinal) == true)
                    {
                        hasCurrentPath = true;
                    }
                    else
                    {
                        return Array.Empty<GitDiffHunk>();
                    }
                }
                position = end + 1;
            }
            if (hasFileHeader == false)
            {
                return Array.Empty<GitDiffHunk>();
            }
            if (hasIndexHeader == false)
            {
                return Array.Empty<GitDiffHunk>();
            }
            if (hasPreviousPath == false)
            {
                return Array.Empty<GitDiffHunk>();
            }
            if (hasCurrentPath == false)
            {
                return Array.Empty<GitDiffHunk>();
            }
            if (starts.Count == 0)
            {
                return Array.Empty<GitDiffHunk>();
            }

            List<GitDiffHunk> hunks = [];
            for (int index = 0; index < starts.Count; index++)
            {
                int end = text.Length;
                if (index + 1 < starts.Count)
                {
                    end = starts[index + 1];
                }
                hunks.Add(new GitDiffHunk(starts[index], end - starts[index]));
            }
            return hunks.AsReadOnly();
        }

        private static string BuildSelectedLinesPatch(GitFilePreview preview, IReadOnlyList<int> selectedLineOffsets, CancellationToken cancellationToken)
        {
            HashSet<int> remaining = new(selectedLineOffsets);
            if (remaining.Count == 0)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }

            string text = preview.Text;
            StringBuilder patch = new(text.Substring(0, preview.Hunks[0].Start));
            long accumulatedChange = 0;
            int includedHunks = 0;
            foreach (GitDiffHunk hunk in preview.Hunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int hunkEnd = hunk.Start + hunk.Length;
                int headerEnd = text.IndexOf('\n', hunk.Start);
                if (headerEnd < 0)
                {
                    throw new GitException("LocalSelectedFileChanged", null, preview.Path);
                }
                if (TryReadSelectedHunkHeader(text, hunk.Start, headerEnd, out int originalPreviousStart, out int originalPreviousCount, out int originalCurrentStart, out int originalCurrentCount, out int suffixStart) == false)
                {
                    throw new GitException("LocalSelectedFileChanged", null, preview.Path);
                }

                StringBuilder body = new();
                int previousCount = 0;
                int currentCount = 0;
                int originalPreviousLines = 0;
                int originalCurrentLines = 0;
                bool hasChanges = false;
                bool previousEnded = false;
                bool currentEnded = false;
                bool invalidEnding = false;
                char lastEmittedKind = '\0';
                int position = headerEnd + 1;
                while (position < hunkEnd)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int lineEnd = text.IndexOf('\n', position);
                    if (lineEnd < 0)
                    {
                        lineEnd = hunkEnd;
                    }
                    int nextPosition = lineEnd;
                    if (nextPosition < hunkEnd)
                    {
                        nextPosition++;
                    }
                    char kind = text[position];
                    if (kind == '\\')
                    {
                        if (text.AsSpan(position, lineEnd - position).StartsWith("\\ No newline at end of file", StringComparison.Ordinal) == false)
                        {
                            throw new GitException("LocalSelectedFileChanged", null, preview.Path);
                        }
                        if (lastEmittedKind != '\0')
                        {
                            body.Append(text, position, nextPosition - position);
                            if (lastEmittedKind != '+')
                            {
                                previousEnded = true;
                            }
                            if (lastEmittedKind != '-')
                            {
                                currentEnded = true;
                            }
                        }
                        lastEmittedKind = '\0';
                        position = nextPosition;
                        continue;
                    }
                    if (kind != ' ')
                    {
                        if (kind != '+')
                        {
                            if (kind != '-')
                            {
                                throw new GitException("LocalSelectedFileChanged", null, preview.Path);
                            }
                        }
                    }
                    if (kind != '+')
                    {
                        originalPreviousLines++;
                    }
                    if (kind != '-')
                    {
                        originalCurrentLines++;
                    }

                    bool selected = remaining.Remove(position);
                    if (selected == true)
                    {
                        if (kind == ' ')
                        {
                            throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                        }
                        hasChanges = true;
                    }
                    char emittedKind = kind;
                    if (selected == false)
                    {
                        if (preview.IsStaged == true)
                        {
                            if (kind == '-')
                            {
                                emittedKind = '\0';
                            }
                            if (kind == '+')
                            {
                                emittedKind = ' ';
                            }
                        }
                        else
                        {
                            if (kind == '+')
                            {
                                emittedKind = '\0';
                            }
                            if (kind == '-')
                            {
                                emittedKind = ' ';
                            }
                        }
                    }

                    lastEmittedKind = emittedKind;
                    if (emittedKind != '\0')
                    {
                        if (emittedKind != '+')
                        {
                            if (previousEnded == true)
                            {
                                invalidEnding = true;
                            }
                            previousCount++;
                        }
                        if (emittedKind != '-')
                        {
                            if (currentEnded == true)
                            {
                                invalidEnding = true;
                            }
                            currentCount++;
                        }
                        body.Append(emittedKind);
                        body.Append(text, position + 1, nextPosition - position - 1);
                    }
                    position = nextPosition;
                }
                if (originalPreviousLines != originalPreviousCount)
                {
                    throw new GitException("LocalSelectedFileChanged", null, preview.Path);
                }
                if (originalCurrentLines != originalCurrentCount)
                {
                    throw new GitException("LocalSelectedFileChanged", null, preview.Path);
                }
                if (hasChanges == false)
                {
                    continue;
                }
                if (invalidEnding == true)
                {
                    throw new GitException("LocalSelectedLinesInvalid", null, Array.Empty<object>());
                }

                long previousZeroStart = originalPreviousStart;
                long currentZeroStart = originalCurrentStart;
                if (originalPreviousCount > 0)
                {
                    previousZeroStart--;
                }
                if (originalCurrentCount > 0)
                {
                    currentZeroStart--;
                }
                if (preview.IsStaged == true)
                {
                    previousZeroStart = currentZeroStart + accumulatedChange;
                    accumulatedChange += previousCount - currentCount;
                }
                else
                {
                    currentZeroStart = previousZeroStart + accumulatedChange;
                    accumulatedChange += currentCount - previousCount;
                }
                if (previousCount > 0)
                {
                    previousZeroStart++;
                }
                if (currentCount > 0)
                {
                    currentZeroStart++;
                }
                patch.Append("@@ -").Append(previousZeroStart.ToString(CultureInfo.InvariantCulture)).Append(',').Append(previousCount.ToString(CultureInfo.InvariantCulture));
                patch.Append(" +").Append(currentZeroStart.ToString(CultureInfo.InvariantCulture)).Append(',').Append(currentCount.ToString(CultureInfo.InvariantCulture)).Append(" @@");
                patch.Append(text, suffixStart, headerEnd - suffixStart).Append('\n');
                patch.Append(body);
                includedHunks++;
            }
            if (remaining.Count != 0)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            if (includedHunks == 0)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            return patch.ToString();
        }

        private static bool TryReadSelectedHunkHeader(string text, int start, int end, out int previousStart, out int previousCount, out int currentStart, out int currentCount, out int suffixStart)
        {
            previousStart = 0;
            previousCount = 0;
            currentStart = 0;
            currentCount = 0;
            suffixStart = 0;
            int previousEnd = text.IndexOf(' ', start + 4, end - start - 4);
            if (previousEnd < 0)
            {
                return false;
            }
            if (previousEnd + 1 >= end)
            {
                return false;
            }
            if (text[previousEnd + 1] != '+')
            {
                return false;
            }
            int currentEnd = text.IndexOf(' ', previousEnd + 2, end - previousEnd - 2);
            if (currentEnd < 0)
            {
                return false;
            }
            if (currentEnd + 3 > end)
            {
                return false;
            }
            if (string.CompareOrdinal(text, currentEnd, " @@", 0, 3) != 0)
            {
                return false;
            }
            if (TryReadSelectedHunkRange(text.AsSpan(start + 4, previousEnd - start - 4), out previousStart, out previousCount) == false)
            {
                return false;
            }
            if (TryReadSelectedHunkRange(text.AsSpan(previousEnd + 2, currentEnd - previousEnd - 2), out currentStart, out currentCount) == false)
            {
                return false;
            }
            suffixStart = currentEnd + 3;
            return true;
        }

        private static bool TryReadSelectedHunkRange(ReadOnlySpan<char> range, out int start, out int count)
        {
            count = 1;
            int separator = range.IndexOf(',');
            if (separator < 0)
            {
                return int.TryParse(range, NumberStyles.None, CultureInfo.InvariantCulture, out start);
            }
            if (int.TryParse(range.Slice(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out start) == false)
            {
                return false;
            }
            return int.TryParse(range.Slice(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out count);
        }

        public async Task StageAsync(GitRepository repository, GitWorktreeFile file, CancellationToken cancellationToken = default)
        {
            if (file.IsConflict == true)
            {
                throw new GitException("WorkingResolveBeforeStage", null, file.Path);
            }

            List<string> arguments = ["add", "-A", "--", LiteralPath(file.Path)];
            if (HasUnstagedOriginalPath(file) == true)
            {
                arguments.Add(LiteralPath(file.OriginalPath));
            }

            await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
        }

        public async Task UnstageAsync(GitRepository repository, GitWorktreeFile file, CancellationToken cancellationToken = default)
        {
            List<string> arguments = await BuildUnstageArgumentsAsync(repository, cancellationToken);
            arguments.Add("--");
            arguments.Add(LiteralPath(file.Path));
            if (string.IsNullOrEmpty(file.OriginalPath) == false)
            {
                arguments.Add(LiteralPath(file.OriginalPath));
            }

            await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
        }

        public async Task UnstageFilesAsync(GitRepository repository, IReadOnlyList<GitWorktreeFile> files, CancellationToken cancellationToken = default)
        {
            if (files.Count == 0)
            {
                return;
            }
            if (files.Count == 1)
            {
                await UnstageAsync(repository, files[0], cancellationToken);
                return;
            }

            List<string> arguments = await BuildUnstageArgumentsAsync(repository, cancellationToken);
            arguments.Add("--pathspec-from-file=-");
            arguments.Add("--pathspec-file-nul");
            StringBuilder pathspecs = new();
            HashSet<string> addedPaths = new(StringComparer.Ordinal);
            foreach (GitWorktreeFile file in files)
            {
                if (addedPaths.Add(file.Path) == true)
                {
                    pathspecs.Append(LiteralPath(file.Path)).Append('\0');
                }

                if (string.IsNullOrEmpty(file.OriginalPath) == false)
                {
                    if (addedPaths.Add(file.OriginalPath) == true)
                    {
                        pathspecs.Append(LiteralPath(file.OriginalPath)).Append('\0');
                    }
                }
            }

            await _runner.RunWithInputAsync(repository.RootPath, arguments, pathspecs.ToString(), cancellationToken);
        }

        public GitDiscardPlan PrepareDiscard(GitRepository repository, GitWorktreeFile file)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (file.IsConflict == true)
            {
                throw new GitException("WorkingResolveBeforeDiscard", null, file.Path);
            }

            if (file.IsUnstaged == false)
            {
                throw new GitException("WorkingNoUnstagedChanges", null, file.Path);
            }

            string fullPath = ResolveDiscardPath(repository, file.Path);
            if (file.WorktreeStatus == 'R')
            {
                if (string.Equals(file.Path, file.OriginalPath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new GitException("WorkingCaseRenameUnsafe", null, file.Path);
                }

                string originalPath = ResolveDiscardPath(repository, file.OriginalPath);
                if (File.Exists(originalPath) == true)
                {
                    throw new GitException("WorkingOriginalPathOccupied", null, file.OriginalPath);
                }

                if (Directory.Exists(originalPath) == true)
                {
                    throw new GitException("WorkingOriginalPathOccupied", null, file.OriginalPath);
                }
            }

            FileInfo information = new(fullPath);
            bool exists = information.Exists;
            if (Directory.Exists(fullPath) == true)
            {
                throw new GitException("WorkingOnlyFileDiscard", null, file.Path);
            }

            if (file.IsUntracked == true)
            {
                if (exists == false)
                {
                    throw new GitException("WorkingUntrackedFileMissing", null, file.Path);
                }
            }

            if (file.WorktreeStatus == 'R')
            {
                if (exists == false)
                {
                    throw new GitException("WorkingRenamedFileMissing", null, file.Path);
                }
            }

            if (exists == false)
            {
                return new GitDiscardPlan(file, false, 0, default, default, default, null, null);
            }

            if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new GitException("WorkingSymlinkDiscardUnsafe", null, file.Path);
            }

            using FileStream stream = File.OpenRead(fullPath);
            string contentHash = Convert.ToHexString(SHA256.HashData(stream));
            return new GitDiscardPlan(file, true, information.Length, information.LastWriteTimeUtc, information.CreationTimeUtc, information.Attributes, information.LinkTarget, contentHash);
        }

        public async Task<IReadOnlyList<GitDiscardPlan>> PrepareDiscardsAsync(GitRepository repository, IEnumerable<GitWorktreeFile> files, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(files);
            List<GitWorktreeFile> selected = files.ToList();
            if (selected.Count == 0)
            {
                throw new GitException("WorkingSelectDiscardFile", null, Array.Empty<object>());
            }

            GitWorktreeStatus status = await GetStatusAsync(repository, cancellationToken);
            Dictionary<string, List<GitWorktreeFile>> currentFiles = status.Files.GroupBy(file => file.Path, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            Dictionary<string, string> indexEntries = await GetDiscardIndexEntriesAsync(repository, selected, cancellationToken);
            return await Task.Run(() =>
            {
                List<GitDiscardPlan> plans = [];
                HashSet<string> paths = new(StringComparer.Ordinal);
                foreach (GitWorktreeFile file in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (paths.Add(file.Path) == false)
                    {
                        throw new GitException("WorkingDuplicateSelection", null, file.Path);
                    }

                    if (currentFiles.TryGetValue(file.Path, out List<GitWorktreeFile> candidates) == false)
                    {
                        throw new GitException("WorkingSelectedFileChanged", null, file.Path);
                    }

                    GitWorktreeFile current = candidates.FirstOrDefault(candidate => DiscardStatusMatches(file, candidate));
                    if (current == null)
                    {
                        throw new GitException("WorkingSelectedFileChanged", null, file.Path);
                    }

                    GitDiscardPlan plan = PrepareDiscard(repository, current);
                    plan.IndexEntries = GetDiscardIndexFingerprint(indexEntries, current);
                    plans.Add(plan);
                }

                return (IReadOnlyList<GitDiscardPlan>)plans;
            }, cancellationToken);
        }

        public async Task<GitDiscardBatchResult> ApplyDiscardsAsync(GitRepository repository, IReadOnlyList<GitDiscardPlan> plans, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(plans);
            if (plans.Count == 0)
            {
                throw new GitException("WorkingSelectDiscardFile", null, Array.Empty<object>());
            }

            IReadOnlyList<GitDiscardPlan> current = await PrepareDiscardsAsync(repository, plans.Select(plan => plan.File), cancellationToken);
            for (int index = 0; index < plans.Count; index++)
            {
                if (DiscardFileMatches(plans[index], current[index]) == false)
                {
                    throw new GitException("WorkingSelectedFileChanged", null, plans[index].Path);
                }

                if (plans[index].IndexEntries != current[index].IndexEntries)
                {
                    throw new GitException("WorkingIndexChanged", null, plans[index].Path);
                }
            }

            foreach (GitDiscardPlan plan in current)
            {
                if (plan.IsWorktreeRename == false)
                {
                    continue;
                }

                GitCommandResult tracked = await _runner.RunAsync(repository.RootPath, new string[] { "ls-files", "-z", "--", LiteralPath(plan.Path) }, false, cancellationToken);
                if (tracked.Output.Length > 0)
                {
                    throw new GitException("WorkingRenamedDestinationTracked", null, plan.Path);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<string> completed = [];
            List<GitDiscardPlan> regular = current.Where(plan => plan.IsUntracked == false && plan.IsWorktreeRename == false).ToList();
            try
            {
                if (regular.Count > 0)
                {
                    StringBuilder pathspecs = new();
                    foreach (GitDiscardPlan plan in regular)
                    {
                        string fullPath = ResolveDiscardPath(repository, plan.Path);
                        if (Directory.Exists(fullPath) == true)
                        {
                            throw new GitException("WorkingOnlyFileDiscard", null, plan.Path);
                        }

                        pathspecs.Append(LiteralPath(plan.Path)).Append('\0');
                    }

                    await _runner.RunWithInputAsync(repository.RootPath, _restoreBatchArguments, pathspecs.ToString(), cancellationToken);
                    completed.AddRange(regular.Select(plan => plan.Path));
                }

                foreach (GitDiscardPlan plan in current)
                {
                    if (plan.IsWorktreeRename == false)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    string originalPath = ResolveDiscardPath(repository, plan.File.OriginalPath);
                    if (File.Exists(originalPath) == true)
                    {
                        throw new GitException("WorkingOriginalPathOccupied", null, plan.File.OriginalPath);
                    }

                    if (Directory.Exists(originalPath) == true)
                    {
                        throw new GitException("WorkingOriginalPathOccupied", null, plan.File.OriginalPath);
                    }

                    await _runner.RunAsync(repository.RootPath, new string[] { "restore", "--worktree", "--", LiteralPath(plan.File.OriginalPath) }, false, cancellationToken);
                    if (DiscardDestinationMatches(repository, plan) == false)
                    {
                        throw new GitException("WorkingRenamedFileChanged", null, plan.Path);
                    }

                    File.Delete(ResolveDiscardPath(repository, plan.Path));
                    completed.Add(plan.Path);
                }

                foreach (GitDiscardPlan plan in current)
                {
                    if (plan.IsUntracked == false)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (DiscardDestinationMatches(repository, plan) == false)
                    {
                        throw new GitException("WorkingUntrackedFileChanged", null, plan.Path);
                    }

                    File.Delete(ResolveDiscardPath(repository, plan.Path));
                    completed.Add(plan.Path);
                }

                return new GitDiscardBatchResult(completed, Array.Empty<string>(), null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                List<string> remaining = current.Select(plan => plan.Path).Where(path => completed.Contains(path) == false).ToList();
                return new GitDiscardBatchResult(completed, remaining, exception);
            }
        }

        public async Task ApplyDiscardAsync(GitRepository repository, GitDiscardPlan plan, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            GitDiscardBatchResult result = await ApplyDiscardsAsync(repository, new GitDiscardPlan[] { plan }, cancellationToken);
            if (result.HasError == true)
            {
                ExceptionDispatchInfo.Capture(result.Error).Throw();
            }
        }

        public async Task<GitStopTrackingPlan> PrepareStopTrackingAsync(GitRepository repository, IEnumerable<GitWorktreeFile> files, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GitDiscardPlan> plans = await PrepareDiscardsAsync(repository, files, cancellationToken);
            foreach (GitDiscardPlan plan in plans)
            {
                ValidateStopTrackingPlan(plan);
            }

            GitIgnorePlan ignore = await Ignore.PrepareStopTrackingAsync(repository, plans, cancellationToken);
            return new GitStopTrackingPlan(plans, ignore);
        }

        public async Task StopTrackingAsync(GitRepository repository, GitStopTrackingPlan plan, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(plan);
            IReadOnlyList<GitDiscardPlan> plans = plan.Files;
            if (plans.Count == 0)
            {
                throw new GitException("WorkingSelectDiscardFile", null, Array.Empty<object>());
            }

            GitStopTrackingPlan currentPlan = await PrepareStopTrackingAsync(repository, plans.Select(file => file.File), cancellationToken);
            IReadOnlyList<GitDiscardPlan> current = currentPlan.Files;
            for (int index = 0; index < plans.Count; index++)
            {
                if (DiscardFileMatches(plans[index], current[index]) == false)
                {
                    throw new GitException("WorkingSelectedFileChanged", null, plans[index].Path);
                }

                if (plans[index].IndexEntries != current[index].IndexEntries)
                {
                    throw new GitException("WorkingIndexChanged", null, plans[index].Path);
                }
            }

            GitIgnoreService.ValidatePlan(plan.Ignore, currentPlan.Ignore);
            List<string> arguments = ["rm", "--cached", "--"];
            foreach (GitDiscardPlan file in current)
            {
                arguments.Add(LiteralPath(file.Path));
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<string> previewArguments = new(arguments);
            previewArguments.Insert(1, "--dry-run");
            await _runner.RunAsync(repository.RootPath, previewArguments, false, cancellationToken);
            await GitIgnoreService.ApplyRulesAsync(currentPlan.Ignore, cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
            }
            catch (Exception exception)
            {
                if (currentPlan.Ignore.Entries.Count > 0)
                {
                    throw new GitException("WorkingStopTrackingPartialFailure", exception, Array.Empty<object>());
                }

                throw;
            }
        }

        public async Task StageAllAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            GitWorktreeStatus status = await GetStatusAsync(repository, cancellationToken);
            await StageFilesAsync(repository, status.Files, cancellationToken);
        }

        public async Task<GitStagePlan> PrepareStageSelectedAsync(GitRepository repository, string selectedPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(selectedPath) == true)
            {
                throw new GitException("WorkingSelectedPathRequired", null, Array.Empty<object>());
            }

            return await PrepareStageSelectionAsync(repository, new string[] { selectedPath }, cancellationToken);
        }

        public Task<GitStagePlan> PrepareStageSelectionAsync(GitRepository repository, IReadOnlyList<string> selectedPaths, CancellationToken cancellationToken = default)
        {
            if (selectedPaths == null)
            {
                throw new GitException("WorkingSelectedPathRequired", null, Array.Empty<object>());
            }
            if (selectedPaths.Count == 0)
            {
                throw new GitException("WorkingSelectedPathRequired", null, Array.Empty<object>());
            }
            return PrepareStagePlanAsync(repository, selectedPaths.ToArray(), cancellationToken);
        }

        public async Task<GitStagePlan> PrepareStageAllAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            return await PrepareStagePlanAsync(repository, null, cancellationToken);
        }

        public async Task ApplyStagePlanAsync(GitRepository repository, GitStagePlan plan, CancellationToken cancellationToken = default)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            GitStagePlan current = await PrepareStagePlanAsync(repository, plan.SelectedPaths, cancellationToken);
            if (StagePlansMatch(plan, current) == false)
            {
                throw new GitException("WorkingStageStateChanged", null, Array.Empty<object>());
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (plan.SelectedPaths != null)
            {
                if (plan.SelectedPaths.Count == 1)
                {
                    await StageAsync(repository, current.Files[0], cancellationToken);
                    return;
                }
            }

            await StageFilesAsync(repository, current.Files, cancellationToken);
        }

        private async Task<GitStagePlan> PrepareStagePlanAsync(GitRepository repository, IReadOnlyList<string> selectedPaths, CancellationToken cancellationToken)
        {
            HashSet<string> remainingPaths = null;
            if (selectedPaths != null)
            {
                if (selectedPaths.Count == 0)
                {
                    throw new GitException("WorkingSelectedPathRequired", null, Array.Empty<object>());
                }
                foreach (string path in selectedPaths)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        throw new GitException("WorkingSelectedPathRequired", null, Array.Empty<object>());
                    }
                }
                remainingPaths = new HashSet<string>(selectedPaths, StringComparer.Ordinal);
            }
            GitWorktreeStatus status = await GetStatusAsync(repository, cancellationToken);
            List<GitWorktreeFile> files = [];
            List<GitLargeFileCandidate> largeFiles = [];
            foreach (GitWorktreeFile file in status.Files)
            {
                if (file.IsUnstaged == false)
                {
                    continue;
                }

                if (file.IsConflict == true)
                {
                    continue;
                }

                if (remainingPaths != null)
                {
                    if (remainingPaths.Remove(file.Path) == false)
                    {
                        continue;
                    }
                }

                files.Add(file);
                bool isNewFile = file.IsUntracked;
                if (file.WorktreeStatus == 'A')
                {
                    isNewFile = true;
                }

                if (isNewFile == false)
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(Path.Combine(repository.RootPath, file.Path));
                if (Directory.Exists(fullPath) == true)
                {
                    continue;
                }

                FileInfo information = new(fullPath);
                if (information.LinkTarget != null)
                {
                    continue;
                }

                if (information.Exists == false)
                {
                    throw new GitException("WorkingNewFileDisappeared", null, file.Path);
                }

                if (information.Length >= _largeNewFileThreshold)
                {
                    largeFiles.Add(new GitLargeFileCandidate(file.Path, information.Length));
                }
            }

            if (remainingPaths != null)
            {
                if (remainingPaths.Count > 0)
                {
                    throw new GitException("WorkingStageFileUnavailable", null, remainingPaths.First());
                }
            }

            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            largeFiles.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            return new GitStagePlan(files, largeFiles, selectedPaths);
        }

        private static bool StagePlansMatch(GitStagePlan expected, GitStagePlan actual)
        {
            if (expected.Files.Count != actual.Files.Count)
            {
                return false;
            }

            if (expected.LargeFiles.Count != actual.LargeFiles.Count)
            {
                return false;
            }

            for (int index = 0; index < expected.Files.Count; index++)
            {
                GitWorktreeFile left = expected.Files[index];
                GitWorktreeFile right = actual.Files[index];
                if (left.Path != right.Path)
                {
                    return false;
                }

                if (left.OriginalPath != right.OriginalPath)
                {
                    return false;
                }

                if (left.IndexStatus != right.IndexStatus)
                {
                    return false;
                }

                if (left.WorktreeStatus != right.WorktreeStatus)
                {
                    return false;
                }
            }

            for (int index = 0; index < expected.LargeFiles.Count; index++)
            {
                GitLargeFileCandidate left = expected.LargeFiles[index];
                GitLargeFileCandidate right = actual.LargeFiles[index];
                if (left.Path != right.Path)
                {
                    return false;
                }

                if (left.SizeBytes != right.SizeBytes)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task StageFilesAsync(GitRepository repository, IEnumerable<GitWorktreeFile> files, CancellationToken cancellationToken)
        {
            StringBuilder pathspecs = new();
            HashSet<string> addedPaths = new(StringComparer.Ordinal);
            foreach (GitWorktreeFile file in files)
            {
                if (file.IsUnstaged == false)
                {
                    continue;
                }

                if (file.IsConflict == true)
                {
                    continue;
                }

                if (addedPaths.Add(file.Path) == true)
                {
                    pathspecs.Append(LiteralPath(file.Path)).Append('\0');
                }

                if (HasUnstagedOriginalPath(file) == true && addedPaths.Add(file.OriginalPath) == true)
                {
                    pathspecs.Append(LiteralPath(file.OriginalPath)).Append('\0');
                }
            }

            if (pathspecs.Length == 0)
            {
                return;
            }

            await _runner.RunWithInputAsync(repository.RootPath, _stageBatchArguments, pathspecs.ToString(), cancellationToken);
        }

        public async Task UnstageAllAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            List<string> arguments = await BuildUnstageArgumentsAsync(repository, cancellationToken);
            arguments.Add("--");
            arguments.Add(":/");
            await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
        }

        public async Task<string> GetHeadCommitMessageAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            GitCommandResult head = await _runner.RunAsync(repository.RootPath, _headMessageArguments, true, cancellationToken);
            if (head.ExitCode != 0)
            {
                throw new GitException("WorkingNoPreviousCommit", null, Array.Empty<object>());
            }

            return await GetHeadCommitMessageAsync(repository, head.Output.Trim(), cancellationToken);
        }

        public async Task<string> GetHeadCommitMessageAsync(GitRepository repository, string commitHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(commitHash) == true)
            {
                throw new GitException("WorkingNoPreviousCommit", null, Array.Empty<object>());
            }
            byte[] content = await _runner.RunBytesAsync(repository.RootPath, new string[] { "cat-file", "commit", commitHash }, 1048576, cancellationToken);
            string raw;
            try
            {
                raw = new UTF8Encoding(false, true).GetString(content);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException("WorkingPreviousMessageInvalidUtf8", exception, Array.Empty<object>());
            }

            int messageStart = raw.IndexOf("\n\n", StringComparison.Ordinal);
            if (messageStart < 0)
            {
                throw new GitException("WorkingCommitMessageBoundaryMissing", null, Array.Empty<object>());
            }

            return raw.Substring(messageStart + 2);
        }

        public async Task<GitCommitResult> CommitAsync(GitRepository repository, string message, bool amend, CancellationToken cancellationToken = default)
        {
            GitWorktreeStatus expectedStatus = await GetStatusAsync(repository, cancellationToken);
            return await CommitAsync(repository, message, amend, expectedStatus, cancellationToken);
        }

        public async Task<GitCommitResult> CommitAsync(GitRepository repository, string message, bool amend, GitWorktreeStatus expectedStatus, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message) == true)
            {
                throw new GitException("WorkingCommitMessageRequired", null, Array.Empty<object>());
            }

            string firstLine = message.Split('\n')[0];
            if (string.IsNullOrWhiteSpace(firstLine) == true)
            {
                throw new GitException("WorkingCommitSummaryRequired", null, Array.Empty<object>());
            }

            GitCommandResult name = await _runner.RunAsync(repository.RootPath, _userNameArguments, true, cancellationToken);
            GitCommandResult email = await _runner.RunAsync(repository.RootPath, _userEmailArguments, true, cancellationToken);
            GitWorktreeStatus status = await GetStatusAsync(repository, cancellationToken);
            if (status.Files.Any(file => file.IsStaged) == false)
            {
                throw new GitException("WorkingStageBeforeCommit", null, Array.Empty<object>());
            }

            if (status.Files.Any(file => file.IsConflict) == true)
            {
                throw new GitException("WorkingResolveBeforeCommit", null, Array.Empty<object>());
            }

            if (expectedStatus == null)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            if (expectedStatus.HeadCommitHash == null)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            if (expectedStatus.HeadCommitHash != status.HeadCommitHash)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            ValidateSelectedFiles(expectedStatus.Files.Where(file => file.IsStaged).ToArray(), status.Files.Where(file => file.IsStaged).ToArray(), true);
            if (expectedStatus.IndexEntries.Count != status.IndexEntries.Count)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }
            foreach (KeyValuePair<string, string> entry in expectedStatus.IndexEntries)
            {
                if (status.IndexEntries.TryGetValue(entry.Key, out string currentEntry) == false)
                {
                    throw new GitException("LocalIndexChanged", null, entry.Key);
                }
                if (entry.Value != currentEntry)
                {
                    throw new GitException("LocalIndexChanged", null, entry.Key);
                }
            }

            if (string.IsNullOrWhiteSpace(name.Output) == true)
            {
                throw new GitException("WorkingAuthorRequired", null, Array.Empty<object>());
            }
            if (string.IsNullOrWhiteSpace(email.Output) == true)
            {
                throw new GitException("WorkingAuthorRequired", null, Array.Empty<object>());
            }

            List<string> arguments = ["commit", "--cleanup=verbatim", "--file=-"];

            if (amend == true)
            {
                arguments.Add("--amend");
            }

            await _runner.RunWithInputAsync(repository.RootPath, arguments, message, cancellationToken);
            try
            {
                GitCommandResult head = await _runner.RunAsync(repository.RootPath, _headArguments, false, cancellationToken);
                return new GitCommitResult(head.Output.Trim(), null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new GitCommitResult(null, exception);
            }
        }

        private async Task<List<string>> BuildUnstageArgumentsAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult head = await _runner.RunAsync(repository.RootPath, _verifyHeadArguments, true, cancellationToken);
            if (head.ExitCode == 0)
            {
                return ["restore", "--staged"];
            }

            return ["rm", "--cached", "-r", "-f"];
        }

        private static string LiteralPath(string path)
        {
            return $":(literal){path}";
        }

        private static bool HasUnstagedOriginalPath(GitWorktreeFile file)
        {
            if (string.IsNullOrEmpty(file.OriginalPath) == true)
            {
                return false;
            }

            return file.WorktreeStatus == 'R' || file.WorktreeStatus == 'C';
        }

        private static bool DiscardStatusMatches(GitWorktreeFile expected, GitWorktreeFile current)
        {
            if (current.IsConflict == true)
            {
                return false;
            }

            if (current.IsUnstaged == false)
            {
                return false;
            }

            if (expected.Path != current.Path)
            {
                return false;
            }

            if (expected.OriginalPath != current.OriginalPath)
            {
                return false;
            }

            if (expected.IndexStatus != current.IndexStatus)
            {
                return false;
            }

            return expected.WorktreeStatus == current.WorktreeStatus;
        }

        private static bool DiscardFileMatches(GitDiscardPlan expected, GitDiscardPlan current)
        {
            if (expected.Exists != current.Exists)
            {
                return false;
            }

            if (expected.Exists == false)
            {
                return true;
            }

            if (expected.Length != current.Length)
            {
                return false;
            }

            if (expected.LastWriteUtc != current.LastWriteUtc)
            {
                return false;
            }

            if (expected.CreationUtc != current.CreationUtc)
            {
                return false;
            }

            if (expected.Attributes != current.Attributes)
            {
                return false;
            }

            if (expected.LinkTarget != current.LinkTarget)
            {
                return false;
            }

            return expected.ContentHash == current.ContentHash;
        }

        private static void ValidateStopTrackingPlan(GitDiscardPlan plan)
        {
            if (plan.IsUntracked == true)
            {
                throw new GitException("WorkingSelectedFileChanged", null, plan.Path);
            }

            if (plan.File.IsConflict == true)
            {
                throw new GitException("WorkingSelectedFileChanged", null, plan.Path);
            }

            if (string.IsNullOrEmpty(plan.File.OriginalPath) == false)
            {
                throw new GitException("WorkingSelectedFileChanged", null, plan.Path);
            }
        }

        private async Task<Dictionary<string, string>> GetDiscardIndexEntriesAsync(GitRepository repository, IReadOnlyList<GitWorktreeFile> files, CancellationToken cancellationToken)
        {
            HashSet<string> selectedPaths = new(StringComparer.Ordinal);
            foreach (GitWorktreeFile file in files)
            {
                if (file.IsUntracked == true)
                {
                    continue;
                }

                selectedPaths.Add(file.Path);
                if (string.IsNullOrEmpty(file.OriginalPath) == false)
                {
                    selectedPaths.Add(file.OriginalPath);
                }
            }

            if (selectedPaths.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            List<string> arguments = ["ls-files", "--stage", "-z"];
            int argumentLength = selectedPaths.Sum(path => path.Length + 12);
            if (argumentLength <= 12000)
            {
                arguments.Add("--");
                arguments.AddRange(selectedPaths.Select(LiteralPath));
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
            Dictionary<string, StringBuilder> entries = new(StringComparer.Ordinal);
            foreach (string entry in result.Output.Split('\0'))
            {
                int separator = entry.IndexOf('\t');
                if (separator < 0)
                {
                    continue;
                }

                string path = entry.Substring(separator + 1);
                if (selectedPaths.Contains(path) == false)
                {
                    continue;
                }

                if (entries.TryGetValue(path, out StringBuilder builder) == false)
                {
                    builder = new StringBuilder();
                    entries.Add(path, builder);
                }

                builder.Append(entry).Append('\0');
            }

            return entries.ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.Ordinal);
        }

        private static string GetDiscardIndexFingerprint(Dictionary<string, string> entries, GitWorktreeFile file)
        {
            entries.TryGetValue(file.Path, out string currentEntry);
            string originalEntry = null;
            if (string.IsNullOrEmpty(file.OriginalPath) == false)
            {
                entries.TryGetValue(file.OriginalPath, out originalEntry);
            }

            return $"{currentEntry}\0{originalEntry}";
        }

        private static bool DiscardDestinationMatches(GitRepository repository, GitDiscardPlan plan)
        {
            string fullPath = ResolveDiscardPath(repository, plan.Path);
            FileInfo information = new(fullPath);
            if (information.Exists == false)
            {
                return false;
            }

            if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            if (information.Length != plan.Length)
            {
                return false;
            }

            if (information.LastWriteTimeUtc != plan.LastWriteUtc)
            {
                return false;
            }

            if (information.CreationTimeUtc != plan.CreationUtc)
            {
                return false;
            }

            if (information.Attributes != plan.Attributes)
            {
                return false;
            }

            if (information.LinkTarget != plan.LinkTarget)
            {
                return false;
            }

            using FileStream stream = File.OpenRead(fullPath);
            return Convert.ToHexString(SHA256.HashData(stream)) == plan.ContentHash;
        }

        private static string ResolveDiscardPath(GitRepository repository, string path)
        {
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                throw new GitException("WorkingRelativePathMissing", null, Array.Empty<object>());
            }

            if (Path.IsPathRooted(path) == true)
            {
                throw new GitException("WorkingPathOutsideRoot", null, path);
            }

            string root = Path.GetFullPath(repository.RootPath);
            string fullPath = Path.GetFullPath(Path.Combine(root, path));
            string relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == ".")
            {
                throw new GitException("WorkingRootDiscardDenied", null, Array.Empty<object>());
            }

            if (Path.IsPathRooted(relativePath) == true)
            {
                throw new GitException("WorkingPathOutsideRoot", null, path);
            }

            if (relativePath == "..")
            {
                throw new GitException("WorkingPathOutsideRoot", null, path);
            }

            if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) == true)
            {
                throw new GitException("WorkingPathOutsideRoot", null, path);
            }

            string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = root;
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index].Equals(".git", StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new GitException("WorkingMetadataDiscardDenied", null, path);
                }

                if (index == segments.Length - 1)
                {
                    break;
                }

                parent = Path.Combine(parent, segments[index]);
                if (File.Exists(parent) == true)
                {
                    throw new GitException("WorkingFileParentInvalid", null, path);
                }

                if (Directory.Exists(parent) == false)
                {
                    continue;
                }

                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitException("WorkingPathSymlink", null, path);
                }
            }

            return fullPath;
        }

        private static long GetWorkingFileSize(GitRepository repository, string path)
        {
            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(repository.RootPath, path));
            if (File.Exists(fullPath) == false)
            {
                return 0;
            }

            return new FileInfo(fullPath).Length;
        }

        private static async Task<GitFilePreview> ReadUntrackedAsync(GitRepository repository, string path, CancellationToken cancellationToken)
        {
            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(repository.RootPath, path));
            FileInfo info = new(fullPath);
            if (info.Length > 1048576)
            {
                return new GitFilePreview(string.Empty, "WorktreePreviewNewFileTooLarge", false, info.Length, info.Length);
            }

            byte[] content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            if (HasBinaryContent(content) == true)
            {
                return new GitFilePreview(string.Empty, "WorktreePreviewNewBinaryFile", true, content.Length, content.Length);
            }

            try
            {
                string text = new UTF8Encoding(false, true).GetString(content);
                if (text.Length > 0 && text[0] == '\uFEFF')
                {
                    text = text.Substring(1);
                }

                return new GitFilePreview(text, "WorktreePreviewNewTextFile", false, content.Length, content.Length);
            }
            catch (DecoderFallbackException)
            {
                return new GitFilePreview(string.Empty, "WorktreePreviewNewBinaryOrInvalidUtf8", true, content.Length, content.Length);
            }
        }

        private static bool HasBinaryContent(byte[] content)
        {
            foreach (byte value in content)
            {
                if (value < 0x20 && value != '\t' && value != '\n' && value != '\r')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
