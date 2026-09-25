using System;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;
using Bough.App.Internals;

namespace Bough.App.ViewModels.Models
{
    public class RemoteOperationRequest
    {
        private RemoteOperationRequest(RemoteOperationKind kind, GitRepository repository, string localBranch,
            string remote, string remoteBranch, bool fetchAll, bool prune, GitPullStrategy pullStrategy, bool pushTargetConfirmed)
        {
            ArgumentNullException.ThrowIfNull(repository);
            Kind = kind;
            Repository = repository;
            LocalBranch = localBranch ?? string.Empty;
            Remote = remote ?? string.Empty;
            RemoteBranch = remoteBranch ?? string.Empty;
            FetchAll = fetchAll;
            Prune = prune;
            PullStrategy = pullStrategy;
            PushTargetConfirmed = pushTargetConfirmed;
        }

        public RemoteOperationKind Kind { get; }
        public GitRepository Repository { get; }
        public string LocalBranch { get; }
        public string Remote { get; }
        public string RemoteBranch { get; }
        public bool FetchAll { get; }
        public bool Prune { get; }
        public GitPullStrategy PullStrategy { get; }
        public bool PushTargetConfirmed { get; }

        public static RemoteOperationRequest ForFetch(GitRepository repository, string remote, bool fetchAll, bool prune)
        {
            return new RemoteOperationRequest(RemoteOperationKind.Fetch, repository, string.Empty,
                remote, string.Empty, fetchAll, prune, default, false);
        }

        public static RemoteOperationRequest ForPull(GitRepository repository, string localBranch, string remote,
            string remoteBranch, GitPullStrategy strategy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localBranch);
            ArgumentException.ThrowIfNullOrWhiteSpace(remote);
            ArgumentException.ThrowIfNullOrWhiteSpace(remoteBranch);
            return new RemoteOperationRequest(RemoteOperationKind.Pull, repository, localBranch,
                remote, remoteBranch, false, false, strategy, false);
        }

        public static RemoteOperationRequest ForPush(GitRepository repository, string localBranch, string remote,
            string remoteBranch, bool targetConfirmed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localBranch);
            ArgumentException.ThrowIfNullOrWhiteSpace(remote);
            ArgumentException.ThrowIfNullOrWhiteSpace(remoteBranch);
            return new RemoteOperationRequest(RemoteOperationKind.Push, repository, localBranch,
                remote, remoteBranch, false, false, default, targetConfirmed);
        }
    }

    public class RemoteOperationStateSnapshot
    {
        public RemoteOperationStateSnapshot(GitRepository repository, GitRemoteState state)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(state);
            Repository = repository;
            State = state;
        }

        public GitRepository Repository { get; }
        public GitRemoteState State { get; }
    }
}
