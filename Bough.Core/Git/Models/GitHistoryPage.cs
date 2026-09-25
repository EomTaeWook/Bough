using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitHistoryPage
    {
        public GitHistoryPage(IReadOnlyList<GitHistoryCommit> commits, bool hasMore)
        {
            Commits = commits;
            HasMore = hasMore;
        }

        public IReadOnlyList<GitHistoryCommit> Commits { get; }
        public bool HasMore { get; }
    }
}
