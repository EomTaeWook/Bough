using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitStopTrackingPlan
    {
        public GitStopTrackingPlan(IReadOnlyList<GitDiscardPlan> files, GitIgnorePlan ignore)
        {
            Files = files;
            Ignore = ignore;
        }

        public IReadOnlyList<GitDiscardPlan> Files { get; }

        public GitIgnorePlan Ignore { get; }
    }
}
