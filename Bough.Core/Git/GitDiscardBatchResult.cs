using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitDiscardBatchResult
    {
        public GitDiscardBatchResult(IReadOnlyList<string> completedPaths, IReadOnlyList<string> remainingPaths, string error)
        {
            CompletedPaths = completedPaths;
            RemainingPaths = remainingPaths;
            Error = error;
        }

        public IReadOnlyList<string> CompletedPaths { get; }

        public IReadOnlyList<string> RemainingPaths { get; }

        public string Error { get; }

        public bool HasError { get { return string.IsNullOrEmpty(Error) == false; } }
    }
}
