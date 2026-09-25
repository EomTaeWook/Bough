using System;

namespace Bough.Core.Git.Models
{
    public class GitFileHistoryEntry
    {
        public GitFileHistoryEntry(string commitHash, string author, DateTimeOffset authoredAt, string title, string path, string previousPath)
        {
            CommitHash = commitHash;
            Author = author;
            AuthoredAt = authoredAt;
            Title = title;
            Path = path;
            PreviousPath = previousPath;
        }

        public string CommitHash { get; }

        public string Author { get; }

        public DateTimeOffset AuthoredAt { get; }

        public string Title { get; }

        public string Path { get; }

        public string PreviousPath { get; }
    }
}
