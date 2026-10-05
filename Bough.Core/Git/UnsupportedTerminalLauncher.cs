using System;
using Bough.Core.Git.Models;
using Bough.Core.Interfaces;

namespace Bough.Core.Git
{
    public class UnsupportedTerminalLauncher : ITerminalLauncher
    {
        public void Open(GitRepository repository)
        {
            throw new GitException("TerminalPlatformUnsupported", null, Array.Empty<object>());
        }
    }
}
