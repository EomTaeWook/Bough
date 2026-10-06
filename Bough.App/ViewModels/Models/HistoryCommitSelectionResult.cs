using System;
using Bough.App.Internals;
using Bough.Core.Internals;

namespace Bough.App.ViewModels.Models
{
    public class HistoryCommitSelectionResult
    {
        public HistoryCommitSelectionResult(string repositoryRoot, string commitHash, GitHistoryScope scope,
            HistoryCommitSelectionOutcome outcome, Exception error, int requestVersion)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            Scope = scope;
            Outcome = outcome;
            Error = error;
            RequestVersion = requestVersion;
        }

        public string RepositoryRoot { get; }
        public string CommitHash { get; }
        public GitHistoryScope Scope { get; }
        public HistoryCommitSelectionOutcome Outcome { get; }
        public Exception Error { get; }
        public int RequestVersion { get; }
    }
}
