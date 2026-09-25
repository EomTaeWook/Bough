namespace Bough.App.Internals
{
    public enum AppearanceThemeMode
    {
        Light,
        Dark,
        System
    }

    public enum StringLanguage
    {
        Korean,
        English
    }

    public enum AuthorPhotoSource
    {
        LocalIdenticon,
        GitHubCommit,
        GitHubLinkedAccount,
        Gravatar
    }

    public enum ConflictStageOutcome
    {
        Succeeded,
        NoLongerConflicted,
        FileChanged,
        InvalidResolution,
        Canceled,
        Failed,
        RefreshFailed
    }

    internal enum HistoryReferenceKind
    {
        CurrentBranch,
        LocalBranch,
        RemoteBranch,
        Tag,
        Other
    }

    public enum ReferenceTreeNodeKind
    {
        Section,
        Branch,
        Tag,
        Remote,
        RemoteBranch,
        Stash,
        Submodule,
        Empty
    }

    public enum RemoteOperationOutcome
    {
        None,
        Running,
        Succeeded,
        Failed,
        Canceled,
        PartiallySucceeded,
        RefreshFailed,
        NoNewCommits
    }

    public enum RemoteOperationKind
    {
        Fetch,
        Pull,
        Push
    }

    public enum StashMutationKind
    {
        Save,
        Apply,
        Pop,
        Drop
    }
}
