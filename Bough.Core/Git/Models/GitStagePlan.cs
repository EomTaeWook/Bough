using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git.Models
{
    public class GitStagePlan
    {
        public GitStagePlan(string selectedPath, IEnumerable<GitWorktreeFile> files, IEnumerable<GitLargeFileCandidate> largeFiles)
            : this(files, largeFiles, GetSelectedPaths(selectedPath))
        {
        }

        public GitStagePlan(IEnumerable<GitWorktreeFile> files, IEnumerable<GitLargeFileCandidate> largeFiles, IReadOnlyList<string> selectedPaths)
        {
            if (selectedPaths != null)
            {
                SelectedPaths = Array.AsReadOnly(selectedPaths.ToArray());
            }
            Files = Array.AsReadOnly(files.ToArray());
            LargeFiles = Array.AsReadOnly(largeFiles.ToArray());
        }

        public string SelectedPath
        {
            get
            {
                if (SelectedPaths == null)
                {
                    return null;
                }
                if (SelectedPaths.Count != 1)
                {
                    return null;
                }
                return SelectedPaths[0];
            }
        }

        public IReadOnlyList<string> SelectedPaths { get; }

        public IReadOnlyList<GitWorktreeFile> Files { get; }

        public IReadOnlyList<GitLargeFileCandidate> LargeFiles { get; }

        private static IReadOnlyList<string> GetSelectedPaths(string selectedPath)
        {
            if (selectedPath == null)
            {
                return null;
            }
            return new string[] { selectedPath };
        }
    }
}
