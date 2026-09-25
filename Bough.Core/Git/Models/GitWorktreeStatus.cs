using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git.Models
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
