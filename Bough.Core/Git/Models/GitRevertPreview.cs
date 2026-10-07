using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitRevertPreview
    {
        public GitRevertPreview(string repositoryRoot, string branchName, string headHash, string targetHash,
            string subject, IReadOnlyList<GitRevertParent> parents)
        {
            RepositoryRoot = repositoryRoot;
            BranchName = branchName;
            HeadHash = headHash;
            TargetHash = targetHash;
            Subject = subject;
            Parents = parents;
        }

        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string HeadHash { get; }
        public string TargetHash { get; }
        public string Subject { get; }
        public IReadOnlyList<GitRevertParent> Parents { get; }
    }
}
