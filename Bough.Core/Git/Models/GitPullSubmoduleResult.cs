using System;
using Bough.Core.Internals;

namespace Bough.Core.Git.Models
{
    public class GitPullSubmoduleResult
    {
        public GitPullSubmoduleResult(string path, GitPullSubmoduleOutcome outcome, Exception error = null)
        {
            Path = path;
            Outcome = outcome;
            Error = error;
        }

        public string Path { get; }
        public GitPullSubmoduleOutcome Outcome { get; }
        public Exception Error { get; }
    }
}
