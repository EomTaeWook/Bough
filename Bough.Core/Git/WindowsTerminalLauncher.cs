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
        public WindowsTerminalLauncher(GitExecutableSettings executableSettings)
            : base(executableSettings)
        {
        }

        protected override ProcessStartInfo CreatePlatformStartInfo(GitRepository repository)
        {
            List<string> arguments = new() { "new-tab", "powershell.exe" };
            arguments.AddRange(CreatePowerShellArguments(repository));
            return CreateProcessInfo("wt.exe", repository.RootPath, arguments.ToArray());
        }

        protected override ProcessStartInfo CreateFallbackStartInfo(GitRepository repository)
        {
            return CreateProcessInfo("powershell.exe", repository.RootPath, CreatePowerShellArguments(repository));
        }

        private string[] CreatePowerShellArguments(GitRepository repository)
        {
            string command = "Set-Location -LiteralPath " + QuotePowerShellValue(repository.RootPath) + "; git --version";
            string gitDirectory = GetGitDirectory();
            if (gitDirectory.Length > 0)
            {
                command = "$env:PATH = " + QuotePowerShellValue(gitDirectory + Path.PathSeparator) + " + $env:PATH; " + command;
            }

            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return new string[] { "-NoLogo", "-NoExit", "-EncodedCommand", encodedCommand };
        }

        private static string QuotePowerShellValue(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
