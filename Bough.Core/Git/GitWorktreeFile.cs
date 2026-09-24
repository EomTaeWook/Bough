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

        public string StatusText
        {
            get
            {
                if (IsConflict == true)
                {
                    return "Conflict";
                }

                if (IsUntracked == true)
                {
                    return "Untracked";
                }

                if (IndexStatus == 'R' || WorktreeStatus == 'R')
                {
                    return "Renamed";
                }

                if (IndexStatus == 'A' || WorktreeStatus == 'A')
                {
                    return "Added";
                }

                if (IndexStatus == 'D' || WorktreeStatus == 'D')
                {
                    return "Deleted";
                }

                if (IndexStatus == 'C' || WorktreeStatus == 'C')
                {
                    return "Copied";
                }

                return "Modified";
            }
        }

        public string DisplayStatusText
        {
            get
            {
                if (IsPartiallyStaged == true)
                {
                    return $"{StatusText} · partially staged";
                }

                return StatusText;
            }
        }

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
