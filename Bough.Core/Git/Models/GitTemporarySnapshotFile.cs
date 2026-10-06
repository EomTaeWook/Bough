namespace Bough.Core.Git.Models
{
    public class GitTemporarySnapshotFile
    {
        internal GitTemporarySnapshotFile(string directoryPath, string filePath)
        {
            DirectoryPath = directoryPath;
            FilePath = filePath;
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }
    }
}
