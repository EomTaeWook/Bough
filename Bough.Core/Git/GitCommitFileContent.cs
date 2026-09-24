namespace Bough.Core.Git
{
    public class GitCommitFileContent
    {
        public GitCommitFileContent(string repositoryRoot, string commitHash, string path, string text, string reason, long size, string objectHash)
        {
            RepositoryRoot = repositoryRoot;
            CommitHash = commitHash;
            Path = path;
            Text = text;
            Reason = reason;
            Size = size;
            ObjectHash = objectHash;
        }

        public string RepositoryRoot { get; }

        public string CommitHash { get; }

        public string Path { get; }

        public string Text { get; }

        public string Reason { get; }

        public long Size { get; }

        public string ObjectHash { get; }

        public bool HasText { get { return Reason.Length == 0; } }
    }
}
