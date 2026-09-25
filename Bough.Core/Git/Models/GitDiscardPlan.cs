using System;
using System.IO;

namespace Bough.Core.Git
{
    public class GitDiscardPlan
    {
        public GitDiscardPlan(GitWorktreeFile file, bool exists, long length, DateTime lastWriteUtc, DateTime creationUtc, FileAttributes attributes, string linkTarget, string contentHash)
        {
            File = file;
            Exists = exists;
            Length = length;
            LastWriteUtc = lastWriteUtc;
            CreationUtc = creationUtc;
            Attributes = attributes;
            LinkTarget = linkTarget;
            ContentHash = contentHash;
        }

        public GitWorktreeFile File { get; }

        public string Path { get { return File.Path; } }

        public bool IsUntracked { get { return File.IsUntracked; } }

        public bool IsWorktreeRename { get { return File.WorktreeStatus == 'R'; } }

        public bool Exists { get; }

        public long Length { get; }

        public DateTime LastWriteUtc { get; }

        public DateTime CreationUtc { get; }

        public FileAttributes Attributes { get; }

        public string LinkTarget { get; }

        public string ContentHash { get; }

        public string IndexEntries { get; internal set; }
    }
}
