using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.Core.Git
{
    public class GitHistoryService
    {
        private static readonly string[] _remoteUrlsArguments = new string[] { "config", "--local", "--null", "--get-regexp", "^remote\\..*\\.url$" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "--quiet", "HEAD" };
        private readonly GitCommandRunner _runner;

        public GitHistoryService(GitCommandRunner runner)
        {
            _runner = runner;
        }

        public async Task<IReadOnlyList<string>> GetRemoteUrlsAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitCommandResult result = await _runner.RunAsync(repository.RootPath,
                _remoteUrlsArguments, true, cancellationToken);
            if (result.ExitCode == 1)
            {
                return Array.Empty<string>();
            }
            if (result.ExitCode != 0)
            {
                throw new GitException("HistoryRemoteUrlsUnreadable", null, result.Error.Trim());
            }

            ArrayQueue<string> origin = [];
            ArrayQueue<string> others = [];
            foreach (string record in result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = record.IndexOf('\n');
                if (separator < 0)
                {
                    continue;
                }
                string key = record[..separator];
                string url = record[(separator + 1)..];
                if (key == "remote.origin.url")
                {
                    origin.Add(url);
                }
                else
                {
                    others.Add(url);
                }
            }
            return origin.Concat(others).ToArray();
        }

        public Task<GitHistoryPage> GetHistoryAsync(GitRepository repository, int limit, GitHistoryScope scope, CancellationToken cancellationToken = default)
        {
            return GetHistoryPageAsync(repository, 0, limit, scope, cancellationToken);
        }

        public async Task<GitHistoryPage> GetHistoryPageAsync(GitRepository repository, int skip, int limit, GitHistoryScope scope, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (skip < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip));
            }
            if (limit < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }

            if (scope == GitHistoryScope.CurrentBranch && skip == 0)
            {
                GitCommandResult head = await _runner.RunAsync(repository.RootPath,
                    _verifyHeadArguments, true, cancellationToken);
                if (head.ExitCode != 0)
                {
                    return new GitHistoryPage(Array.Empty<GitHistoryCommit>(), false);
                }
            }

            string format = "%H%x00%P%x00%B%x00%an%x00%ae%x00%aI%x00%D";
            ArrayQueue<string> arguments = [];
            arguments.Add("log");
            if (scope == GitHistoryScope.All)
            {
                arguments.Add("--all");
            }
            else
            {
                arguments.Add("HEAD");
            }
            arguments.Add("-z");
            arguments.Add("--topo-order");
            arguments.Add("--date-order");
            arguments.Add("--decorate=full");
            arguments.Add($"--skip={skip}");
            arguments.Add($"--max-count={limit + 1}");
            arguments.Add($"--format={format}");
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, false, cancellationToken);

            ArrayQueue<GitHistoryCommit> commits = [];
            string[] fields = result.Output.Split('\0');
            if (fields[^1].Length != 0 || (fields.Length - 1) % 7 != 0)
            {
                throw new GitException("HistoryLogFormatInvalid", null, Array.Empty<object>());
            }

            for (int index = 0; index < fields.Length - 1; index += 7)
            {
                if (DateTimeOffset.TryParse(fields[index + 5], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset authoredAt) == false)
                {
                    throw new GitException("HistoryCommitDateInvalid", null, fields[index]);
                }

                string[] parents = fields[index + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string[] references = fields[index + 6].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                string firstLine = GitCommitMessageService.GetFirstLine(fields[index + 2]);
                commits.Add(new GitHistoryCommit(fields[index], parents, firstLine, fields[index + 3], fields[index + 4], authoredAt, references));
            }

            bool hasMore = commits.Count > limit;
            return new GitHistoryPage(commits.Take(limit).ToArray(), hasMore);
        }

        public async Task<GitCommitDetails> GetDetailsAsync(GitRepository repository, GitHistoryCommit commit, CancellationToken cancellationToken = default)
        {
            string hash = commit.Hash;
            if (hash.Length != 40 && hash.Length != 64)
            {
                throw new GitException("HistoryCommitHashRequired", null, Array.Empty<object>());
            }

            GitCommandResult bodyResult = await _runner.RunAsync(repository.RootPath,
                new string[] { "show", "-s", "--format=%b", hash }, false, cancellationToken);
            string[] fileArguments;
            if (commit.Parents.Count == 0)
            {
                fileArguments = new string[] { "diff-tree", "--root", "--no-commit-id", "--name-status", "--no-renames", "-r", "-z", hash };
            }
            else
            {
                fileArguments = new string[] { "diff", "--name-status", "--no-renames", "-z", commit.Parents[0], hash };
            }

            GitCommandResult filesResult = await _runner.RunAsync(repository.RootPath, fileArguments, false, cancellationToken);

            ArrayQueue<GitChangedFile> files = [];
            string[] parts = filesResult.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length % 2 != 0)
            {
                throw new GitException("HistoryChangedFileFormatInvalid", null, Array.Empty<object>());
            }

            for (int index = 0; index < parts.Length; index += 2)
            {
                files.Add(new GitChangedFile(parts[index], parts[index + 1]));
            }

            return new GitCommitDetails(bodyResult.Output.Trim(), files.ToArray());
        }
    }
}
