namespace Bough.Core.Internals
{
    public enum ResolutionChoiceType
    {
        Unresolved,
        Ours,
        Theirs,
        Both,
        Remove
    }
}

namespace Bough.Core.Git
{
    public enum GitResetMode
    {
        Soft,
        Mixed,
        Hard
    }

    public enum GitHistoryScope
    {
        All,
        CurrentBranch
    }

    public enum GitPullStrategy
    {
        FastForwardOnly,
        Merge,
        Rebase
    }

    public enum GitPullStage
    {
        Fetching,
        Inspecting,
        Applying
    }

    public enum GitIgnoreLocation
    {
        Repository,
        Local
    }
}
