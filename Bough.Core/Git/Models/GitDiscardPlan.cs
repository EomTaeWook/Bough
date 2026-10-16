using System;
using System.IO;

namespace Bough.Core.Git.Models
{
    public class GitDiscardPlan
    {
        public GitDiscardPlan(GitWorktreeFile file, string submoduleRoot, string submoduleHead,
            string submoduleHeadReference, string submoduleIndexCommit)
            : this(file, true, 0, default, default, default, null, null)
        {
            SubmoduleRoot = submoduleRoot;
            SubmoduleHead = submoduleHead;
            SubmoduleHeadReference = submoduleHeadReference;
            SubmoduleIndexCommit = submoduleIndexCommit;
        }

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

        public bool IsSubmodule { get { return File.IsSubmodule; } }

        public string SubmoduleRoot { get; }

        public string SubmoduleHead { get; }

        public string SubmoduleHeadReference { get; }

        public string SubmoduleIndexCommit { get; }

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
