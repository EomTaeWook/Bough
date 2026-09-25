using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git.Models
{
    public class GitUnifiedDiffHunk
    {
        public GitUnifiedDiffHunk(string header, int oldStart, int newStart, IEnumerable<GitUnifiedDiffLine> lines)
        {
            Header = header;
            OldStart = oldStart;
            NewStart = newStart;
            Lines = Array.AsReadOnly(lines.ToArray());
        }

        public string Header { get; }

        public int OldStart { get; }

        public int NewStart { get; }

        public IReadOnlyList<GitUnifiedDiffLine> Lines { get; }
    }
}
