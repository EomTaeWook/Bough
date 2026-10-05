using System;
using System.Diagnostics;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class LinuxTerminalLauncher : TerminalLauncher
    {
        public LinuxTerminalLauncher(GitExecutableSettings executableSettings)
            : base(executableSettings)
        {
        }

        protected override ProcessStartInfo CreatePlatformStartInfo(GitRepository repository)
        {
            return CreateProcessInfo("x-terminal-emulator", repository.RootPath, Array.Empty<string>());
        }
    }
}
