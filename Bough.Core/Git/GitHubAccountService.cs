using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private static readonly string[] _remoteUrlSettingsArguments = new string[] { "config", "--includes", "--null", "--show-scope", "--show-origin", "--get-regexp", "^(remote[.].*[.](url|pushurl|vcs)|url[.].*[.](insteadof|pushinsteadof)|include(if[.].*)?[.]path)$" };
        private static readonly string[] _commonDirectoryArguments = new string[] { "rev-parse", "--path-format=absolute", "--git-common-dir" };
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
            IReadOnlyList<(string Key, string Value, string Scope, string Origin)> urlSettings = await ReadRemoteUrlSettingsAsync(repository, cancellationToken);
            string sharedConfigPath = await ReadSharedConfigPathAsync(repository, urlSettings, cancellationToken);
            List<GitHubRemoteAccount> remotes = [];
            foreach (string remoteName in SplitLines(names.Output))
            {
                remotes.Add(await GetRemoteAsync(repository, remoteName, gcmAvailable, cancellationToken, urlSettings, sharedConfigPath));
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

        public async Task RemovePushUrlAsync(GitRepository repository, string remoteName, GitRemotePushUrlSettings expected, CancellationToken cancellationToken)
        {
            if (expected?.RemovalFingerprint == null)
            {
                throw new GitException("GitRemotePushUrlRemovalChanged", null, Array.Empty<object>());
            }

            string sharedConfigPath = expected.SharedConfigPath;
            string lockPath = sharedConfigPath + ".lock";
            bool ownsLock = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileStreamOptions lockOptions = new()
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.CreateNew,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                };
                if (OperatingSystem.IsWindows() == false)
                {
                    lockOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                byte[] originalConfig;
                await using (FileStream configLock = new(lockPath, lockOptions))
                {
                    ownsLock = true;
                    if ((File.GetAttributes(sharedConfigPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new GitException("GitRemotePushUrlRemovalChanged", null, Array.Empty<object>());
                    }
                    originalConfig = await File.ReadAllBytesAsync(sharedConfigPath, cancellationToken);
                    (string[] fetchUrls, string[] pushUrls) = await ReadResolvedUrlsAsync(repository, remoteName, cancellationToken);
                    IReadOnlyList<(string Key, string Value, string Scope, string Origin)> urlSettings = await ReadRemoteUrlSettingsAsync(repository, cancellationToken);
                    string actualConfigPath = await ReadSharedConfigPathAsync(repository, urlSettings, cancellationToken);
                    GitRemotePushUrlSettings current = DescribeRemoteUrls(repository, remoteName, fetchUrls, pushUrls, urlSettings, actualConfigPath);
                    if (current.RemovalFingerprint != expected.RemovalFingerprint)
                    {
                        throw new GitException("GitRemotePushUrlRemovalChanged", null, Array.Empty<object>());
                    }
                    await configLock.WriteAsync(originalConfig, cancellationToken);
                }

                // Keep the shared config locked while Git edits its pending replacement.
                GitCommandResult result = await _runner.RunAsync(repository.RootPath, ["config", "--file", lockPath, "--no-includes", "--unset", "--", $"remote.{remoteName}.pushurl"], true, cancellationToken);
                if (result.ExitCode != 0)
                {
                    throw new GitException("GitRemotePushUrlRemovalFailed", null, Array.Empty<object>());
                }
                if ((File.GetAttributes(sharedConfigPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitException("GitRemotePushUrlRemovalChanged", null, Array.Empty<object>());
                }
                byte[] currentConfig = await File.ReadAllBytesAsync(sharedConfigPath, cancellationToken);
                if (originalConfig.AsSpan().SequenceEqual(currentConfig) == false)
                {
                    throw new GitException("GitRemotePushUrlRemovalChanged", null, Array.Empty<object>());
                }
                if (OperatingSystem.IsWindows() == false)
                {
                    File.SetUnixFileMode(lockPath, File.GetUnixFileMode(sharedConfigPath));
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Replace(lockPath, sharedConfigPath, null);
                ownsLock = false;
            }
            finally
            {
                if (ownsLock == true)
                {
                    File.Delete(lockPath);
                }
            }
        }

        private async Task<GitHubRemoteAccount> GetRemoteAsync(GitRepository repository, string remoteName, bool gcmAvailable, CancellationToken cancellationToken, IReadOnlyList<(string Key, string Value, string Scope, string Origin)> urlSettings = null, string sharedConfigPath = null)
        {
            (string[] resolvedFetchUrls, string[] resolvedPushUrls) = await ReadResolvedUrlsAsync(repository, remoteName, cancellationToken);
            GitRemotePushUrlSettings pushUrlSettings = DescribeRemoteUrls(repository, remoteName, resolvedFetchUrls, resolvedPushUrls, urlSettings, sharedConfigPath);
            string[] fetchUrls = resolvedFetchUrls ?? Array.Empty<string>();
            string[] pushUrls = resolvedPushUrls ?? Array.Empty<string>();
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
            if (pushUrlSettings.IsReadable == false)
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
            if (pushUrlSettings.IsReadable == true && fetchUrls.Length == 1)
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
                            reasonCode = "GitHubRemoteDefaultAccountUnreadable";
                        }
                    }
                    else
                    {
                        reasonCode = "GitHubRemoteLocalAccountUnreadable";
                    }
                }
            }

            return new GitHubRemoteAccount(remoteName, fetchUrl, pushUrl, selectedName, isRepositorySelection, reasonCode.Length == 0, reasonCode) { PushUrlSettings = pushUrlSettings };
        }

        private async Task<(string[] Fetch, string[] Push)> ReadResolvedUrlsAsync(GitRepository repository, string remoteName, CancellationToken cancellationToken)
        {
            GitCommandResult fetch = await _runner.RunAsync(repository.RootPath, ["remote", "get-url", "--all", "--", remoteName], true, cancellationToken);
            GitCommandResult push = await _runner.RunAsync(repository.RootPath, ["remote", "get-url", "--push", "--all", "--", remoteName], true, cancellationToken);
            return (ReadResolvedUrlLines(fetch), ReadResolvedUrlLines(push));
        }

        private static string[] ReadResolvedUrlLines(GitCommandResult result)
        {
            if (result.ExitCode != 0)
            {
                return null;
            }
            if (result.Output.Length == 0)
            {
                return Array.Empty<string>();
            }
            if (result.Output.EndsWith('\n') == false)
            {
                return null;
            }
            return result.Output[..^1].Split('\n');
        }

        private async Task<IReadOnlyList<(string Key, string Value, string Scope, string Origin)>> ReadRemoteUrlSettingsAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            // GIT_CONFIG can redirect git config reads without redirecting git remote.
            if (Environment.GetEnvironmentVariable("GIT_CONFIG") != null)
            {
                return null;
            }
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _remoteUrlSettingsArguments, true, cancellationToken);
            List<(string Key, string Value, string Scope, string Origin)> settings = [];
            if (result.Output.Length == 0)
            {
                if (result.ExitCode == 1)
                {
                    return settings;
                }
                return null;
            }
            if (result.ExitCode != 0)
            {
                return null;
            }

            string[] fields = result.Output.Split('\0');
            if ((fields.Length - 1) % 3 != 0)
            {
                return null;
            }
            if (fields[^1].Length != 0)
            {
                return null;
            }
            for (int index = 0; index < fields.Length - 1; index += 3)
            {
                string keyValue = fields[index + 2];
                int separator = keyValue.IndexOf('\n');
                if (separator < 0)
                {
                    settings.Add((keyValue, null, fields[index], fields[index + 1]));
                    continue;
                }
                settings.Add((keyValue[..separator], keyValue[(separator + 1)..], fields[index], fields[index + 1]));
            }
            return settings;
        }

        private async Task<string> ReadSharedConfigPathAsync(GitRepository repository, IReadOnlyList<(string Key, string Value, string Scope, string Origin)> settings, CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                return null;
            }
            if (settings.Any(setting => setting.Key.StartsWith("remote.", StringComparison.Ordinal) && setting.Key.EndsWith(".pushurl", StringComparison.Ordinal)) == false)
            {
                return null;
            }

            GitCommandResult result = await _runner.RunAsync(repository.RootPath, _commonDirectoryArguments, true, cancellationToken);
            if (result.ExitCode != 0)
            {
                return null;
            }
            string directory = result.Output.TrimEnd('\n', '\r');
            if (Path.IsPathFullyQualified(directory) == false)
            {
                return null;
            }
            if (directory.Any(char.IsControl))
            {
                return null;
            }
            string configPath = Path.GetFullPath(Path.Combine(directory, "config"));
            try
            {
                FileAttributes attributes = File.GetAttributes(configPath);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    return null;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            return configPath;
        }

        private static GitRemotePushUrlSettings DescribeRemoteUrls(GitRepository repository, string remoteName, string[] fetchUrls, string[] pushUrls, IReadOnlyList<(string Key, string Value, string Scope, string Origin)> settings, string sharedConfigPath)
        {
            GitRemotePushUrlSettings description = new()
            {
                FetchUrls = MaskResolvedUrls(fetchUrls),
                PushUrls = MaskResolvedUrls(pushUrls),
                PushSourceCode = "GitRemotePushSourceUnknown",
                IsReadable = fetchUrls != null && pushUrls != null
            };
            if (settings == null)
            {
                return description;
            }

            string fetchKey = $"remote.{remoteName}.url";
            string pushKey = $"remote.{remoteName}.pushurl";
            string helperKey = $"remote.{remoteName}.vcs";
            (string Key, string Value, string Scope, string Origin)[] fetchSettings = settings.Where(setting => setting.Key == fetchKey).ToArray();
            (string Key, string Value, string Scope, string Origin)[] pushSettings = settings.Where(setting => setting.Key == pushKey).ToArray();
            description.ExplicitPushUrls = Array.AsReadOnly(pushSettings.Select(setting => (setting.Scope, setting.Origin, MaskUrl(setting.Value))).ToArray());
            (string Key, string Value, string Scope, string Origin)[] rewrites = settings.Where(setting => setting.Key.StartsWith("url.", StringComparison.Ordinal)).ToArray();

            if (description.IsReadable == false)
            {
                return description;
            }
            if (fetchSettings.Concat(pushSettings).Concat(rewrites).Any(setting => HasUnsafeUrlCharacters(setting.Value) || HasUnsafeUrlCharacters(setting.Key)))
            {
                description.FetchUrls = Array.Empty<string>();
                description.PushUrls = Array.Empty<string>();
                description.IsReadable = false;
                return description;
            }

            bool fetchSourceKnown = false;
            if (fetchSettings.Length == 1)
            {
                if (fetchUrls.Length == 1)
                {
                    fetchSourceKnown = TryDescribeRewrite(fetchSettings[0].Value, fetchUrls[0], rewrites, ".insteadof", "insteadOf", out (string Rule, string Scope, string Origin)? fetchRewrite);
                    description.FetchRewrite = fetchRewrite;
                }
            }
            if (pushUrls.Length != 1)
            {
                return description;
            }
            if (pushSettings.Length == 0)
            {
                if (fetchSourceKnown == false)
                {
                    return description;
                }
                string rawFetchUrl = fetchSettings[0].Value;
                if (HasPushRewrite(rewrites, rawFetchUrl))
                {
                    if (TryDescribeRewrite(rawFetchUrl, pushUrls[0], rewrites, ".pushinsteadof", "pushInsteadOf", out (string Rule, string Scope, string Origin)? pushRewrite))
                    {
                        description.PushRewrite = pushRewrite;
                        description.PushSourceCode = null;
                    }
                }
                else if (pushUrls[0] == fetchUrls[0])
                {
                    description.PushSourceCode = "GitRemotePushSourceFetch";
                    description.PushRewrite = description.FetchRewrite;
                }
                return description;
            }
            if (pushSettings.Length != 1)
            {
                return description;
            }
            if (TryDescribeRewrite(pushSettings[0].Value, pushUrls[0], rewrites, ".insteadof", "insteadOf", out (string Rule, string Scope, string Origin)? explicitRewrite) == false)
            {
                return description;
            }
            description.PushSourceCode = "GitRemotePushSourceExplicit";
            description.PushRewrite = explicitRewrite;

            if (sharedConfigPath == null)
            {
                return description;
            }
            if (fetchSourceKnown == false)
            {
                return description;
            }
            if (IsDirectConfigOrigin(repository, sharedConfigPath, pushSettings[0].Scope, pushSettings[0].Origin) == false)
            {
                return description;
            }
            if (IsDirectConfigOrigin(repository, sharedConfigPath, fetchSettings[0].Scope, fetchSettings[0].Origin) == false)
            {
                return description;
            }
            if (description.FetchRewrite is { } fetchSource)
            {
                if (IsDirectConfigOrigin(repository, sharedConfigPath, fetchSource.Scope, fetchSource.Origin) == false)
                {
                    return description;
                }
            }
            if (explicitRewrite is { } pushSource)
            {
                if (IsDirectConfigOrigin(repository, sharedConfigPath, pushSource.Scope, pushSource.Origin) == false)
                {
                    return description;
                }
            }
            if (HasPushRewrite(rewrites, fetchSettings[0].Value))
            {
                return description;
            }
            if (settings.Any(setting => setting.Key == helperKey))
            {
                return description;
            }
            if (IsSafeHttpsDestination(fetchUrls[0]) == false)
            {
                return description;
            }
            if (string.IsNullOrEmpty(description.PushUrls[0]) == true)
            {
                return description;
            }

            StringBuilder fingerprintInput = new();
            fingerprintInput.Append(repository.RootPath).Append('\0').Append(remoteName).Append('\0').Append(sharedConfigPath).Append('\0');
            fingerprintInput.Append(fetchUrls[0]).Append('\0').Append(pushUrls[0]).Append('\0');
            foreach ((string key, string value, string scope, string origin) in settings)
            {
                fingerprintInput.Append(key).Append('\0').Append(value).Append('\0').Append(scope).Append('\0').Append(origin).Append('\0');
            }
            description.RemovalFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput.ToString())));
            description.SharedConfigPath = sharedConfigPath;
            description.PushUrlAfterRemoval = description.FetchUrls[0];
            return description;
        }

        private static bool TryDescribeRewrite(string rawUrl, string effectiveUrl, IReadOnlyList<(string Key, string Value, string Scope, string Origin)> rewrites, string suffix, string rule, out (string Rule, string Scope, string Origin)? source)
        {
            source = null;
            if (string.IsNullOrEmpty(rawUrl) == true)
            {
                return false;
            }
            string rewrittenUrl = rawUrl;
            foreach ((string key, string value, string scope, string origin) in rewrites)
            {
                if (key.EndsWith(suffix, StringComparison.Ordinal) == false)
                {
                    continue;
                }
                if (value == null)
                {
                    return false;
                }
                if (rawUrl.StartsWith(value, StringComparison.Ordinal) == false)
                {
                    continue;
                }
                if (source != null)
                {
                    source = null;
                    return false;
                }
                string replacement = key.Substring("url.".Length, key.Length - "url.".Length - suffix.Length);
                rewrittenUrl = replacement + rawUrl[value.Length..];
                source = (rule, scope, origin);
            }
            if (rewrittenUrl != effectiveUrl)
            {
                source = null;
                return false;
            }
            return true;
        }

        private static bool HasPushRewrite(IReadOnlyList<(string Key, string Value, string Scope, string Origin)> rewrites, string rawFetchUrl)
        {
            foreach ((string Key, string Value, string Scope, string Origin) setting in rewrites)
            {
                if (setting.Key.EndsWith(".pushinsteadof", StringComparison.Ordinal) == false)
                {
                    continue;
                }
                if (setting.Value == null)
                {
                    return true;
                }
                if (rawFetchUrl.StartsWith(setting.Value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsDirectConfigOrigin(GitRepository repository, string configPath, string scope, string origin)
        {
            if (scope != "local")
            {
                return false;
            }
            if (origin.StartsWith("file:", StringComparison.Ordinal) == false)
            {
                return false;
            }
            string path = origin["file:".Length..];
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                return false;
            }
            try
            {
                return string.Equals(Path.GetFullPath(path, repository.RootPath), configPath, StringComparison.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsSafeHttpsDestination(string url)
        {
            if (HasUnsafeUrlCharacters(url))
            {
                return false;
            }
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }
            if (url.Any(char.IsWhiteSpace))
            {
                return false;
            }
            if (url.Contains('\\'))
            {
                return false;
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri address) == false)
            {
                return false;
            }
            return address.Scheme == Uri.UriSchemeHttps && address.Host.Length > 0 && address.UserInfo.Length == 0 && address.Query.Length == 0 && address.Fragment.Length == 0;
        }

        private static bool HasUnsafeUrlCharacters(string value)
        {
            if (value == null)
            {
                return false;
            }
            return value.Any(char.IsControl) || value.Contains('\uFFFD');
        }

        private static IReadOnlyList<string> MaskResolvedUrls(string[] urls)
        {
            if (urls == null)
            {
                return Array.Empty<string>();
            }
            return Array.AsReadOnly(urls.Select(MaskUrl).ToArray());
        }

        internal static string MaskUrl(string url)
        {
            if (string.IsNullOrEmpty(url) == true)
            {
                return url;
            }
            if (HasUnsafeUrlCharacters(url))
            {
                return null;
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri address))
            {
                switch (address.Scheme)
                {
                    case "https":
                    case "http":
                    case "ssh":
                    case "git":
                    case "ftp":
                    case "ftps":
                    case "file":
                        return SafeDisplayUrl(url);
                }
            }

            int separator = url.IndexOf(':');
            if (separator <= 0)
            {
                return null;
            }
            string host = url[..separator];
            string path = url[(separator + 1)..];
            int userSeparator = host.LastIndexOf('@');
            if (userSeparator >= 0)
            {
                host = host[(userSeparator + 1)..];
            }
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                return null;
            }
            if (path.Length == 0)
            {
                return null;
            }
            if (path[0] == ':')
            {
                return null;
            }
            if (path.StartsWith("//", StringComparison.Ordinal))
            {
                return null;
            }
            if (path.Contains('@'))
            {
                return null;
            }
            int suffix = path.AsSpan().IndexOfAny('?', '#');
            if (suffix >= 0)
            {
                path = path[..suffix];
            }
            return $"{host}:{path}";
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
            if (HasUnsafeUrlCharacters(url))
            {
                reasonCode = "GitHubRemoteUrlUnreadable";
                return false;
            }
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
                return $"{address.Scheme}://{address.Authority}{address.AbsolutePath}";
            }
            if (address.Query.Length > 0)
            {
                return $"{address.Scheme}://{address.Authority}{address.AbsolutePath}";
            }
            if (address.Fragment.Length > 0)
            {
                return $"{address.Scheme}://{address.Authority}{address.AbsolutePath}";
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
