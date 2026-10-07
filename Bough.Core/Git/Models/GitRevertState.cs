using System;
using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitRevertState
    {
        public GitRevertState(string repositoryRoot, string branchName, string headHash, string targetHash,
            string operationFingerprint, IReadOnlyList<string> conflictPaths, string worktreeFingerprint = null)
        {
            RepositoryRoot = repositoryRoot;
            BranchName = branchName;
            HeadHash = headHash;
            TargetHash = targetHash;
            OperationFingerprint = operationFingerprint;
            WorktreeFingerprint = worktreeFingerprint;
            ConflictPaths = conflictPaths ?? Array.Empty<string>();
        }

        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string HeadHash { get; }
        public string TargetHash { get; }
        public string OperationFingerprint { get; }
        public string WorktreeFingerprint { get; }
        public IReadOnlyList<string> ConflictPaths { get; }
        public bool IsInProgress { get { return string.IsNullOrEmpty(TargetHash) == false; } }
    }
}
