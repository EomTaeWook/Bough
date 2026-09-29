using System;

namespace Bough.Core.Git.Models
{
    public enum GitConflictStageOutcome
    {
        Succeeded,
        NoLongerConflicted,
        FileChanged,
        IncompleteMarkers,
        UnresolvedMarkers
    }

    public class GitConflictStageResult
    {
        public GitConflictStageResult(GitConflictStageOutcome outcome, Exception exception = null)
        {
            Outcome = outcome;
            Exception = exception;
        }

        public GitConflictStageOutcome Outcome { get; }
        public Exception Exception { get; }
    }
}
