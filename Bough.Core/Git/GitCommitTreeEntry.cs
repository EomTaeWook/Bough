namespace Bough.Core.Git
{
    public class GitCommitTreeEntry
    {
        public GitCommitTreeEntry(string path, string mode, string objectType, string objectHash)
        {
            Path = path;
            Mode = mode;
            ObjectType = objectType;
            ObjectHash = objectHash;
        }

        public string Path { get; }

        public string Mode { get; }

        public string ObjectType { get; }

        public string ObjectHash { get; }

        public bool IsDirectory { get { return ObjectType == "tree"; } }

        public bool IsGitlink { get { return Mode == "160000"; } }
    }
}
