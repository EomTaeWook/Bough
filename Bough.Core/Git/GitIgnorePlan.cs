using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitIgnorePlan
    {
        public GitIgnorePlan(GitIgnoreLocation location, string targetPath, IReadOnlyList<GitIgnoreEntry> entries, byte[] originalBytes, string newLine)
        {
            Location = location;
            TargetPath = targetPath;
            Entries = entries;
            OriginalBytes = originalBytes;
            NewLine = newLine;
        }

        public GitIgnoreLocation Location { get; }

        public string TargetPath { get; }

        public IReadOnlyList<GitIgnoreEntry> Entries { get; }

        public byte[] OriginalBytes { get; }

        public string NewLine { get; }
    }
}
