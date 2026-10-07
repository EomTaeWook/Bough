using System;
using Bough.Core.Internals;

namespace Bough.Core.Git.Models
{
    public class GitRevertResult
    {
        public GitRevertResult(string repositoryRoot, GitRevertOutcome outcome, GitRepository repository, GitRevertState state, Exception readError, Exception operationError = null)
        {
            RepositoryRoot = repositoryRoot;
            Outcome = outcome;
            Repository = repository;
            State = state;
            ReadError = readError;
            OperationError = operationError;
        }

        public string RepositoryRoot { get; }
        public GitRevertOutcome Outcome { get; }
        public GitRepository Repository { get; }
        public GitRevertState State { get; }
        public Exception ReadError { get; }
        public Exception OperationError { get; }
    }
}
