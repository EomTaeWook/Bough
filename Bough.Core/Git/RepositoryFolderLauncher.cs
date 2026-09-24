using System;
using System.Diagnostics;
using System.IO;

namespace Bough.Core.Git
{
    public class RepositoryFolderLauncher
    {
        public void Open(GitRepository repository)
        {
            ProcessStartInfo startInfo = CreateStartInfo(repository);
            Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException($"파일 탐색기를 실행하지 못했습니다: {startInfo.FileName}");
            }
            process.Dispose();
        }

        public ProcessStartInfo CreateStartInfo(GitRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (Directory.Exists(repository.RootPath) == false)
            {
                throw new DirectoryNotFoundException($"저장소 폴더를 찾을 수 없습니다: {repository.RootPath}");
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
                throw new PlatformNotSupportedException("이 운영체제의 파일 탐색기 실행 방식은 지원하지 않습니다.");
            }
            startInfo.ArgumentList.Add(repository.RootPath);
            return startInfo;
        }
    }
}
