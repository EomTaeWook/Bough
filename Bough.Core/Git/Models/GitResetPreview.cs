namespace Bough.Core.Git
{
    public class GitResetPreview
    {
        public GitResetPreview(string repositoryRoot, string branchName, string headHash, string targetHash, string targetSubject, string statusSnapshot, string stateFingerprint, int stagedCount, int workingCount, int untrackedCount, bool isAncestor)
        {
            RepositoryRoot = repositoryRoot;
            BranchName = branchName;
            HeadHash = headHash;
            TargetHash = targetHash;
            TargetSubject = targetSubject;
            StatusSnapshot = statusSnapshot;
            StateFingerprint = stateFingerprint;
            StagedCount = stagedCount;
            WorkingCount = workingCount;
            UntrackedCount = untrackedCount;
            IsAncestor = isAncestor;
        }

        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string HeadHash { get; }
        public string TargetHash { get; }
        public string TargetSubject { get; }
        public string StatusSnapshot { get; }
        public string StateFingerprint { get; }
        public int StagedCount { get; }
        public int WorkingCount { get; }
        public int UntrackedCount { get; }
        public bool IsAncestor { get; }
        public string ShortHash { get { return TargetHash.Substring(0, 8); } }
    }
}
