using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.Core.Git
{
    public class GitSettingsService
    {
        private static readonly string[] _versionArguments = new string[] { "--version" };
        private static readonly string[] _credentialHelperArguments = new string[] { "config", "--show-origin", "--get-all", "credential.helper" };
        private static readonly string[] _remoteArguments = new string[] { "remote" };
        private readonly GitCommandRunner _runner;
        private readonly GitExecutableSettings _executableSettings;

        public GitSettingsService(GitCommandRunner runner)
            : this(runner, GitExecutableSettings.Default)
        {
        }

        public GitSettingsService(GitCommandRunner runner, GitExecutableSettings executableSettings)
        {
            _runner = runner;
            _executableSettings = executableSettings;
        }

        public string ConfiguredGitPath { get { return _executableSettings.ConfiguredPath; } }

        public async Task<string> TestGitAsync(string candidatePath, CancellationToken cancellationToken = default)
        {
            string executable = "git";
            if (string.IsNullOrWhiteSpace(candidatePath) == false)
            {
                executable = candidatePath.Trim();
            }
            if (executable != "git" && Path.IsPathFullyQualified(executable) == false)
            {
                throw new ArgumentException($"Git 실행 파일의 절대 경로를 입력하세요: {candidatePath}", nameof(candidatePath));
            }
            if (executable != "git" && File.Exists(executable) == false)
            {
                throw new FileNotFoundException($"Git 실행 파일을 찾을 수 없습니다: {executable}", executable);
            }

            GitCommandResult result = await _runner.RunWithExecutableAsync(executable, Directory.GetCurrentDirectory(), _versionArguments, false, cancellationToken);
            string version = result.Output.Trim();
            if (version.StartsWith("git version ", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new GitException($"선택한 파일의 Git 버전을 확인할 수 없습니다: {version}");
            }

            return version;
        }

        public async Task<string> SaveGitPathAsync(string candidatePath, CancellationToken cancellationToken = default)
        {
            string selectedPath = candidatePath?.Trim() ?? string.Empty;
            string version = await TestGitAsync(selectedPath, cancellationToken);
            if (selectedPath == "git")
            {
                selectedPath = string.Empty;
            }
            _executableSettings.SavePath(selectedPath);
            return version;
        }

        public async Task<GitSettingsSnapshot> GetSnapshotAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            string localName = await ReadConfigAsync(repository, "--local", "user.name", cancellationToken);
            string localEmail = await ReadConfigAsync(repository, "--local", "user.email", cancellationToken);
            string globalName = await ReadConfigAsync(repository, "--global", "user.name", cancellationToken);
            string globalEmail = await ReadConfigAsync(repository, "--global", "user.email", cancellationToken);
            GitCommandResult helperResult = await _runner.RunAsync(repository.RootPath, _credentialHelperArguments, true, cancellationToken);
            string helper = "설정되지 않음";
            if (helperResult.ExitCode == 0)
            {
                helper = helperResult.Output.Trim();
            }
            if (helperResult.ExitCode != 0 && helperResult.ExitCode != 1)
            {
                throw new GitException($"credential.helper 설정을 읽지 못했습니다: {helperResult.Error.Trim()}");
            }

            ArrayQueue<GitRemote> remotes = [];
            GitCommandResult remoteResult = await _runner.RunAsync(repository.RootPath, _remoteArguments, false, cancellationToken);
            foreach (string name in remoteResult.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                GitCommandResult urlResult = await _runner.RunAsync(repository.RootPath, new string[] { "remote", "get-url", name }, true, cancellationToken);
                string url = string.Empty;
                if (urlResult.ExitCode == 0)
                {
                    url = GitReferenceService.SanitizeUrl(urlResult.Output.Trim());
                }
                else
                {
                    throw new GitException($"{name} 원격 URL을 읽지 못했습니다: {urlResult.Error.Trim()}");
                }
                remotes.Add(new GitRemote(name, url, Array.Empty<GitRemoteBranch>()));
            }

            return new GitSettingsSnapshot(localName, localEmail, globalName, globalEmail, helper, remotes.ToArray());
        }

        public async Task SaveAuthorAsync(GitRepository repository, bool global, string name, string email, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repository);
            string scope = "--local";
            if (global == true)
            {
                scope = "--global";
            }
            await WriteConfigAsync(repository, scope, "user.name", name, cancellationToken);
            await WriteConfigAsync(repository, scope, "user.email", email, cancellationToken);
        }

        private async Task<string> ReadConfigAsync(GitRepository repository, string scope, string key, CancellationToken cancellationToken)
        {
            GitCommandResult result = await _runner.RunAsync(repository.RootPath, new string[] { "config", scope, "--get", key }, true, cancellationToken);
            if (result.ExitCode == 0)
            {
                return result.Output.TrimEnd('\r', '\n');
            }
            if (result.ExitCode == 1)
            {
                return string.Empty;
            }

            throw new GitException($"{scope} {key} 설정을 읽지 못했습니다: {result.Error.Trim()}");
        }

        private async Task WriteConfigAsync(GitRepository repository, string scope, string key, string value, CancellationToken cancellationToken)
        {
            string input = value?.Trim() ?? string.Empty;
            if (input.Length == 0)
            {
                GitCommandResult unsetResult = await _runner.RunAsync(repository.RootPath, new string[] { "config", scope, "--unset-all", key }, true, cancellationToken);
                if (unsetResult.ExitCode != 0 && unsetResult.ExitCode != 5)
                {
                    throw new GitException($"{scope} {key} 설정을 지우지 못했습니다: {unsetResult.Error.Trim()}");
                }

                return;
            }

            await _runner.RunAsync(repository.RootPath, new string[] { "config", scope, key, input }, false, cancellationToken);
        }
    }
}
