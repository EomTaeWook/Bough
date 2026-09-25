using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitReferenceSnapshot
    {
        public GitReferenceSnapshot(string currentBranch, IReadOnlyList<GitLocalBranch> branches, IReadOnlyList<GitRemote> remotes, IReadOnlyList<GitTag> tags, IReadOnlyList<GitSubmodule> submodules)
        {
            CurrentBranch = currentBranch;
            Branches = branches;
            Remotes = remotes;
            Tags = tags;
            Submodules = submodules;
        }

        public string CurrentBranch { get; }
        public IReadOnlyList<GitLocalBranch> Branches { get; }
        public IReadOnlyList<GitRemote> Remotes { get; }
        public IReadOnlyList<GitTag> Tags { get; }
        public IReadOnlyList<GitSubmodule> Submodules { get; }
    }

    public class GitLocalBranch
    {
        public GitLocalBranch(string name, string commitHash, bool isCurrent, string upstreamName = null, string upstreamRemoteName = null, string upstreamRemoteRef = null)
        {
            Name = name;
            CommitHash = commitHash;
            IsCurrent = isCurrent;
            UpstreamName = upstreamName ?? string.Empty;
            UpstreamRemoteName = upstreamRemoteName ?? string.Empty;
            UpstreamRemoteRef = upstreamRemoteRef ?? string.Empty;
        }

        public string Name { get; }
        public string CommitHash { get; }
        public bool IsCurrent { get; }
        public string UpstreamName { get; }
        public string UpstreamRemoteName { get; }
        public string UpstreamRemoteRef { get; }
    }

    public class GitRemote
    {
        public GitRemote(string name, string url, IReadOnlyList<GitRemoteBranch> branches)
        {
            Name = name;
            Url = url;
            Branches = branches;
        }

        public string Name { get; }
        public string Url { get; }
        public IReadOnlyList<GitRemoteBranch> Branches { get; }
    }

    public class GitRemoteBranch
    {
        public GitRemoteBranch(string remoteName, string name, string commitHash)
        {
            RemoteName = remoteName;
            Name = name;
            CommitHash = commitHash;
        }

        public string RemoteName { get; }
        public string Name { get; }
        public string CommitHash { get; }
        public string FullName { get { return $"{RemoteName}/{Name}"; } }
    }

    public class GitTag
    {
        public GitTag(string name, string commitHash)
        {
            Name = name;
            CommitHash = commitHash;
        }

        public string Name { get; }
        public string CommitHash { get; }
    }

    public class GitStash
    {
        public GitStash(string name, string message, string commitHash)
        {
            Name = name;
            Message = message;
            CommitHash = commitHash;
        }

        public string Name { get; }
        public string Message { get; }
        public string CommitHash { get; }
    }

    public class GitSubmodule
    {
        public GitSubmodule(string path, string url, string expectedCommit, string checkedOutCommit, string state)
        {
            Path = path;
            Url = url;
            ExpectedCommit = expectedCommit;
            CheckedOutCommit = checkedOutCommit;
            State = state;
        }

        public string Path { get; }
        public string Url { get; }
        public string ExpectedCommit { get; }
        public string CheckedOutCommit { get; }
        public string State { get; }
    }
}
