using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git.Models
{
    public class GitCommitTreeListing
    {
        public GitCommitTreeListing(string repositoryRoot, string commitHash, string directoryPath, IEnumerable<GitCommitTreeEntry> entries)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            DirectoryPath = directoryPath;
            Entries = Array.AsReadOnly(entries.ToArray());
        }

        public string RepositoryRoot { get; }

        public string CommitHash { get; }

        public string DirectoryPath { get; }

        public IReadOnlyList<GitCommitTreeEntry> Entries { get; }
    }
}
