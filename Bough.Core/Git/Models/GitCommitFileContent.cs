using System;
using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitCommitFileContent
    {
        public GitCommitFileContent(string repositoryRoot, string commitHash, string path, string text, string reasonCode, long size, string objectHash, params object[] reasonArguments)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            Path = path;
            Text = text;
            ReasonCode = reasonCode;
            object[] values = Array.Empty<object>();
            if (reasonArguments != null)
            {
                values = (object[])reasonArguments.Clone();
            }
            ReasonArguments = Array.AsReadOnly(values);
            Size = size;
            ObjectHash = objectHash;
        }

        public string RepositoryRoot { get; }

        public string CommitHash { get; }

        public string Path { get; }

        public string Text { get; }

        public string ReasonCode { get; }

        public IReadOnlyList<object> ReasonArguments { get; }

        public long Size { get; }

        public string ObjectHash { get; }

        public bool HasText { get { return ReasonCode.Length == 0; } }
    }
}
