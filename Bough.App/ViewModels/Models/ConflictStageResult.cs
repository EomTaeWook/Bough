using System;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.App.Internals;

namespace Bough.App.ViewModels.Models
{
    public class ConflictStageResult
    {
        public ConflictStageResult(GitRepository repository, string path, ConflictStageOutcome outcome, string message, Exception exception = null)
        {
            Repository = repository;
            Path = path;
            Outcome = outcome;
            Message = message;
            Exception = exception;
        }

        public GitRepository Repository { get; }
        public string Path { get; }
        public ConflictStageOutcome Outcome { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public bool Succeeded { get { return Outcome == ConflictStageOutcome.Succeeded; } }
        public bool WasStaged { get { return Outcome == ConflictStageOutcome.Succeeded || Outcome == ConflictStageOutcome.RefreshFailed; } }
    }
}
