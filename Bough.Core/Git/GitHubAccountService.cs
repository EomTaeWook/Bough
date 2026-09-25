using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class GitHubAccountService
    {
        private static readonly string[] _listAccountsArguments = new string[] { "credential-manager", "github", "list", "--no-ui" };
        private static readonly string[] _remoteArguments = new string[] { "remote" };
        private static readonly string[] _gcmVersionArguments = new string[] { "credential-manager", "--version" };
        private readonly GitCommandRunner _runner;

        public GitHubAccountService(GitCommandRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public async Task<GitHubAccountSnapshot> GetSnapshotAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);

            bool gcmAvailable = await IsGcmAvailableAsync(repository, cancellationToken);
            string availabilityMessageCode = string.Empty;
            string accountListErrorCode = string.Empty;
            IReadOnlyList<string> knownAccounts = Array.Empty<string>();
            if (gcmAvailable == false)
            {
                availabilityMessageCode = "GitHubGcmUnavailable";
            }
            else
            {
                GitCommandResult list = await _runner.RunAsync(repository.RootPath, _listAccountsArguments, true, cancellationToken);
                if (list.ExitCode == 0)
                {
                    knownAccounts = ParseAccounts(list.Output);
                }
                else
                {
                    accountListErrorCode = "GitHubAccountListUnavailable";
                }
            }

            GitCommandResult names = await _runner.RunAsync(repository.RootPath, _remoteArguments, false, cancellationToken);
            List<GitHubRemoteAccount> remotes = [];
            foreach (string remoteName in SplitLines(names.Output))
            {
                remotes.Add(await GetRemoteAsync(repository, remoteName, gcmAvailable, cancellationToken));
            }

            return new GitHubAccountSnapshot(gcmAvailable, availabilityMessageCode, knownAccounts, accountListErrorCode, remotes);
        }

        public async Task LoginAsync(GitRepository repository, string userName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            string requestedName = userName?.Trim() ?? string.Empty;
            if (requestedName.Length > 0)
            {
                ValidateUserName(requestedName);
            }

            if (await IsGcmAvailableAsync(repository, cancellationToken) == false)
            {
                throw new GitException("GitHubGcmUnavailable", null, Array.Empty<object>());
            }

            List<string> arguments = ["credential-manager", "github", "login", "--browser"];
            if (requestedName.Length > 0)
            {
                arguments.Add("--username");
                arguments.Add(requestedName);
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, arguments, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitException("GitHubLoginFailed", null, Array.Empty<object>());
            }
        }

        public async Task SaveSelectedAccountAsync(GitRepository repository, string remoteName, string userName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (string.IsNullOrWhiteSpace(remoteName))
            {
                throw new GitException("GitHubRemoteNameRequired", null, Array.Empty<object>());
            }

            string selectedName = userName?.Trim() ?? string.Empty;
            ValidateUserName(selectedName);
            if (await IsGcmAvailableAsync(repository, cancellationToken) == false)
            {
                throw new GitException("GitHubGcmUnavailable", null, Array.Empty<object>());
            }

            GitCommandResult names = await _runner.RunAsync(repository.RootPath, _remoteArguments, false, cancellationToken);
            if (SplitLines(names.Output).Contains(remoteName, StringComparer.Ordinal) == false)
            {
                throw new GitException("GitHubRemoteNotFound", null, remoteName);
            }

            GitHubRemoteAccount remote = await GetRemoteAsync(repository, remoteName, true, cancellationToken);
            if (remote.CanSelect == false)
            {
                throw new GitException(remote.UnavailableReasonCode, null, Array.Empty<object>());
            }

            string key = GetCredentialKey(remote.FetchUrl);
            await _runner.RunAsync(repository.RootPath, new string[] { "config", "--local", "--replace-all", key, selectedName }, false, cancellationToken);
        }

        private async Task<GitHubRemoteAccount> GetRemoteAsync(GitRepository repository, string remoteName, bool gcmAvailable, CancellationToken cancellationToken)
        {
            GitCommandResult fetchResult = await _runner.RunAsync(repository.RootPath, new string[] { "remote", "get-url", "--all", remoteName }, true, cancellationToken);
            GitCommandResult pushResult = await _runner.RunAsync(repository.RootPath, new string[] { "remote", "get-url", "--push", "--all", remoteName }, true, cancellationToken);
            string[] fetchUrls = SplitLines(fetchResult.Output);
            string[] pushUrls = SplitLines(pushResult.Output);
            string fetchUrl = string.Empty;
            string pushUrl = string.Empty;
            if (fetchUrls.Length > 0)
            {
                fetchUrl = SafeDisplayUrl(fetchUrls[0]);
            }
            if (pushUrls.Length > 0)
            {
                pushUrl = SafeDisplayUrl(pushUrls[0]);
            }

            string reasonCode = string.Empty;
            if (fetchResult.ExitCode != 0)
            {
                reasonCode = "GitHubRemoteUrlUnreadable";
            }
            else if (pushResult.ExitCode != 0)
            {
                reasonCode = "GitHubRemoteUrlUnreadable";
            }
            else if (fetchUrls.Length != 1)
            {
                reasonCode = "GitHubRemoteMultipleUrls";
            }
            else if (pushUrls.Length != 1)
            {
                reasonCode = "GitHubRemoteMultipleUrls";
            }
            else if (fetchUrls[0] != pushUrls[0])
            {
                reasonCode = "GitHubRemoteFetchPushMismatch";
            }
            else if (TryGetGitHubUrl(fetchUrls[0], out reasonCode) == true)
            {
                if (gcmAvailable == false)
                {
                    reasonCode = "GitHubGcmUnavailable";
                }
                else if (await HasGcmHelperAsync(repository, fetchUrls[0], cancellationToken) == false)
                {
                    reasonCode = "GitHubRemoteGcmHelperRequired";
                }
            }

            string selectedName = string.Empty;
            bool isRepositorySelection = false;
            if (fetchUrls.Length == 1)
            {
                if (TryGetGitHubUrl(fetchUrls[0], out string ignoredReason) == true)
                {
                    string key = GetCredentialKey(fetchUrls[0]);
                    GitCommandResult local = await _runner.RunAsync(repository.RootPath, new string[] { "config", "--local", "--get", key }, true, cancellationToken);
                    if (local.ExitCode == 0)
                    {
                        selectedName = SafeUserName(local.Output);
                        isRepositorySelection = selectedName.Length > 0;
                    }
                    else if (local.ExitCode == 1)
                    {
                        GitCommandResult inherited = await _runner.RunAsync(repository.RootPath, new string[] { "config", "--get-urlmatch", "credential.username", fetchUrls[0] }, true, cancellationToken);
                        if (inherited.ExitCode == 0)
                        {
                            selectedName = SafeUserName(inherited.Output);
                        }
                        else if (inherited.ExitCode != 1)
                        {
                            throw new GitException("GitHubRemoteDefaultAccountUnreadable", null, remoteName);
                        }
                    }
                    else
                    {
                        throw new GitException("GitHubRemoteLocalAccountUnreadable", null, remoteName);
                    }
                }
            }

            return new GitHubRemoteAccount(remoteName, fetchUrl, pushUrl, selectedName, isRepositorySelection, reasonCode.Length == 0, reasonCode);
        }

        private async Task<bool> IsGcmAvailableAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _gcmVersionArguments, true, cancellationToken);
            return result.ExitCode == 0;
        }

        private async Task<bool> HasGcmHelperAsync(GitRepository repository, string url, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "config", "--get-urlmatch", "credential.helper", url }, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                return false;
            }

            string[] helpers = SplitLines(result.Output);
            if (helpers.Length == 0)
            {
                return false;
            }

            foreach (string helper in helpers)
            {
                if (helper != "manager" && helper != "manager-core")
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetGitHubUrl(string url, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri address) == false)
            {
                reasonCode = "GitHubRemoteHttpsRequired";
                return false;
            }
            if (address.Scheme != Uri.UriSchemeHttps)
            {
                reasonCode = "GitHubRemoteHttpsRequired";
                return false;
            }
            if (address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) == false)
            {
                reasonCode = "GitHubRemoteHostUnsupported";
                return false;
            }
            if (address.Port != 443)
            {
                reasonCode = "GitHubRemotePortUnsupported";
                return false;
            }
            if (address.UserInfo.Length > 0)
            {
                reasonCode = "GitHubRemoteUserInfoUnsupported";
                return false;
            }
            if (address.Query.Length > 0)
            {
                reasonCode = "GitHubRemoteQueryUnsupported";
                return false;
            }
            if (address.Fragment.Length > 0)
            {
                reasonCode = "GitHubRemoteQueryUnsupported";
                return false;
            }
            if (address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2)
            {
                reasonCode = "GitHubRemotePathInvalid";
                return false;
            }

            return true;
        }

        private static string SafeDisplayUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri address) == false)
            {
                return string.Empty;
            }
            if (address.UserInfo.Length > 0)
            {
                return $"{address.Scheme}://{address.Host}{address.AbsolutePath}";
            }
            if (address.Query.Length > 0)
            {
                return $"{address.Scheme}://{address.Host}{address.AbsolutePath}";
            }
            if (address.Fragment.Length > 0)
            {
                return $"{address.Scheme}://{address.Host}{address.AbsolutePath}";
            }

            return url;
        }

        private static string GetCredentialKey(string url)
        {
            return $"credential.{url}.username";
        }

        private static IReadOnlyList<string> ParseAccounts(string output)
        {
            HashSet<string> accounts = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in SplitLines(output))
            {
                string userName = SafeUserName(line);
                if (userName.Length > 0)
                {
                    accounts.Add(userName);
                }
            }

            return accounts.OrderBy(account => account, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string SafeUserName(string value)
        {
            string userName = value.Trim();
            if (IsValidUserName(userName) == false)
            {
                return string.Empty;
            }

            return userName;
        }

        private static void ValidateUserName(string userName)
        {
            if (IsValidUserName(userName) == false)
            {
                throw new GitException("GitHubUserNameInvalid", null, Array.Empty<object>());
            }
        }

        private static bool IsValidUserName(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return false;
            }
            if (userName.Length > 39)
            {
                return false;
            }
            if (userName[0] == '-')
            {
                return false;
            }
            if (userName[^1] == '-')
            {
                return false;
            }
            foreach (char character in userName)
            {
                if (character >= 'a' && character <= 'z')
                {
                    continue;
                }
                if (character >= 'A' && character <= 'Z')
                {
                    continue;
                }
                if (character >= '0' && character <= '9')
                {
                    continue;
                }
                if (character == '-')
                {
                    continue;
                }
                return false;
            }

            return true;
        }

        private static string[] SplitLines(string output)
        {
            return output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
