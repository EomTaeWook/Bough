using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git.Models
{
    public class GitCommitInspection
    {
        public GitCommitInspection(string repositoryRoot, string hash, IEnumerable<string> parents, IEnumerable<string> references, string authorName, string authorEmail, DateTimeOffset authoredAt, string title, string body)
        {
            RepositoryRoot = repositoryRoot;
            Hash = hash;
            Parents = Array.AsReadOnly(parents.ToArray());
            References = Array.AsReadOnly(references.ToArray());
            AuthorName = authorName;
            AuthorEmail = authorEmail;
            AuthoredAt = authoredAt;
            Title = title;
            Body = body;
        }

        public string RepositoryRoot { get; }

        public string Hash { get; }

        public IReadOnlyList<string> Parents { get; }

        public IReadOnlyList<string> References { get; }

        public string AuthorName { get; }

        public string AuthorEmail { get; }

        public DateTimeOffset AuthoredAt { get; }

        public string Title { get; }

        public string Body { get; }

        public string DefaultParent
        {
            get
            {
                if (Parents.Count == 0)
                {
                    return string.Empty;
                }

                return Parents[0];
            }
        }
    }
}
