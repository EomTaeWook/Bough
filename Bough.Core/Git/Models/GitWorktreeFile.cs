using System;

namespace Bough.Core.Git
{
    public class GitWorktreeFile
    {
        public GitWorktreeFile(string path, string originalPath, char indexStatus, char worktreeStatus)
        {
            Path = path;
            OriginalPath = originalPath;
            IndexStatus = indexStatus;
            WorktreeStatus = worktreeStatus;
        }

        public string Path { get; }

        public string OriginalPath { get; }

        public char IndexStatus { get; }

        public char WorktreeStatus { get; }

        public bool IsConflict
        {
            get
            {
                switch (IndexStatus, WorktreeStatus)
                {
                    case ('D', 'D'):
                    case ('A', 'U'):
                    case ('U', 'D'):
                    case ('U', 'A'):
                    case ('D', 'U'):
                    case ('A', 'A'):
                    case ('U', 'U'):
                        return true;
                    default:
                        return false;
                }
            }
        }

        public bool IsUntracked { get { return IndexStatus == '?' && WorktreeStatus == '?'; } }

        public bool IsStaged { get { return IsConflict == false && IsUntracked == false && IndexStatus != ' '; } }

        public bool IsUnstaged { get { return IsConflict == false && (IsUntracked == true || WorktreeStatus != ' '); } }

        public bool IsPartiallyStaged { get { return IsStaged == true && IsUnstaged == true; } }

        public string StatusCode
        {
            get
            {
                if (IsConflict == true)
                {
                    return "WorktreeStatusConflict";
                }

                if (IsUntracked == true)
                {
                    return "WorktreeStatusUntracked";
                }

                if (IndexStatus == 'R' || WorktreeStatus == 'R')
                {
                    return "WorktreeStatusRenamed";
                }

                if (IndexStatus == 'A' || WorktreeStatus == 'A')
                {
                    return "WorktreeStatusAdded";
                }

                if (IndexStatus == 'D' || WorktreeStatus == 'D')
                {
                    return "WorktreeStatusDeleted";
                }

                if (IndexStatus == 'C' || WorktreeStatus == 'C')
                {
                    return "WorktreeStatusCopied";
                }

                return "WorktreeStatusModified";
            }
        }

        public string DisplayStatusText { get; set; } = string.Empty;

        public string DisplayPath
        {
            get
            {
                if (string.IsNullOrEmpty(OriginalPath) == true)
                {
                    return Path;
                }

                return $"{OriginalPath} → {Path}";
            }
        }
    }
}
