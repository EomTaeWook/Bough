using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitStashPreview
    {
        public GitStashPreview(IEnumerable<string> files, string diff)
        {
            Files = Array.AsReadOnly(files.ToArray());
            Diff = diff;
        }

        public IReadOnlyList<string> Files { get; }

        public string Diff { get; }
    }
}
