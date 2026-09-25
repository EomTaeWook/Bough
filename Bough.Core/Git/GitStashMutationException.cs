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

            if (exception is GitException uncodedGitException)
            {
                return new GitStashMutationException(uncodedGitException.Message, worktreeMayHaveChanged, stashesMayHaveChanged, uncodedGitException);
            }

            if (exception is OperationCanceledException)
            {
                return new GitStashMutationException("StashMutationCanceled", exception, worktreeMayHaveChanged, stashesMayHaveChanged);
            }

            return new GitStashMutationException(exception, worktreeMayHaveChanged, stashesMayHaveChanged);
        }

        private GitStashMutationException(GitException exception, bool worktreeMayHaveChanged,
            bool stashesMayHaveChanged)
            : base(exception.ErrorCode, exception, exception.Arguments.ToArray())
        {
            WorktreeMayHaveChanged = worktreeMayHaveChanged;
            StashesMayHaveChanged = stashesMayHaveChanged;
        }

        private GitStashMutationException(Exception exception, bool worktreeMayHaveChanged,
            bool stashesMayHaveChanged)
            : this("StashMutationFailed", exception, worktreeMayHaveChanged, stashesMayHaveChanged)
        {
        }

        private GitStashMutationException(string errorCode, Exception exception, bool worktreeMayHaveChanged,
            bool stashesMayHaveChanged)
            : base(errorCode, exception, Array.Empty<object>())
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
