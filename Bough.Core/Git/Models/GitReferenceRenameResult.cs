namespace Bough.Core.Git.Models
{
    public class GitReferenceRenameResult
    {
        public GitReferenceRenameResult(GitRepository repository, bool changed)
        {
            Repository = repository;
            Changed = changed;
        }

        public GitRepository Repository { get; }
        public bool Changed { get; }
    }
}
