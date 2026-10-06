using System;

namespace Bough.Core.Git.Models
{
    public class GitReferenceRenameResult
    {
        public GitReferenceRenameResult(GitRepository repository, bool changed, Exception readError = null)
        {
            Repository = repository;
            Changed = changed;
            ReadError = readError;
        }

        public GitRepository Repository { get; }
        public bool Changed { get; }
        public Exception ReadError { get; }
    }
}
