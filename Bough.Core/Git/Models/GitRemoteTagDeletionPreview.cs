namespace Bough.Core.Git.Models
{
    public class GitRemoteTagDeletionPreview
    {
        public GitRemoteTagDeletionPreview(string repositoryRoot, string remoteName, string pushUrl, string tagName, string objectId)
        {
            RepositoryRoot = repositoryRoot;
            RemoteName = remoteName;
            PushUrl = pushUrl;
            TagName = tagName;
            ObjectId = objectId;
        }

        public string RepositoryRoot { get; }
        public string RemoteName { get; }
        public string PushUrl { get; }
        public string TagName { get; }
        public string ObjectId { get; }
        public string ReferenceName { get { return "refs/tags/" + TagName; } }
    }
}
