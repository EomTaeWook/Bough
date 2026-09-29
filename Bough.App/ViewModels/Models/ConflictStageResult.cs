using System;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.App.Internals;

namespace Bough.App.ViewModels.Models
{
    public class ConflictStageResult
    {
        public ConflictStageResult(GitRepository repository, string path, ConflictStageOutcome outcome,
            string messageCode = null, Exception exception = null, Exception refreshException = null)
        {
            Repository = repository;
            Path = path;
            Outcome = outcome;
            MessageCode = messageCode;
            Exception = exception;
            RefreshException = refreshException;
        }

        public GitRepository Repository { get; }
        public string Path { get; }
        public ConflictStageOutcome Outcome { get; }
        public string MessageCode { get; }
        public Exception Exception { get; }
        public Exception RefreshException { get; }
        public bool Succeeded { get { return Outcome == ConflictStageOutcome.Succeeded; } }
        public bool WasStaged { get { return Outcome == ConflictStageOutcome.Succeeded || Outcome == ConflictStageOutcome.RefreshFailed; } }
    }
}
