using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitStagePlan
    {
        public GitStagePlan(string selectedPath, IEnumerable<GitWorktreeFile> files, IEnumerable<GitLargeFileCandidate> largeFiles)
        {
            SelectedPath = selectedPath;
            Files = Array.AsReadOnly(files.ToArray());
            LargeFiles = Array.AsReadOnly(largeFiles.ToArray());
        }

        public string SelectedPath { get; }

        public IReadOnlyList<GitWorktreeFile> Files { get; }

        public IReadOnlyList<GitLargeFileCandidate> LargeFiles { get; }
    }
}
