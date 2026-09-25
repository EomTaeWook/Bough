using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitCommitDetails
    {
        public GitCommitDetails(string body, IReadOnlyList<GitChangedFile> files)
        {
            Body = body;
            Files = files;
        }

        public string Body { get; }
        public IReadOnlyList<GitChangedFile> Files { get; }
    }
}
