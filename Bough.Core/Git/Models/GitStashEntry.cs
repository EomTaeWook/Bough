using System;

namespace Bough.Core.Git
{
    public class GitStashEntry
    {
        public GitStashEntry(string name, string commitHash, string message, string branch, DateTimeOffset createdAt)
        {
            Name = name;
            CommitHash = commitHash;
            Message = message;
            Branch = branch;
            CreatedAt = createdAt;
        }

        public string Name { get; }

        public string CommitHash { get; }

        public string Message { get; }

        public string Branch { get; }

        public DateTimeOffset CreatedAt { get; }

        public string CreatedAtText { get { return CreatedAt.ToLocalTime().ToString("g"); } }

        public string DisplayText { get { return $"{Name} · {Message}"; } }
    }
}
