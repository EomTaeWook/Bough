using System;
using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitStashSaveResult
    {
        public GitStashSaveResult(GitStashEntry created, IReadOnlyList<GitStashEntry> entries)
            : this(created, entries, null)
        {
        }

        public GitStashSaveResult(GitStashEntry created, IReadOnlyList<GitStashEntry> entries, Exception readError)
        {
            Created = created;
            Entries = entries;
            ReadError = readError;
        }

        public GitStashEntry Created { get; }
        public IReadOnlyList<GitStashEntry> Entries { get; }
        public Exception ReadError { get; }
    }
}
