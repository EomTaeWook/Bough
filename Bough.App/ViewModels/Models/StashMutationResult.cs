using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.App.Internals;

namespace Bough.App.ViewModels.Models
{
    public class StashMutationResult
    {
        public StashMutationResult(GitRepository repository, StashMutationKind kind, bool succeeded, bool worktreeMayHaveChanged, bool stashesMayHaveChanged, GitWorktreeStatus worktreeStatus, string errorText)
        {
            Repository = repository;
            Kind = kind;
            Succeeded = succeeded;
            WorktreeMayHaveChanged = worktreeMayHaveChanged;
            StashesMayHaveChanged = stashesMayHaveChanged;
            WorktreeStatus = worktreeStatus;
            ErrorText = errorText;
        }

        public GitRepository Repository { get; }
        public StashMutationKind Kind { get; }
        public bool Succeeded { get; }
        public bool WorktreeMayHaveChanged { get; }
        public bool StashesMayHaveChanged { get; }
        public GitWorktreeStatus WorktreeStatus { get; }
        public string ErrorText { get; }
    }
}
