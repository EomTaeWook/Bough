using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.Core.Git
{
    public class GitWorkingTreeService
    {
        private const long _largeNewFileThreshold = 10 * 1024 * 1024;
        private static readonly string[] _statusArguments = new string[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" };
        private static readonly string[] _restoreBatchArguments = new string[] { "restore", "--worktree", "--pathspec-from-file=-", "--pathspec-file-nul" };
        private static readonly string[] _stageBatchArguments = new string[] { "add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul" };
        private static readonly string[] _headMessageArguments = new string[] { "rev-parse", "--verify", "HEAD^{commit}" };
        private static readonly string[] _userNameArguments = new string[] { "config", "--get", "user.name" };
        private static readonly string[] _userEmailArguments = new string[] { "config", "--get", "user.email" };
        private static readonly string[] _headArguments = new string[] { "rev-parse", "HEAD" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "HEAD" };
        private readonly GitCommandRunner _runner;

        public GitWorkingTreeService(GitCommandRunner runner)
        {
            _runner = runner;
            Ignore = new GitIgnoreService(this, runner);
        }

        public GitIgnoreService Ignore { get; }

        public async Task<GitWorktreeStatus> GetStatusAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _statusArguments, false, cancellationToken);
            ArrayQueue<GitWorktreeFile> files = [];
            string[] entries = result.Output.Split('\0');
            if (entries[entries.Length - 1].Length != 0)
            {
                throw new GitException("Git status did not end with a NUL separator.");
            }

            for (int index = 0; index < entries.Length - 1; index++)
            {
                string entry = entries[index];
                if (entry.Length < 4 || entry[2] != ' ')
                {
                    throw new GitException($"Git returned an invalid status entry at position {index}.");
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
                        throw new GitException($"Git returned a rename without its original path: {path}.");
                    }

                    originalPath = entries[index];
                }

                files.Add(new GitWorktreeFile(path, originalPath, indexStatus, worktreeStatus));
            }

            return new GitWorktreeStatus(files);
        }

        public async Task<GitFilePreview> GetPreviewAsync(GitRepository repository, GitWorktreeFile file, bool staged, CancellationToken cancellationToken = default)
        {
            if (file.IsConflict == true)
            {
                return new GitFilePreview(string.Empty, "Unresolved conflict. Open Resolve to choose the final content.", false, 0);
            }

            if (file.IsUntracked == true && staged == false)
            {
                return await ReadUntrackedAsync(repository, file.Path, cancellationToken);
            }

            List<string> arguments = ["diff", "--no-ext-diff", "--no-color"];
            List<string> numstatArguments = ["diff", "--numstat", "--no-ext-diff"];
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

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
            GitCommandResult numstat = await _runner.RunAsync(repository.RootPath, numstatArguments, false, cancellationToken);
            bool isBinary = numstat.Output.StartsWith("-\t-\t", StringComparison.Ordinal);
            long size = GetWorkingFileSize(repository, file.Path);
            if (isBinary == true)
            {
                return new GitFilePreview(string.Empty, $"Binary file changed ({size} bytes in working tree).", true, size);
            }

            if (result.Output.Length == 0)
            {
                return new GitFilePreview(string.Empty, "No text diff is available for this side of the change.", false, size);
            }

            return new GitFilePreview(result.Output, $"{file.StatusText} · {size} bytes in working tree", false, size);
        }

        public async Task StageAsync(GitRepository repository, GitWorktreeFile file, CancellationToken cancellationToken = default)
        {
            if (file.IsConflict == true)
            {
                throw new GitException($"Resolve the conflict before staging: {file.Path}.");
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
                throw new GitException($"Resolve the conflict before discarding changes: {file.Path}.");
            }

            if (file.IsUnstaged == false)
            {
                throw new GitException($"The file has no unstaged changes to discard: {file.Path}.");
            }

            string fullPath = ResolveDiscardPath(repository, file.Path);
            if (file.WorktreeStatus == 'R')
            {
                if (string.Equals(file.Path, file.OriginalPath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new GitException($"A case-only rename cannot be discarded safely: {file.Path}.");
                }

                string originalPath = ResolveDiscardPath(repository, file.OriginalPath);
                if (File.Exists(originalPath) == true)
                {
                    throw new GitException($"The original rename path is occupied: {file.OriginalPath}.");
                }

                if (Directory.Exists(originalPath) == true)
                {
                    throw new GitException($"The original rename path is occupied: {file.OriginalPath}.");
                }
            }

            FileInfo information = new(fullPath);
            bool exists = information.Exists;
            if (Directory.Exists(fullPath) == true)
            {
                throw new GitException($"Only a file can be discarded: {file.Path}.");
            }

            if (file.IsUntracked == true)
            {
                if (exists == false)
                {
                    throw new GitException($"The untracked file is no longer available: {file.Path}.");
                }
            }

            if (file.WorktreeStatus == 'R')
            {
                if (exists == false)
                {
                    throw new GitException($"The renamed file is no longer available: {file.Path}.");
                }
            }

            if (exists == false)
            {
                return new GitDiscardPlan(file, false, 0, default, default, default, null, null);
            }

            if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new GitException($"A symbolic link cannot be discarded safely: {file.Path}.");
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
                throw new GitException("Select at least one file to discard.");
            }

            GitWorktreeStatus status = await GetStatusAsync(repository, cancellationToken);
            Dictionary<string, GitWorktreeFile> currentFiles = status.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
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
                        throw new GitException($"A file was selected more than once: {file.Path}.");
                    }

                    if (currentFiles.TryGetValue(file.Path, out GitWorktreeFile current) == false)
                    {
                        throw new GitException($"The selected file changed before discard: {file.Path}.");
                    }

                    if (DiscardStatusMatches(file, current) == false)
                    {
                        throw new GitException($"The selected file changed before discard: {file.Path}.");
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
                throw new GitException("Select at least one file to discard.");
            }

            IReadOnlyList<GitDiscardPlan> current = await PrepareDiscardsAsync(repository, plans.Select(plan => plan.File), cancellationToken);
            for (int index = 0; index < plans.Count; index++)
            {
                if (DiscardFileMatches(plans[index], current[index]) == false)
                {
                    throw new GitException($"The selected file changed before discard: {plans[index].Path}.");
                }

                if (plans[index].IndexEntries != current[index].IndexEntries)
                {
                    throw new GitException($"The index changed before discard: {plans[index].Path}.");
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
                    throw new GitException($"The renamed destination is tracked and cannot be deleted: {plan.Path}.");
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
                            throw new GitException($"Only a file can be discarded: {plan.Path}.");
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
                        throw new GitException($"The original rename path is occupied: {plan.File.OriginalPath}.");
                    }

                    if (Directory.Exists(originalPath) == true)
                    {
                        throw new GitException($"The original rename path is occupied: {plan.File.OriginalPath}.");
                    }

                    await _runner.RunAsync(repository.RootPath, new string[] { "restore", "--worktree", "--", LiteralPath(plan.File.OriginalPath) }, false, cancellationToken);
                    if (DiscardDestinationMatches(repository, plan) == false)
                    {
                        throw new GitException($"The renamed file changed before deletion: {plan.Path}.");
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
                        throw new GitException($"The untracked file changed before deletion: {plan.Path}.");
                    }

                    File.Delete(ResolveDiscardPath(repository, plan.Path));
                    completed.Add(plan.Path);
                }

                return new GitDiscardBatchResult(completed, Array.Empty<string>(), null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                List<string> remaining = current.Select(plan => plan.Path).Where(path => completed.Contains(path) == false).ToList();
                return new GitDiscardBatchResult(completed, remaining, exception.Message);
            }
        }

        public async Task ApplyDiscardAsync(GitRepository repository, GitDiscardPlan plan, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            GitDiscardBatchResult result = await ApplyDiscardsAsync(repository, new GitDiscardPlan[] { plan }, cancellationToken);
            if (result.HasError == true)
            {
                throw new GitException(result.Error);
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
                throw new ArgumentException("A selected path is required.", nameof(selectedPath));
            }

            return await PrepareStagePlanAsync(repository, selectedPath, cancellationToken);
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

            GitStagePlan current = await PrepareStagePlanAsync(repository, plan.SelectedPath, cancellationToken);
            if (StagePlansMatch(plan, current) == false)
            {
                throw new GitException("Working tree changed while staging was awaiting confirmation. Refresh and try again.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (plan.SelectedPath != null)
            {
                await StageAsync(repository, current.Files[0], cancellationToken);
                return;
            }

            await StageFilesAsync(repository, current.Files, cancellationToken);
        }

        private async Task<GitStagePlan> PrepareStagePlanAsync(GitRepository repository, string selectedPath, CancellationToken cancellationToken)
        {
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

                if (selectedPath != null)
                {
                    if (file.Path != selectedPath)
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
                    throw new GitException($"New file disappeared before staging: {file.Path}.");
                }

                if (information.Length >= _largeNewFileThreshold)
                {
                    largeFiles.Add(new GitLargeFileCandidate(file.Path, information.Length));
                }
            }

            if (selectedPath != null && files.Count == 0)
            {
                throw new GitException($"File is no longer available to stage: {selectedPath}.");
            }

            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            largeFiles.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            return new GitStagePlan(selectedPath, files, largeFiles);
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
                throw new GitException("There is no previous commit to amend.");
            }

            byte[] content = await _runner.RunBytesAsync(repository.RootPath, new string[] { "cat-file", "commit", head.Output.Trim() }, 1048576, cancellationToken);
            string raw;
            try
            {
                raw = new UTF8Encoding(false, true).GetString(content);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException("The previous commit message is not valid UTF-8.", exception);
            }

            int messageStart = raw.IndexOf("\n\n", StringComparison.Ordinal);
            if (messageStart < 0)
            {
                throw new GitException("Git returned a commit without a message boundary.");
            }

            return raw.Substring(messageStart + 2);
        }

        public async Task<string> CommitAsync(GitRepository repository, string message, bool amend, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message) == true)
            {
                throw new GitException("Enter a commit message.");
            }

            string firstLine = message.Split('\n')[0];
            if (string.IsNullOrWhiteSpace(firstLine) == true)
            {
                throw new GitException("Enter the first line of the commit message.");
            }

            GitWorktreeStatus status = await GetStatusAsync(repository, cancellationToken);
            if (status.Files.Any(file => file.IsStaged) == false)
            {
                throw new GitException("Stage at least one file before committing.");
            }

            if (status.Files.Any(file => file.IsConflict) == true)
            {
                throw new GitException("Resolve all conflicts before committing.");
            }

            GitCommandResult name = await _runner.RunAsync(repository.RootPath, _userNameArguments, true, cancellationToken);
            GitCommandResult email = await _runner.RunAsync(repository.RootPath, _userEmailArguments, true, cancellationToken);
            if (string.IsNullOrWhiteSpace(name.Output) == true || string.IsNullOrWhiteSpace(email.Output) == true)
            {
                throw new GitException("Set Git user.name and user.email before committing.");
            }

            List<string> arguments = ["commit", "--cleanup=verbatim", "--file=-"];

            if (amend == true)
            {
                arguments.Add("--amend");
            }

            await _runner.RunWithInputAsync(repository.RootPath, arguments, message, cancellationToken);
            GitCommandResult head = await _runner.RunAsync(repository.RootPath, _headArguments, false, cancellationToken);
            return head.Output.Trim();
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
                throw new GitException("The selected file has no repository-relative path.");
            }

            if (Path.IsPathRooted(path) == true)
            {
                throw new GitException($"The selected file path is outside the repository: {path}.");
            }

            string root = Path.GetFullPath(repository.RootPath);
            string fullPath = Path.GetFullPath(Path.Combine(root, path));
            string relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == ".")
            {
                throw new GitException("The repository root cannot be discarded.");
            }

            if (Path.IsPathRooted(relativePath) == true)
            {
                throw new GitException($"The selected file path is outside the repository: {path}.");
            }

            if (relativePath == "..")
            {
                throw new GitException($"The selected file path is outside the repository: {path}.");
            }

            if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) == true)
            {
                throw new GitException($"The selected file path is outside the repository: {path}.");
            }

            string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = root;
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index].Equals(".git", StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new GitException($"Git metadata cannot be discarded: {path}.");
                }

                if (index == segments.Length - 1)
                {
                    break;
                }

                parent = Path.Combine(parent, segments[index]);
                if (File.Exists(parent) == true)
                {
                    throw new GitException($"The selected file path has a file parent: {path}.");
                }

                if (Directory.Exists(parent) == false)
                {
                    continue;
                }

                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitException($"The selected file path crosses a symbolic link: {path}.");
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
                return new GitFilePreview(string.Empty, $"New file is {info.Length} bytes. Preview is limited to 1 MiB.", false, info.Length);
            }

            byte[] content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            if (HasBinaryContent(content) == true)
            {
                return new GitFilePreview(string.Empty, $"New binary file ({content.Length} bytes).", true, content.Length);
            }

            try
            {
                string text = new UTF8Encoding(false, true).GetString(content);
                if (text.Length > 0 && text[0] == '\uFEFF')
                {
                    text = text.Substring(1);
                }

                return new GitFilePreview(text, $"New text file ({content.Length} bytes).", false, content.Length);
            }
            catch (DecoderFallbackException)
            {
                return new GitFilePreview(string.Empty, $"New binary or non-UTF-8 file ({content.Length} bytes).", true, content.Length);
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
