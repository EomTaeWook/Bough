using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class WindowsTerminalLauncher : TerminalLauncher
    {
        private const int _maximumGitRootDepth = 3;

        public WindowsTerminalLauncher(GitExecutableSettings executableSettings)
            : base(executableSettings)
        {
        }

        protected override ProcessStartInfo CreatePlatformStartInfo(GitRepository repository)
        {
            List<string> arguments = new() { "--window", "new", "new-tab", "--useApplicationTitle", "powershell.exe" };
            arguments.AddRange(CreateConsoleArguments(repository));
            return CreateProcessInfo("wt.exe", repository.RootPath, arguments.ToArray());
        }

        protected override ProcessStartInfo CreateFallbackStartInfo(GitRepository repository)
        {
            return CreateProcessInfo("powershell.exe", repository.RootPath, CreateConsoleArguments(repository));
        }

        private string[] CreateConsoleArguments(GitRepository repository)
        {
            string command = CreateStartupCommand(repository);
            string bashPath = FindGitBash();
            if (bashPath.Length > 0)
            {
                command = "$ErrorActionPreference = 'Stop'; " + command
                    + "; $env:CHERE_INVOKING = '1'; & " + QuotePowerShellValue(bashPath)
                    + " '--login' '-i'; exit $LASTEXITCODE";
                string bashCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                return new string[] { "-NoLogo", "-NoProfile", "-EncodedCommand", bashCommand };
            }

            command += "; git --version";
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return new string[] { "-NoLogo", "-NoExit", "-EncodedCommand", encodedCommand };
        }

        private string CreateStartupCommand(GitRepository repository)
        {
            string command = "Set-Location -LiteralPath " + QuotePowerShellValue(repository.RootPath);
            string gitDirectory = GetGitDirectory();
            if (gitDirectory.Length > 0)
            {
                command = "$env:PATH = " + QuotePowerShellValue(gitDirectory + Path.PathSeparator) + " + $env:PATH; " + command;
            }

            return command;
        }

        private string FindGitBash()
        {
            string configuredGitDirectory = GetGitDirectory();
            if (configuredGitDirectory.Length > 0)
            {
                return FindGitBashNearGitDirectory(configuredGitDirectory);
            }

            string inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string entry in inheritedPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string directory = entry.Trim().Trim('"');
                if (Path.IsPathFullyQualified(directory) == false)
                {
                    continue;
                }
                if (File.Exists(Path.Combine(directory, "git.exe")) == false)
                {
                    continue;
                }

                return FindGitBashNearGitDirectory(directory);
            }

            string[] installationDirectories = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd")
            };
            foreach (string directory in installationDirectories)
            {
                string bashPath = FindGitBashNearGitDirectory(directory);
                if (bashPath.Length > 0)
                {
                    return bashPath;
                }
            }

            return string.Empty;
        }

        private static string FindGitBashNearGitDirectory(string gitDirectory)
        {
            DirectoryInfo directory = new(gitDirectory);
            for (int depth = 0; depth < _maximumGitRootDepth; depth++)
            {
                if (directory == null)
                {
                    break;
                }

                string bashPath = Path.Combine(directory.FullName, "bin", "bash.exe");
                if (File.Exists(bashPath))
                {
                    if (File.Exists(Path.Combine(directory.FullName, "cmd", "git.exe")))
                    {
                        return bashPath;
                    }
                    if (File.Exists(Path.Combine(directory.FullName, "bin", "git.exe")))
                    {
                        return bashPath;
                    }
                }
                directory = directory.Parent;
            }

            return string.Empty;
        }

        private static string QuotePowerShellValue(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
