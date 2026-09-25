using System;

namespace Bough.Core.Git
{
    public class GitBlameLine
    {
        public GitBlameLine(int lineNumber, int originalLineNumber, string commitHash, string author, DateTimeOffset authoredAt, string summary, string originalPath, string text)
        {
            LineNumber = lineNumber;
            OriginalLineNumber = originalLineNumber;
            CommitHash = commitHash;
            Author = author;
            AuthoredAt = authoredAt;
            Summary = summary;
            OriginalPath = originalPath;
            Text = text;
        }

        public int LineNumber { get; }

        public int OriginalLineNumber { get; }

        public string CommitHash { get; }

        public string Author { get; }

        public DateTimeOffset AuthoredAt { get; }

        public string Summary { get; }

        public string OriginalPath { get; }

        public string Text { get; }
    }
}
