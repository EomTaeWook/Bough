namespace Bough.App.ViewModels.Models
{
    public class HistoryFileActionContext
    {
        public HistoryFileActionContext(string repositoryRoot, string commitHash, string path,
            bool isDeleted = false, bool isDirectory = false, bool isGitlink = false,
            int inspectionRequest = 0, string comparisonParent = null)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            Path = path;
            IsDeleted = isDeleted;
            IsDirectory = isDirectory;
            IsGitlink = isGitlink;
            InspectionRequest = inspectionRequest;
            ComparisonParent = comparisonParent;
        }

        public string RepositoryRoot { get; }
        public string CommitHash { get; }
        public string Path { get; }
        public bool IsDeleted { get; }
        public bool IsDirectory { get; }
        public bool IsGitlink { get; }
        public int InspectionRequest { get; }
        public string ComparisonParent { get; }
    }
}
