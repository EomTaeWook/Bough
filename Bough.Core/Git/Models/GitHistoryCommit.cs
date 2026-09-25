using System;
using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitHistoryCommit
    {
        public GitHistoryCommit(string hash, IReadOnlyList<string> parents, string subject, string author, string authorEmail, DateTimeOffset authoredAt, IReadOnlyList<string> references)
        {
            Hash = hash;
            Parents = parents;
            Subject = subject;
            Author = author;
            AuthorEmail = authorEmail;
            AuthoredAt = authoredAt;
            References = references;
        }

        public string Hash { get; }
        public IReadOnlyList<string> Parents { get; }
        public string Subject { get; }
        public string Author { get; }
        public string AuthorEmail { get; }
        public DateTimeOffset AuthoredAt { get; }
        public IReadOnlyList<string> References { get; }
    }
}
