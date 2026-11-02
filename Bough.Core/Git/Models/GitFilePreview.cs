using System;
using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitFilePreview
    {
        public GitFilePreview(string text, string descriptionCode, bool isBinary, long size, params object[] descriptionArguments)
        {
            Text = text;
            DescriptionCode = descriptionCode;
            object[] arguments = Array.Empty<object>();
            if (descriptionArguments != null)
            {
                arguments = (object[])descriptionArguments.Clone();
            }
            DescriptionArguments = Array.AsReadOnly(arguments);
            IsBinary = isBinary;
            Size = size;
        }

        public string Text { get; }

        public string DescriptionCode { get; }
        public IReadOnlyList<object> DescriptionArguments { get; }

        public bool IsBinary { get; }

        public long Size { get; }

        public IReadOnlyList<GitDiffHunk> Hunks { get; internal set; } = Array.Empty<GitDiffHunk>();

        public bool IsStaged { get; internal set; }

        public string Path
        {
            get
            {
                if (File == null)
                {
                    return string.Empty;
                }
                return File.Path;
            }
        }

        internal string RepositoryRoot { get; set; }

        internal GitWorktreeFile File { get; set; }

        internal string HeadCommitHash { get; set; }

        internal string IndexEntry { get; set; }

        internal string WorktreeHash { get; set; }
    }
}
