using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Bough.Core.Git
{
    public class TerminalLauncher
    {
        private static readonly string[] _powershellArguments = new string[] { "-NoExit" };

        public void Open(GitRepository repository)
        {
            if (OperatingSystem.IsWindows() == true)
            {
                try
                {
                    Start(CreateStartInfo(repository));
                    return;
                }
                catch (Win32Exception)
                {
                    try
                    {
                        Start(CreateProcessInfo("powershell.exe", repository.RootPath, _powershellArguments));
                    }
                    catch (Win32Exception exception)
                    {
                        throw new GitException("TerminalStartFailed", exception, "powershell.exe");
                    }
                    return;
                }
            }

            ProcessStartInfo startInfo = CreateStartInfo(repository);
            try
            {
                Start(startInfo);
            }
            catch (Win32Exception exception)
            {
                throw new GitException("TerminalStartFailed", exception, startInfo.FileName);
            }
        }

        public ProcessStartInfo CreateStartInfo(GitRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (Directory.Exists(repository.RootPath) == false)
            {
                throw new GitException("RepositoryFolderMissing", null, repository.RootPath);
            }

            if (OperatingSystem.IsWindows() == true)
            {
                return CreateProcessInfo("wt.exe", repository.RootPath, new string[] { "-d", repository.RootPath });
            }

            if (OperatingSystem.IsMacOS() == true)
            {
                return CreateProcessInfo("osascript", repository.RootPath, new string[]
                {
                    "-e", "on run argv",
                    "-e", "tell application \"Terminal\"",
                    "-e", "activate",
                    "-e", "do script \"cd \" & quoted form of (item 1 of argv)",
                    "-e", "end tell",
                    "-e", "end run",
                    repository.RootPath
                });
            }

            if (OperatingSystem.IsLinux() == true)
            {
                return CreateProcessInfo("x-terminal-emulator", repository.RootPath, Array.Empty<string>());
            }

            throw new GitException("TerminalPlatformUnsupported", null, Array.Empty<object>());
        }

        private static ProcessStartInfo CreateProcessInfo(string executable, string workingDirectory, string[] arguments)
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
