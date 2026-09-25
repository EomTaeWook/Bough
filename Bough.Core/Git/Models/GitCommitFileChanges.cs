using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitCommitFileChanges
    {
        public GitCommitFileChanges(string repositoryRoot, string commitHash, string parentHash, IEnumerable<GitCommitChangedFile> files)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            ParentHash = parentHash;
            Files = Array.AsReadOnly(files.ToArray());
        }

        public string RepositoryRoot { get; }

        public string CommitHash { get; }

        public string ParentHash { get; }

        public IReadOnlyList<GitCommitChangedFile> Files { get; }
    }
}
