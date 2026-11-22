using System;
using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitPullResult
    {
        public GitPullResult(IReadOnlyList<GitPullSubmoduleResult> submodules, bool submodulesCanceled = false, Exception submoduleError = null)
        {
            Submodules = submodules;
            SubmodulesCanceled = submodulesCanceled;
            SubmoduleError = submoduleError;
        }

        public IReadOnlyList<GitPullSubmoduleResult> Submodules { get; }
        public bool SubmodulesCanceled { get; }
        public Exception SubmoduleError { get; }
    }
}
