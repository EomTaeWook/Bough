using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Bough.Core.Git.Models;
using Bough.Core.Interfaces;

namespace Bough.Core.Git
{
    public abstract class TerminalLauncher : ITerminalLauncher
    {
        private readonly GitExecutableSettings _executableSettings;

        protected TerminalLauncher(GitExecutableSettings executableSettings)
        {
            if (executableSettings == null)
            {
                throw new ArgumentNullException(nameof(executableSettings));
            }

            _executableSettings = executableSettings;
        }

        public void Open(GitRepository repository)
        {
            ProcessStartInfo startInfo = CreateStartInfo(repository);
            try
            {
                Start(startInfo);
            }
            catch (Win32Exception exception)
            {
                ProcessStartInfo fallback = CreateFallbackStartInfo(repository);
                if (fallback == null)
                {
                    throw new GitException("TerminalStartFailed", exception, startInfo.FileName);
                }

                try
                {
                    Start(fallback);
                }
                catch (Win32Exception fallbackException)
                {
                    throw new GitException("TerminalStartFailed", fallbackException, fallback.FileName);
                }
            }
        }

        public ProcessStartInfo CreateStartInfo(GitRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (Directory.Exists(repository.RootPath) == false)
            {
                throw new GitException("RepositoryFolderMissing", null, repository.RootPath);
            }

            return CreatePlatformStartInfo(repository);
        }

        protected abstract ProcessStartInfo CreatePlatformStartInfo(GitRepository repository);

        protected virtual ProcessStartInfo CreateFallbackStartInfo(GitRepository repository)
        {
            return null;
        }

        protected string GetGitDirectory()
        {
            string configuredPath = _executableSettings.ConfiguredPath;
            if (string.IsNullOrWhiteSpace(configuredPath) == true)
            {
                return string.Empty;
            }

            string gitDirectory = Path.GetDirectoryName(configuredPath);
            if (string.IsNullOrEmpty(gitDirectory) == true)
            {
                return string.Empty;
            }

            return gitDirectory;
        }

        protected ProcessStartInfo CreateProcessInfo(string executable, string workingDirectory, string[] arguments)
        {
            ProcessStartInfo info = new()
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            string gitDirectory = GetGitDirectory();
            if (gitDirectory.Length > 0)
            {
                string inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                info.Environment["PATH"] = gitDirectory + Path.PathSeparator + inheritedPath;
            }

            return info;
        }

        private static void Start(ProcessStartInfo info)
        {
            Process process = Process.Start(info);
            if (process == null)
            {
                throw new GitException("TerminalStartFailed", null, info.FileName);
            }

            process.Dispose();
        }
    }
}
