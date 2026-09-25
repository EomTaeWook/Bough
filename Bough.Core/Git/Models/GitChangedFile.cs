namespace Bough.Core.Git
{
    public class GitChangedFile
    {
        public GitChangedFile(string status, string path)
        {
            Status = status;
            Path = path;
        }

        public string Status { get; }
        public string Path { get; }
    }
}
