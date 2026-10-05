using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git.Models
{
    public class GitWorktreeStatus
    {
        public GitWorktreeStatus(IEnumerable<GitWorktreeFile> files, string mergeCommitMessage = "")
        {
            Files = Array.AsReadOnly(files.ToArray());
            MergeCommitMessage = mergeCommitMessage;
        }

        public IReadOnlyList<GitWorktreeFile> Files { get; }
        public string MergeCommitMessage { get; }
    }
}
