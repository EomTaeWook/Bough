using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitFileHistoryPage
    {
        public GitFileHistoryPage(string repositoryRoot, string revisionHash, string path, IEnumerable<GitFileHistoryEntry> entries)
        {
            RepositoryRoot = repositoryRoot;
            RevisionHash = revisionHash;
            Path = path;
            Entries = Array.AsReadOnly(entries.ToArray());
        }

        public string RepositoryRoot { get; }

        public string RevisionHash { get; }

        public string Path { get; }

        public IReadOnlyList<GitFileHistoryEntry> Entries { get; }
    }
}
