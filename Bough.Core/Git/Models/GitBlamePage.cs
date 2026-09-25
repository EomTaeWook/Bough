using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitBlamePage
    {
        public GitBlamePage(string repositoryRoot, string revisionHash, string path, int startLine, IEnumerable<GitBlameLine> lines)
        {
            RepositoryRoot = repositoryRoot;
            RevisionHash = revisionHash;
            Path = path;
            StartLine = startLine;
            Lines = Array.AsReadOnly(lines.ToArray());
        }

        public string RepositoryRoot { get; }

        public string RevisionHash { get; }

        public string Path { get; }

        public int StartLine { get; }

        public IReadOnlyList<GitBlameLine> Lines { get; }
    }
}
