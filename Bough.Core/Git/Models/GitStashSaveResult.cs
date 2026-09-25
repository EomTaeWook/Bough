using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitStashSaveResult
    {
        public GitStashSaveResult(GitStashEntry created, IReadOnlyList<GitStashEntry> entries)
        {
            Created = created;
            Entries = entries;
        }

        public GitStashEntry Created { get; }
        public IReadOnlyList<GitStashEntry> Entries { get; }
    }
}
