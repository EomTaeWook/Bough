using System;

namespace Bough.Core.Git.Models
{
    public class GitCommitResult
    {
        public GitCommitResult(string commitHash, Exception readError)
        {
            CommitHash = commitHash;
            ReadError = readError;
        }

        public string CommitHash { get; }

        public Exception ReadError { get; }
    }
}
