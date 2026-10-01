namespace Bough.Core.Git.Models
{
    public class GitReferenceRenameRequest
    {
        public GitReferenceRenameRequest(string repositoryRoot, string referenceName, string objectId, string newName)
        {
            RepositoryRoot = repositoryRoot;
            ReferenceName = referenceName;
            ObjectId = objectId;
            NewName = newName;
        }

        public string RepositoryRoot { get; }
        public string ReferenceName { get; }
        public string ObjectId { get; }
        public string NewName { get; }
    }
}
