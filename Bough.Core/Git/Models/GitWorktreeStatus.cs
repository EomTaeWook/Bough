using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Bough.Core.Git.Models
{
    public class GitWorktreeStatus
    {
        public GitWorktreeStatus(IEnumerable<GitWorktreeFile> files, string mergeCommitMessage = "", string headCommitHash = null, IReadOnlyDictionary<string, string> indexEntries = null)
        {
            Files = Array.AsReadOnly(files.ToArray());
            MergeCommitMessage = mergeCommitMessage;
            HeadCommitHash = headCommitHash;
            Dictionary<string, string> entries = new(StringComparer.Ordinal);
            if (indexEntries != null)
            {
                foreach (KeyValuePair<string, string> entry in indexEntries)
                {
                    entries.Add(entry.Key, entry.Value);
                }
            }
            IndexEntries = new ReadOnlyDictionary<string, string>(entries);
        }

        public IReadOnlyList<GitWorktreeFile> Files { get; }
        public string MergeCommitMessage { get; }
        public string HeadCommitHash { get; }
        public IReadOnlyDictionary<string, string> IndexEntries { get; }
    }
}
