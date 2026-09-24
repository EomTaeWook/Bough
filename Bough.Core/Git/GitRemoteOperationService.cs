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
    public enum GitPullStrategy
    {
        FastForwardOnly,
        Merge,
        Rebase
    }

    public enum GitPullStage
    {
        Fetching,
        Inspecting,
        Applying
    }

    public class GitPullProgress
    {
        public GitPullProgress(GitPullStage stage, string transferStatus, string incomingSummary)
        {
            Stage = stage;
            TransferStatus = transferStatus;
            IncomingSummary = incomingSummary;
        }

        public GitPullStage Stage { get; }
        public string TransferStatus { get; }
        public string IncomingSummary { get; }
    }

    public class GitRemoteState
    {
        public GitRemoteState(string repositoryRoot, string branchName, string headHash, string upstreamName, string upstreamRemote, string upstreamBranch, int ahead, int behind, IReadOnlyList<string> remotes)
        {
            RepositoryRoot = repositoryRoot;
            BranchName = branchName;
            HeadHash = headHash;
            UpstreamName = upstreamName;
            UpstreamRemote = upstreamRemote;
            UpstreamBranch = upstreamBranch;
            Ahead = ahead;
            Behind = behind;
            Remotes = remotes;
        }

        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string HeadHash { get; }
        public string UpstreamName { get; }
        public string UpstreamRemote { get; }
        public string UpstreamBranch { get; }
        public int Ahead { get; }
        public int Behind { get; }
        public IReadOnlyList<string> Remotes { get; }
        public bool IsDetached { get { return BranchName.Length == 0; } }
        public bool HasUpstream { get { return UpstreamRemote.Length > 0 && UpstreamBranch.Length > 0; } }
    }

    public class GitFetchResult
    {
        public GitFetchResult(IReadOnlyList<string> updatedReferences, IReadOnlyList<string> succeededRemotes, IReadOnlyList<string> failedRemotes)
        {
            UpdatedReferences = updatedReferences;
            SucceededRemotes = succeededRemotes;
            FailedRemotes = failedRemotes;
        }

        public IReadOnlyList<string> UpdatedReferences { get; }
        public IReadOnlyList<string> SucceededRemotes { get; }
        public IReadOnlyList<string> FailedRemotes { get; }
    }

    public class GitRemoteOperationService
    {
        private readonly GitCommandRunner _runner;
        private static readonly string[] _remoteArguments = new string[] { "remote" };
        private static readonly string[] _currentBranchArguments = new string[] { "symbolic-ref", "--quiet", "--short", "HEAD" };
        private static readonly string[] _verifyHeadArguments = new string[] { "rev-parse", "--verify", "HEAD" };
        private static readonly string[] _fetchHeadArguments = new string[] { "rev-parse", "--verify", "FETCH_HEAD^{commit}" };
        private static readonly string[] _remoteReferencesArguments = new string[] { "for-each-ref", "--format=%(refname:short)%00%(objectname)", "refs/remotes", "refs/tags" };

        public GitRemoteOperationService(GitCommandRunner runner)
        {
            _runner = runner;
        }

        public async Task<GitRemoteState> GetStateAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitCommandResult remoteResult = await _runner.RunAsync(repository.RootPath, _remoteArguments, false, cancellationToken);
            string[] remotes = remoteResult.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            GitCommandResult branchResult = await _runner.RunAsync(repository.RootPath, _currentBranchArguments, true, cancellationToken);
            string branch = string.Empty;
            if (branchResult.ExitCode == 0)
            {
                branch = branchResult.Output.Trim();
            }

            string head = string.Empty;
            string upstreamName = string.Empty;
            string upstreamRemote = string.Empty;
            string upstreamBranch = string.Empty;
            int ahead = 0;
            int behind = 0;
            if (branch.Length > 0)
            {
                GitCommandResult trackingResult = await _runner.RunAsync(repository.RootPath,
                    new string[] { "for-each-ref", "--format=%(refname)%00%(objectname)%00%(upstream:short)%00%(upstream:remotename)%00%(upstream:remoteref)%00%(upstream:track,nobracket)", $"refs/heads/{branch}" }, false, cancellationToken);
                foreach (string row in trackingResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = row.TrimEnd('\r').Split('\0');
                    if (fields.Length != 6)
                    {
                        throw new GitException("Git 원격 상태 출력 형식이 올바르지 않습니다.");
                    }
                    if (fields[0] != $"refs/heads/{branch}")
                    {
                        continue;
                    }

                    head = fields[1];
                    if (fields[5].Trim('[', ']') == "gone")
                    {
                        break;
                    }

                    upstreamName = fields[2];
                    string remote = fields[3];
                    string remoteRef = fields[4];
                    if (remotes.Contains(remote, StringComparer.Ordinal))
                    {
                        if (remoteRef.StartsWith("refs/heads/", StringComparison.Ordinal))
                        {
                            upstreamRemote = remote;
                            upstreamBranch = remoteRef["refs/heads/".Length..];
                        }
                    }

                    bool trackingKnown = fields[5].Length == 0;
                    foreach (string count in fields[5].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (count.StartsWith("ahead ", StringComparison.Ordinal))
                        {
                            trackingKnown = int.TryParse(count["ahead ".Length..], out ahead);
                            if (trackingKnown == false)
                            {
                                break;
                            }
                            continue;
                        }
                        if (count.StartsWith("behind ", StringComparison.Ordinal))
                        {
                            trackingKnown = int.TryParse(count["behind ".Length..], out behind);
                            if (trackingKnown == false)
                            {
                                break;
                            }
                            continue;
                        }
                        trackingKnown = false;
                        break;
                    }
                    if (trackingKnown == false)
                    {
                        ahead = -1;
                        behind = -1;
                    }
                    break;
                }
            }
            else
            {
                GitCommandResult headResult = await _runner.RunAsync(repository.RootPath, _verifyHeadArguments, true, cancellationToken);
                if (headResult.ExitCode == 0)
                {
                    head = headResult.Output.Trim();
                }
            }

            return new GitRemoteState(repository.RootPath, branch, head, upstreamName, upstreamRemote, upstreamBranch, ahead, behind, Array.AsReadOnly(remotes));
        }

        public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(GitRepository repository, string remoteName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitRemoteState state = await GetStateAsync(repository, cancellationToken);
            return await GetRemoteBranchesAsync(repository, state, remoteName, cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(GitRepository repository, GitRemoteState state, string remoteName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(state);
            if (state.RepositoryRoot != repository.RootPath)
            {
                throw new GitException("원격 작업 대상 저장소가 변경되었습니다.");
            }
            string remote = ResolveRemote(state, remoteName);
            string prefix = $"refs/remotes/{remote}/";
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "for-each-ref", "--format=%(refname)%00%(symref)", prefix }, false, cancellationToken);
            List<string> branches = [];
            foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.TrimEnd('\r').Split('\0');
                if (fields.Length != 2)
                {
                    continue;
                }
                if (fields[1].Length > 0)
                {
                    continue;
                }
                if (fields[0].StartsWith(prefix, StringComparison.Ordinal) == false)
                {
                    continue;
                }
                branches.Add(fields[0].Substring(prefix.Length));
            }
            branches.Sort(StringComparer.Ordinal);
            return branches.AsReadOnly();
        }

        public async Task ValidatePushTargetAsync(GitRepository repository, string remoteName, string branchName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            GitRemoteState state = await GetStateAsync(repository, cancellationToken);
            await ValidatePushTargetAsync(repository, state, remoteName, branchName, cancellationToken);
        }

        public async Task ValidatePushTargetAsync(GitRepository repository, GitRemoteState state, string remoteName, string branchName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(state);
            if (state.RepositoryRoot != repository.RootPath)
            {
                throw new GitException("원격 작업 대상 저장소가 변경되었습니다.");
            }
            ResolveRemote(state, remoteName);
            if (string.IsNullOrWhiteSpace(branchName) == true)
            {
                throw new GitException("대상 브랜치 이름을 입력하세요.");
            }
            await VerifyBranchNameAsync(repository, branchName.Trim(), cancellationToken);
        }

        public async Task<GitFetchResult> FetchAsync(GitRepository repository, GitRemoteState expected, string remoteName, bool fetchAll, bool prune, CancellationToken cancellationToken = default)
        {
            GitRemoteState current = await VerifyStateAsync(repository, expected, cancellationToken);
            ArrayQueue<string> targets = [];
            if (fetchAll == true)
            {
                foreach (string remote in current.Remotes) { targets.Add(remote); }
            }
            else
            {
                string target = ResolveRemote(current, remoteName);
                targets.Add(target);
            }
            if (targets.Count == 0)
            {
                throw new GitException("설정된 원격이 없습니다.");
            }

            Dictionary<string, string> before = await ReadReferencesAsync(repository, cancellationToken);
            ArrayQueue<string> succeeded = [];
            ArrayQueue<string> failed = [];
            foreach (string target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] arguments = new string[] { "fetch", target };
                if (prune == true)
                {
                    arguments = new string[] { "fetch", "--prune", target };
                }
                GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
                if (result.ExitCode == 0)
                {
                    succeeded.Add(target);
                }
                else
                {
                    failed.Add($"{target}: {await SanitizeErrorAsync(repository, target, result, cancellationToken)}");
                }
            }

            Dictionary<string, string> after = await ReadReferencesAsync(repository, cancellationToken);
            ArrayQueue<string> updated = [];
            foreach (KeyValuePair<string, string> reference in after)
            {
                if (before.TryGetValue(reference.Key, out string oldHash) == false || oldHash != reference.Value)
                {
                    updated.Add(reference.Key);
                }
            }
            foreach (string name in before.Keys)
            {
                if (after.ContainsKey(name) == false)
                {
                    updated.Add($"{name} (removed)");
                }
            }
            return new GitFetchResult(updated.ToArray(), succeeded.ToArray(), failed.ToArray());
        }

        public async Task PullAsync(GitRepository repository, GitRemoteState expected, string remoteName, string branchName, GitPullStrategy strategy, CancellationToken cancellationToken = default)
        {
            await PullWithProgressAsync(repository, expected, remoteName, branchName, strategy, null, cancellationToken);
        }

        public async Task<string> PullWithProgressAsync(GitRepository repository, GitRemoteState expected, string remoteName, string branchName, GitPullStrategy strategy, IProgress<GitPullProgress> progress, CancellationToken cancellationToken = default)
        {
            GitRemoteState current = await VerifyStateAsync(repository, expected, cancellationToken);
            if (current.IsDetached == true)
            {
                throw new GitException("Detached HEAD에서는 Pull할 수 없습니다.");
            }
            if (Enum.IsDefined(strategy) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(strategy));
            }

            string remote = ResolveRemote(current, remoteName);
            string branch = ResolveBranch(current, remote, branchName);
            await VerifyBranchNameAsync(repository, branch, cancellationToken);
            progress?.Report(new GitPullProgress(GitPullStage.Fetching, string.Empty, null));
            IProgress<string> transferProgress = new Progress<string>(line =>
            {
                string safeStatus = FormatTransferProgress(line);
                if (safeStatus.Length > 0)
                {
                    progress?.Report(new GitPullProgress(GitPullStage.Fetching, safeStatus, null));
                }
            });
            GitCommandResult fetch = await _runner.RunWithProgressAsync(repository.RootPath, new string[] { "fetch", "--progress", remote, branch }, transferProgress, true, cancellationToken);
            if (fetch.ExitCode != 0)
            {
                throw new GitException($"Pull {remote}/{branch} 가져오기 실패: {await SanitizeErrorAsync(repository, remote, fetch, cancellationToken)}");
            }

            progress?.Report(new GitPullProgress(GitPullStage.Inspecting, string.Empty, null));
            GitCommandResult incoming = await _runner.RunAsync(repository.RootPath, _fetchHeadArguments, false, cancellationToken);
            string incomingHash = incoming.Output.Trim();
            string summary = await ReadIncomingSummaryAsync(repository, current.HeadHash, incomingHash, cancellationToken);
            progress?.Report(new GitPullProgress(GitPullStage.Inspecting, string.Empty, summary));
            await VerifyStateAsync(repository, current, cancellationToken);

            string[] arguments = new string[] { "merge", "--ff-only", incomingHash };
            if (strategy == GitPullStrategy.Merge)
            {
                arguments = new string[] { "merge", "--no-edit", incomingHash };
            }
            if (strategy == GitPullStrategy.Rebase)
            {
                arguments = new string[] { "rebase", incomingHash };
            }
            progress?.Report(new GitPullProgress(GitPullStage.Applying, string.Empty, summary));
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException($"Pull {remote}/{branch} 실패: {await SanitizeErrorAsync(repository, remote, result, cancellationToken)}");
            }
            return summary;
        }

        private static string FormatTransferProgress(string line)
        {
            string value = line.Trim();
            if (value.StartsWith("remote: ", StringComparison.Ordinal) == true)
            {
                value = value.Substring(8);
            }
            Match match = Regex.Match(value, @"^(?<kind>Enumerating objects|Counting objects|Compressing objects|Receiving objects|Resolving deltas|Writing objects):\s*(?<value>\d{1,3}%\s*\(\d+/\d+\)|\d+)", RegexOptions.CultureInvariant);
            if (match.Success == false)
            {
                return string.Empty;
            }
            return $"Git 객체 전송 · {match.Groups["kind"].Value}: {match.Groups["value"].Value}";
        }

        private async Task<string> ReadIncomingSummaryAsync(GitRepository repository, string localHash, string incomingHash, CancellationToken cancellationToken)
        {
            GitCommandResult countResult = await _runner.RunAsync(repository.RootPath, new string[] { "rev-list", "--count", $"{localHash}..{incomingHash}" }, true, cancellationToken);
            if (countResult.ExitCode != 0)
            {
                return "받는 브랜치의 커밋 요약을 조회하지 못했습니다.";
            }
            if (long.TryParse(countResult.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long count) == false)
            {
                return "받는 브랜치의 커밋 수를 확인하지 못했습니다.";
            }

            StringBuilder summary = new();
            summary.Append($"받는 브랜치의 새 커밋 {count}개");
            if (count > 0)
            {
                GitCommandResult log = await _runner.RunAsync(repository.RootPath, new string[] { "log", "-z", "--max-count=8", "--format=%h%x09%s", $"{localHash}..{incomingHash}" }, true, cancellationToken);
                if (log.ExitCode == 0)
                {
                    foreach (string entry in log.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    {
                        summary.Append('\n');
                        summary.Append(entry.TrimEnd('\r', '\n'));
                    }
                    if (count > 8)
                    {
                        summary.Append($"\n외 {count - 8}개");
                    }
                }
            }

            GitCommandResult baseResult = await _runner.RunAsync(repository.RootPath, new string[] { "merge-base", localHash, incomingHash }, true, cancellationToken);
            if (baseResult.ExitCode != 0)
            {
                summary.Append("\n변경 파일은 공통 조상을 찾지 못해 조회할 수 없습니다.");
                return summary.ToString();
            }
            GitCommandResult files = await _runner.RunAsync(repository.RootPath, new string[] { "diff", "--name-only", "-z", baseResult.Output.Trim(), incomingHash }, true, cancellationToken);
            if (files.ExitCode != 0)
            {
                summary.Append("\n변경 파일을 조회하지 못했습니다.");
                return summary.ToString();
            }
            string[] paths = files.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            summary.Append($"\n변경 파일 {paths.Length}개");
            int shown = Math.Min(paths.Length, 8);
            for (int index = 0; index < shown; index++)
            {
                string path = paths[index].Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
                summary.Append($"\n{path}");
            }
            if (paths.Length > shown)
            {
                summary.Append($"\n외 {paths.Length - shown}개");
            }
            return summary.ToString();
        }

        public async Task<bool> PushAsync(GitRepository repository, GitRemoteState expected, string remoteName, string targetBranch, bool firstPushConfirmed, CancellationToken cancellationToken = default)
        {
            GitRemoteState current = await VerifyStateAsync(repository, expected, cancellationToken);
            if (current.IsDetached == true)
            {
                throw new GitException("Detached HEAD에서는 Push할 수 없습니다.");
            }
            if (current.HeadHash.Length == 0)
            {
                throw new GitException("Push할 커밋이 없습니다.");
            }

            bool firstPush = current.HasUpstream == false;
            string remote = ResolveRemote(current, remoteName);
            string branch = ResolveBranch(current, remote, targetBranch);
            bool destinationChanged = firstPush == true || remote != current.UpstreamRemote || branch != current.UpstreamBranch;
            if (destinationChanged == false && current.Ahead == 0)
            {
                return false;
            }

            await VerifyBranchNameAsync(repository, branch, cancellationToken);
            if (destinationChanged == true && firstPushConfirmed == false)
            {
                throw new GitException($"Push의 원격과 대상 브랜치를 확인해야 합니다: {remote}/{branch}");
            }

            string refspec = $"refs/heads/{current.BranchName}:refs/heads/{branch}";
            string[] arguments = new string[] { "push", remote, refspec };
            if (firstPush == true)
            {
                arguments = new string[] { "push", "--set-upstream", remote, refspec };
            }
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException($"Push {remote}/{branch} 실패: {await SanitizeErrorAsync(repository, remote, result, cancellationToken)}");
            }
            return true;
        }

        private async Task<GitRemoteState> VerifyStateAsync(GitRepository repository, GitRemoteState expected, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(expected);
            if (repository.RootPath != expected.RepositoryRoot)
            {
                throw new GitException("원격 작업 대상 저장소가 변경되었습니다.");
            }
            GitRemoteState current = await GetStateAsync(repository, cancellationToken);
            if (current.BranchName != expected.BranchName)
            {
                throw new GitException($"현재 브랜치가 확인 이후 변경되었습니다: {expected.BranchName} → {current.BranchName}");
            }
            if (current.HeadHash != expected.HeadHash)
            {
                throw new GitException("HEAD가 확인 이후 변경되었습니다. 새로 고친 뒤 다시 시도하세요.");
            }
            if (current.UpstreamName != expected.UpstreamName)
            {
                throw new GitException($"Upstream이 확인 이후 변경되었습니다: {expected.UpstreamName} → {current.UpstreamName}");
            }
            return current;
        }

        private static string ResolveRemote(GitRemoteState state, string selected)
        {
            string remote = selected;
            if (state.HasUpstream == true && string.IsNullOrWhiteSpace(remote) == true)
            {
                remote = state.UpstreamRemote;
            }
            if (string.IsNullOrWhiteSpace(remote) == true)
            {
                throw new GitException("원격을 선택하세요.");
            }
            if (state.Remotes.Contains(remote, StringComparer.Ordinal) == false)
            {
                throw new GitException($"선택한 원격을 찾을 수 없습니다: {remote}");
            }
            return remote;
        }

        private static string ResolveBranch(GitRemoteState state, string remote, string selected)
        {
            if (string.IsNullOrWhiteSpace(selected) == false)
            {
                return selected.Trim();
            }
            if (state.HasUpstream == true && state.UpstreamRemote == remote)
            {
                return state.UpstreamBranch;
            }
            throw new GitException("대상 원격 브랜치를 선택하세요.");
        }

        private async Task VerifyBranchNameAsync(GitRepository repository, string name, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "check-ref-format", "--branch", name }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException($"브랜치 이름이 올바르지 않습니다: {name}");
            }
        }

        private async Task<Dictionary<string, string>> ReadReferencesAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _remoteReferencesArguments, false, cancellationToken);
            Dictionary<string, string> references = new(StringComparer.Ordinal);
            foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.TrimEnd('\r').Split('\0');
                if (fields.Length == 2)
                {
                    references[fields[0]] = fields[1];
                }
            }
            return references;
        }

        private async Task<string> SanitizeErrorAsync(GitRepository repository, string remote, GitCommandResult result, CancellationToken cancellationToken)
        {
            string message = result.Error.Trim();
            GitCommandResult urlResult = await _runner.RunAsync(repository.RootPath, new string[] { "remote", "get-url", remote }, true, cancellationToken);
            if (urlResult.ExitCode == 0)
            {
                string url = urlResult.Output.Trim();
                if (url.Length > 0)
                {
                    message = message.Replace(url, GitReferenceService.SanitizeUrl(url), StringComparison.Ordinal);
                }
            }
            message = Regex.Replace(message, @"(?i)(https?://)[^/@\s]+@", "$1***@");
            message = Regex.Replace(message, @"(?i)([?&](token|access_token|password|passwd|secret|key)=)[^&\s]+", "$1***");
            if (message.Length > 1000)
            {
                message = message.Substring(0, 1000);
            }
            if (message.Length == 0)
            {
                message = $"Git exit code {result.ExitCode}";
            }
            return message;
        }
    }
}
