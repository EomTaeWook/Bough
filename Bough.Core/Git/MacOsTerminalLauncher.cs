using System.Diagnostics;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class MacOsTerminalLauncher : TerminalLauncher
    {
        public MacOsTerminalLauncher(GitExecutableSettings executableSettings)
            : base(executableSettings)
        {
        }

        protected override ProcessStartInfo CreatePlatformStartInfo(GitRepository repository)
        {
            return CreateProcessInfo("osascript", repository.RootPath, new string[]
            {
                "-e", "on run argv",
                "-e", "tell application \"Terminal\"",
                "-e", "activate",
                "-e", "do script (item 1 of argv)",
                "-e", "end tell",
                "-e", "end run",
                CreateShellStartupCommand(repository)
            });
        }

        private string CreateShellStartupCommand(GitRepository repository)
        {
            string command = "cd " + QuoteShellValue(repository.RootPath);
            string gitDirectory = GetGitDirectory();
            if (gitDirectory.Length > 0)
            {
                command = "export PATH=" + QuoteShellValue(gitDirectory) + ":\"$PATH\"; " + command;
            }

            return command;
        }

        private static string QuoteShellValue(string value)
        {
            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }
    }
}
