using System;
using System.Linq;

namespace Bough.Core.Git
{
    public class GitStashMutationException : GitException
    {
        public static GitStashMutationException FromError(Exception exception, bool worktreeMayHaveChanged,
            bool stashesMayHaveChanged)
        {
            if (exception is GitException gitException)
            {
                if (string.IsNullOrWhiteSpace(gitException.ErrorCode) == false)
                {
                    return new GitStashMutationException(gitException, worktreeMayHaveChanged, stashesMayHaveChanged);
                }
            }

            return new GitStashMutationException(exception.Message, worktreeMayHaveChanged, stashesMayHaveChanged, exception);
        }

        private GitStashMutationException(GitException exception, bool worktreeMayHaveChanged,
            bool stashesMayHaveChanged)
            : base(exception.ErrorCode, exception, exception.Arguments.ToArray())
        {
            WorktreeMayHaveChanged = worktreeMayHaveChanged;
            StashesMayHaveChanged = stashesMayHaveChanged;
        }

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
