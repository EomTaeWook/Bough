namespace Bough.App.Appearance
{
    public enum AppearanceThemeMode
    {
        Light,
        Dark,
        System
    }
}

namespace Bough.App.Localization
{
    public enum StringLanguage
    {
        Korean,
        English
    }
}

namespace Bough.App.Controls
{
    public enum AuthorPhotoSource
    {
        LocalIdenticon,
        GitHubCommit,
        GitHubLinkedAccount,
        Gravatar
    }
}

namespace Bough.App.ViewModels
{
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
