using System;
using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitPullProgress
    {
        public GitPullProgress(GitPullStage stage, GitRemoteMessage transferStatus, IReadOnlyList<GitRemoteMessage> incomingSummary)
        {
            Stage = stage;
            TransferStatus = transferStatus;
            IncomingSummary = incomingSummary;
        }

        public GitPullStage Stage { get; }
        public GitRemoteMessage TransferStatus { get; }
        public IReadOnlyList<GitRemoteMessage> IncomingSummary { get; }
    }

    public class GitRemoteMessage
    {
        public GitRemoteMessage(string key, params object[] arguments)
        {
            Key = key;
            Arguments = arguments;
        }

        public string Key { get; }
        public IReadOnlyList<object> Arguments { get; }
    }

    public class GitFetchFailure
    {
        public GitFetchFailure(string remote, string error, int exitCode)
        {
            Remote = remote;
            Error = error;
            ExitCode = exitCode;
        }

        public string Remote { get; }
        public string Error { get; }
        public int ExitCode { get; }
    }

    public class GitRemoteState
    {
        public GitRemoteState(string repositoryRoot, string branchName, string headHash, string upstreamName, string upstreamRemote, string upstreamBranch, int ahead, int behind, IReadOnlyList<string> remotes)
        {
            RepositoryRoot = repositoryRoot;
            BranchName = branchName;
            HeadHash = headHash;
            UpstreamName = upstreamName;
            UpstreamRemote = upstreamRemote;
            UpstreamBranch = upstreamBranch;
            Ahead = ahead;
            Behind = behind;
            Remotes = remotes;
        }

        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string HeadHash { get; }
        public string UpstreamName { get; }
        public string UpstreamRemote { get; }
        public string UpstreamBranch { get; }
        public int Ahead { get; }
        public int Behind { get; }
        public IReadOnlyList<string> Remotes { get; }
        public bool IsDetached { get { return BranchName.Length == 0; } }
        public bool HasUpstream { get { return UpstreamRemote.Length > 0 && UpstreamBranch.Length > 0; } }
    }

    public class GitFetchResult
    {
        public GitFetchResult(IReadOnlyList<string> updatedReferences, IReadOnlyList<string> removedReferences, IReadOnlyList<string> succeededRemotes, IReadOnlyList<GitFetchFailure> failedRemotes)
        {
            UpdatedReferences = updatedReferences;
            RemovedReferences = removedReferences;
            SucceededRemotes = succeededRemotes;
            FailedRemotes = failedRemotes;
        }

        public IReadOnlyList<string> UpdatedReferences { get; }
        public IReadOnlyList<string> RemovedReferences { get; }
        public IReadOnlyList<string> SucceededRemotes { get; }
        public IReadOnlyList<GitFetchFailure> FailedRemotes { get; }
    }
}
