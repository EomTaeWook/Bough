using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitWorktreeStatus
    {
        public GitWorktreeStatus(IEnumerable<GitWorktreeFile> files)
        {
            Files = Array.AsReadOnly(files.ToArray());
        }

        public IReadOnlyList<GitWorktreeFile> Files { get; }
    }
}
