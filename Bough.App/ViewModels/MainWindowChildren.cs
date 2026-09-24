using Bough.App.Localization;
using Bough.Core.Git;
using Dignus.DependencyInjection.Attributes;

namespace Bough.App.ViewModels
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class MainWindowChildren
    {
        public MainWindowChildren(GitHistoryService historyService, GitReferenceService referenceService, GitCommitActionService actionService, GitRemoteOperationService remoteOperationService, GitCommitInspectionService inspectionService, GitCommitMessageService commitMessageService, GitCommitFileActionService fileActionService, GitWorkingTreeService workingTreeService, GitStashService stashService, GitSettingsViewModel gitSettings, TerminalLauncher terminalLauncher, RepositoryFolderLauncher folderLauncher, GitRepositoryService repositoryService, GitOperationQueue operationQueue, StringHelper stringHelper)
        {
            History = new HistoryViewModel(historyService, actionService, inspectionService, commitMessageService, fileActionService, operationQueue);
            LocalChanges = new LocalChangesViewModel(workingTreeService, stashService, operationQueue, stringHelper);
            References = new ReferenceExplorerViewModel(referenceService, actionService, terminalLauncher, folderLauncher, LocalChanges.Stashes, stringHelper, operationQueue);
            RemoteOperations = new RemoteOperationsViewModel(remoteOperationService, repositoryService);
            GitSettings = gitSettings;
        }

        public HistoryViewModel History { get; }
        public LocalChangesViewModel LocalChanges { get; }
        public ReferenceExplorerViewModel References { get; }
        public RemoteOperationsViewModel RemoteOperations { get; }
        public GitSettingsViewModel GitSettings { get; }
    }
}
