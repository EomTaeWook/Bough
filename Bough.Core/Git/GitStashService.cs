using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.Core.Git
{
    public class GitStashService
    {
        private static readonly string[] _listArguments = new string[] { "stash", "list", "--format=%gd%x00%H%x00%gs%x00%aI" };
        private readonly GitCommandRunner _runner;
        private readonly GitWorkingTreeService _workingTreeService;

        public GitStashService(GitCommandRunner runner, GitWorkingTreeService workingTreeService)
        {
            _runner = runner;
            _workingTreeService = workingTreeService;
        }

        public async Task<IReadOnlyList<GitStashEntry>> GetStashesAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _listArguments, false, cancellationToken);
            ArrayQueue<GitStashEntry> entries = [];
            string[] lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] fields = line.TrimEnd('\r').Split('\0');
                if (fields.Length != 4)
                {
                    throw new GitException("Git returned an unexpected stash-list format.");
                }

                if (DateTimeOffset.TryParse(fields[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset createdAt) == false)
                {
                    throw new GitException($"Git returned an invalid stash date for {fields[0]}.");
                }

                string branch = ParseBranch(fields[2]);
                entries.Add(new GitStashEntry(fields[0], fields[1], fields[2], branch, createdAt));
            }

            return Array.AsReadOnly(entries.ToArray());
        }

        public async Task<GitStashPreview> GetPreviewAsync(GitRepository repository, GitStashEntry entry, CancellationToken cancellationToken = default)
        {
            await VerifySelectedAsync(repository, entry, cancellationToken);
            GitCommandResult filesResult = await _runner.RunAsync(repository.RootPath, new string[] { "stash", "show", "--include-untracked", "--name-only", "-z", entry.Name }, false, cancellationToken);
            GitCommandResult diffResult = await _runner.RunAsync(repository.RootPath, new string[] { "stash", "show", "--include-untracked", "--patch", "--no-ext-diff", "--no-color", entry.Name }, false, cancellationToken);
            string[] parts = filesResult.Output.Split('\0');
            if (parts[parts.Length - 1].Length != 0)
            {
                throw new GitException($"Git returned a malformed file list for {entry.Name}.");
            }

            return new GitStashPreview(parts.Where(path => path.Length > 0), diffResult.Output);
        }

        public async Task<GitStashEntry> SaveAsync(GitRepository repository, string message, bool includeUntracked, CancellationToken cancellationToken = default)
        {
            GitStashSaveResult result = await SaveWithEntriesAsync(repository, message, includeUntracked, cancellationToken);
            return result.Created;
        }

        public async Task<GitStashSaveResult> SaveWithEntriesAsync(GitRepository repository, string message, bool includeUntracked, CancellationToken cancellationToken = default)
        {
            GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, cancellationToken);
            if (status.Files.Any(file => file.IsConflict) == true)
            {
                throw new GitException("Resolve conflicts before creating a stash.");
            }

            bool hasTrackedChanges = status.Files.Any(file => file.IsUntracked == false);
            bool hasUntrackedChanges = status.Files.Any(file => file.IsUntracked);
            if (hasTrackedChanges == false && (includeUntracked == false || hasUntrackedChanges == false))
            {
                throw new GitException("There are no selected changes to stash.");
            }

            IReadOnlyList<GitStashEntry> before = await GetStashesAsync(repository, cancellationToken);
            List<string> arguments = ["stash", "push"];
            if (includeUntracked == true)
            {
                arguments.Add("--include-untracked");
            }

            if (string.IsNullOrWhiteSpace(message) == false)
            {
                arguments.Add("-m");
                arguments.Add(message.Trim());
            }

            try
            {
                await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);
                IReadOnlyList<GitStashEntry> after = await GetStashesAsync(repository, cancellationToken);
                if (after.Count == 0)
                {
                    throw new GitException("Git did not create a new stash. Refresh the working tree and try again.");
                }
                if (before.Count > 0 && after[0].CommitHash == before[0].CommitHash)
                {
                    throw new GitException("Git did not create a new stash. Refresh the working tree and try again.");
                }

                return new GitStashSaveResult(after[0], after);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new GitStashMutationException(exception.Message, true, true, exception);
            }
        }

        public async Task ApplyAsync(GitRepository repository, GitStashEntry entry, CancellationToken cancellationToken = default)
        {
            await VerifySelectedAsync(repository, entry, cancellationToken);
            try
            {
                await _runner.RunAsync(repository.RootPath, new string[] { "stash", "apply", entry.Name }, false, cancellationToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new GitStashMutationException(exception.Message, true, false, exception);
            }
        }

        public async Task PopAsync(GitRepository repository, GitStashEntry entry, CancellationToken cancellationToken = default)
        {
            await VerifySelectedAsync(repository, entry, cancellationToken);
            try
            {
                await _runner.RunAsync(repository.RootPath, new string[] { "stash", "pop", entry.Name }, false, cancellationToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new GitStashMutationException(exception.Message, true, true, exception);
            }
        }

        public async Task DropAsync(GitRepository repository, GitStashEntry entry, CancellationToken cancellationToken = default)
        {
            await VerifySelectedAsync(repository, entry, cancellationToken);
            try
            {
                await _runner.RunAsync(repository.RootPath, new string[] { "stash", "drop", entry.Name }, false, cancellationToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new GitStashMutationException(exception.Message, false, true, exception);
            }
        }

        private async Task VerifySelectedAsync(GitRepository repository, GitStashEntry entry, CancellationToken cancellationToken)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (Regex.IsMatch(entry.Name, @"^stash@\{[0-9]+\}$", RegexOptions.CultureInvariant) == false)
            {
                throw new GitException($"Invalid stash reference: {entry.Name}.");
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "rev-parse", "--verify", "--quiet", $"{entry.Name}^{{commit}}" }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException($"{entry.Name} changed or was removed. Refresh the stash list before continuing.");
            }
            if (string.Equals(result.Output.Trim(), entry.CommitHash, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new GitException($"{entry.Name} changed or was removed. Refresh the stash list before continuing.");
            }
        }

        private static string ParseBranch(string message)
        {
            string prefix = "On ";
            if (message.StartsWith(prefix, StringComparison.Ordinal) == false)
            {
                prefix = "WIP on ";
            }

            if (message.StartsWith(prefix, StringComparison.Ordinal) == false)
            {
                return string.Empty;
            }

            int separator = message.IndexOf(':', prefix.Length);
            if (separator < 0)
            {
                return string.Empty;
            }

            return message.Substring(prefix.Length, separator - prefix.Length);
        }
    }
}
