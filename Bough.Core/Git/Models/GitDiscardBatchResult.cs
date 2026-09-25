using System;
using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitDiscardBatchResult
    {
        public GitDiscardBatchResult(IReadOnlyList<string> completedPaths, IReadOnlyList<string> remainingPaths, Exception error)
        {
            CompletedPaths = completedPaths;
            RemainingPaths = remainingPaths;
            Error = error;
        }

        public IReadOnlyList<string> CompletedPaths { get; }

        public IReadOnlyList<string> RemainingPaths { get; }

        public Exception Error { get; }

        public bool HasError { get { return Error != null; } }
    }
}
