namespace Bough.Core.Git.Models
{
    public class GitRepository
    {
        public GitRepository(string rootPath, string displayName, string currentBranch)
        {
            RootPath = rootPath;
            DisplayName = displayName;
            CurrentBranch = currentBranch;
        }

        public string RootPath { get; }

        public string DisplayName { get; }

        public string CurrentBranch { get; }
    }
}
