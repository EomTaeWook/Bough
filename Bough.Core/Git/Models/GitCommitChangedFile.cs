namespace Bough.Core.Git.Models
{
    public class GitCommitChangedFile
    {
        public GitCommitChangedFile(string status, string path, string previousPath)
        {
            Status = status;
            Path = path;
            PreviousPath = previousPath;
        }

        public string Status { get; }

        public char StatusCode { get { return Status[0]; } }

        public string Path { get; }

        public string PreviousPath { get; }
    }
}
