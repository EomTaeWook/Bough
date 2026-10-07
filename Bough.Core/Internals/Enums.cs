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

    public enum GitResetMode
    {
        Soft,
        Mixed,
        Hard
    }

    public enum GitRevertOutcome
    {
        Completed,
        Paused,
        Aborted,
        Failed
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
