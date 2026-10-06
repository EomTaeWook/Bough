namespace Bough.App.ViewModels.Models
{
    public class HistoryFileActionContext
    {
        public HistoryFileActionContext(string repositoryRoot, string commitHash, string path)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            Path = path;
        }

        public string RepositoryRoot { get; }
        public string CommitHash { get; }
        public string Path { get; }
    }
}
