using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Bough.Core.Git
{
    public class RepositoryFolderLauncher
    {
        public void Open(GitRepository repository)
        {
            ProcessStartInfo startInfo = CreateStartInfo(repository);
            Process process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Win32Exception exception)
            {
                throw new GitException("ExplorerStartFailed", exception, startInfo.FileName);
            }
            if (process == null)
            {
                throw new GitException("ExplorerStartFailed", null, startInfo.FileName);
            }
            process.Dispose();
        }

        public ProcessStartInfo CreateStartInfo(GitRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (Directory.Exists(repository.RootPath) == false)
            {
                throw new GitException("RepositoryFolderMissing", null, repository.RootPath);
            }

            ProcessStartInfo startInfo = new()
            {
                WorkingDirectory = repository.RootPath,
                UseShellExecute = false
            };
            if (OperatingSystem.IsWindows() == true)
            {
                startInfo.FileName = "explorer.exe";
            }
            else if (OperatingSystem.IsMacOS() == true)
            {
                startInfo.FileName = "open";
            }
            else if (OperatingSystem.IsLinux() == true)
            {
                startInfo.FileName = "xdg-open";
            }
            else
            {
                throw new GitException("ExplorerPlatformUnsupported", null, Array.Empty<object>());
            }
            startInfo.ArgumentList.Add(repository.RootPath);
            return startInfo;
        }
    }
}
