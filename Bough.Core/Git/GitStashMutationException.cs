using System;

namespace Bough.Core.Git
{
    public class GitStashMutationException : GitException
    {
        public GitStashMutationException(string message, bool worktreeMayHaveChanged, bool stashesMayHaveChanged, Exception innerException)
            : base(message, innerException)
        {
            WorktreeMayHaveChanged = worktreeMayHaveChanged;
            StashesMayHaveChanged = stashesMayHaveChanged;
        }

        public bool WorktreeMayHaveChanged { get; }
        public bool StashesMayHaveChanged { get; }
    }
}
