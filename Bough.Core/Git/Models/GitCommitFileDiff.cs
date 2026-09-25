using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git.Models
{
    public class GitCommitFileDiff
    {
        public GitCommitFileDiff(string repositoryRoot, string commitHash, string parentHash, string path, string previousPath, string text, string reasonCode, IEnumerable<GitUnifiedDiffHunk> hunks, params object[] reasonArguments)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            ParentHash = parentHash;
            Path = path;
            PreviousPath = previousPath;
            Text = text;
            ReasonCode = reasonCode;
            object[] values = Array.Empty<object>();
            if (reasonArguments != null)
            {
                values = (object[])reasonArguments.Clone();
            }
            ReasonArguments = Array.AsReadOnly(values);
            Hunks = Array.AsReadOnly(hunks.ToArray());
        }

        public string RepositoryRoot { get; }

        public string CommitHash { get; }

        public string ParentHash { get; }

        public string Path { get; }

        public string PreviousPath { get; }

        public string Text { get; }

        public string ReasonCode { get; }

        public IReadOnlyList<object> ReasonArguments { get; }

        public IReadOnlyList<GitUnifiedDiffHunk> Hunks { get; }

        public bool HasText { get { return ReasonCode.Length == 0; } }
    }
}
