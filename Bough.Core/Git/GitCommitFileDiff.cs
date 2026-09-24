using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitCommitFileDiff
    {
        public GitCommitFileDiff(string repositoryRoot, string commitHash, string parentHash, string path, string previousPath, string text, string reason, IEnumerable<GitUnifiedDiffHunk> hunks)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            ParentHash = parentHash;
            Path = path;
            PreviousPath = previousPath;
            Text = text;
            Reason = reason;
            Hunks = Array.AsReadOnly(hunks.ToArray());
        }

        public string RepositoryRoot { get; }

        public string CommitHash { get; }

        public string ParentHash { get; }

        public string Path { get; }

        public string PreviousPath { get; }

        public string Text { get; }

        public string Reason { get; }

        public IReadOnlyList<GitUnifiedDiffHunk> Hunks { get; }

        public bool HasText { get { return Reason.Length == 0; } }
    }
}
