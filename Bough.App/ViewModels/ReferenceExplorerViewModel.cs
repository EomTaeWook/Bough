using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.App.Internals;

namespace Bough.App.ViewModels
{
    public class ReferenceExplorerViewModel : ViewModelBase
    {
        private readonly GitReferenceService _referenceService;
        private readonly ReferenceSnapshotPresenter _snapshotPresenter;
        private readonly ReferenceBranchPresenter _branchPresenter;
        private readonly ReferenceMutationPresenter _mutationPresenter;
        private readonly TerminalLauncher _terminalLauncher;
        private readonly RepositoryFolderLauncher _folderLauncher;
        private readonly PullRequestLauncher _pullRequestLauncher;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly ObservableCollection<GitLocalBranch> _branches;
        private readonly ObservableCollection<GitRemote> _remotes;
        private readonly ObservableCollection<GitTag> _tags;
        private readonly ObservableCollection<GitSubmodule> _submodules;
        private readonly ObservableCollection<ReferenceTreeNode> _treeRoots;
        private readonly Dictionary<string, Dictionary<string, bool>> _expansionByRepository;
        private GitRepository _repository;
        private ReferenceTreeNode _selectedTreeNode;
        private string _currentBranch;
        private string _statusMessage;
        private string _branchSwitchFailureMessage;
        private string _pendingBranchSwitchMessage;
        private string _remoteCheckoutWarning;
        private bool _isBusy;
        private int _branchChangeVersion;
        private bool _branchChangeInProgress;
        private string _branchChangeTarget;

        public ReferenceExplorerViewModel(GitReferenceService referenceService, GitCommitActionService actionService, TerminalLauncher terminalLauncher, RepositoryFolderLauncher folderLauncher, PullRequestLauncher pullRequestLauncher, StashViewModel stashViewModel, StringHelper stringHelper, GitOperationQueue operationQueue)
        {
            _referenceService = referenceService;
            _snapshotPresenter = new ReferenceSnapshotPresenter(referenceService);
            _branchPresenter = new ReferenceBranchPresenter(referenceService, operationQueue);
            _mutationPresenter = new ReferenceMutationPresenter(referenceService, actionService, operationQueue);
            _terminalLauncher = terminalLauncher;
            _folderLauncher = folderLauncher;
            _pullRequestLauncher = pullRequestLauncher;
            _stringHelper = stringHelper;
            _errorLocalizer = new GitErrorLocalizer(stringHelper);
            _branches = [];
            _remotes = [];
            _tags = [];
            _submodules = [];
            _treeRoots = [];
            StringComparer pathComparer = StringComparer.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                pathComparer = StringComparer.OrdinalIgnoreCase;
            }
            _expansionByRepository = new Dictionary<string, Dictionary<string, bool>>(pathComparer);
            Branches = new ReadOnlyObservableCollection<GitLocalBranch>(_branches);
            Remotes = new ReadOnlyObservableCollection<GitRemote>(_remotes);
            Tags = new ReadOnlyObservableCollection<GitTag>(_tags);
            Stashes = stashViewModel.Stashes;
            ((INotifyCollectionChanged)Stashes).CollectionChanged += delegate
            {
                NotifyEmptyStates();
                BuildTree();
            };
            Submodules = new ReadOnlyObservableCollection<GitSubmodule>(_submodules);
            TreeRoots = new ReadOnlyObservableCollection<ReferenceTreeNode>(_treeRoots);
            _currentBranch = string.Empty;
            _statusMessage = _stringHelper.GetString("ReferenceSwitchRepositoryRequired");
            _branchSwitchFailureMessage = string.Empty;
            _pendingBranchSwitchMessage = string.Empty;
            _remoteCheckoutWarning = string.Empty;
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanUseRepository);
            OpenTerminalCommand = new RelayCommand(OpenTerminal, CanUseRepository);
            OpenFolderCommand = new RelayCommand(OpenFolder, CanUseRepository);
        }

        public event Action<GitRepository> RepositoryChanged;
        public event Action<string> TagCommitSelected;
        public event Action StashesRequested;
        public event Action<string> StashSelected;
        public GitRepository CurrentRepository { get { return _repository; } }
        public StringHelper Strings { get { return _stringHelper; } }

        public string ReferenceText(string name)
        {
            return _stringHelper.GetString(name);
        }

        public string ReferenceTitle { get { return _stringHelper.GetString("ReferenceTitle"); } }
        public string ReferenceTreeAccessibleName { get { return _stringHelper.GetString("ReferenceTreeAccessibleName"); } }

        public ReadOnlyObservableCollection<GitLocalBranch> Branches { get; }
        public ReadOnlyObservableCollection<GitRemote> Remotes { get; }
        public ReadOnlyObservableCollection<GitTag> Tags { get; }
        public ReadOnlyObservableCollection<GitStashEntry> Stashes { get; }
        public ReadOnlyObservableCollection<GitSubmodule> Submodules { get; }
        public ReadOnlyObservableCollection<ReferenceTreeNode> TreeRoots { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public RelayCommand OpenTerminalCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public bool HasUsableRepository { get { return CanUseRepository(); } }
        public bool IsBranchChangeInProgress { get { return _branchChangeInProgress; } }

        public bool HasSubmodules { get { return Submodules.Count > 0; } }
        public bool HasBranches { get { return Branches.Count > 0; } }
        public bool HasRemotes { get { return Remotes.Count > 0; } }
        public bool HasTags { get { return Tags.Count > 0; } }
        public bool HasStashes { get { return Stashes.Count > 0; } }
        public bool HasNoBranches { get { return Branches.Count == 0; } }
        public bool HasNoRemotes { get { return Remotes.Count == 0; } }
        public bool HasNoTags { get { return Tags.Count == 0; } }
        public bool HasNoStashes { get { return Stashes.Count == 0; } }

        public ReferenceTreeNode SelectedTreeNode
        {
            get { return _selectedTreeNode; }
            set { SetProperty(ref _selectedTreeNode, value); }
        }

        public string CurrentBranch
        {
            get { return _currentBranch; }
            private set
            {
                if (SetProperty(ref _currentBranch, value))
                {
                    OnPropertyChanged(nameof(CurrentBranchDisplay));
                }
            }
        }
        public string CurrentBranchDisplay
        {
            get
            {
                if (CurrentBranch == "Detached HEAD")
                {
                    return _stringHelper.GetString("ReferenceDetachedHead");
                }
                return CurrentBranch;
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    OnPropertyChanged(nameof(HasStatusMessage));
                }
            }
        }
        public bool HasStatusMessage { get { return string.IsNullOrWhiteSpace(StatusMessage) == false; } }

        public string BranchSwitchFailureMessage
        {
            get { return _branchSwitchFailureMessage; }
            private set
            {
                if (SetProperty(ref _branchSwitchFailureMessage, value) == true)
                {
                    OnPropertyChanged(nameof(HasBranchSwitchFailure));
                }
            }
        }
        public bool HasBranchSwitchFailure { get { return BranchSwitchFailureMessage.Length > 0; } }
        public string PendingBranchSwitchMessage
        {
            get { return _pendingBranchSwitchMessage; }
            private set
            {
                if (SetProperty(ref _pendingBranchSwitchMessage, value))
                {
                    OnPropertyChanged(nameof(HasPendingBranchSwitch));
                }
            }
        }
        public bool HasPendingBranchSwitch { get { return PendingBranchSwitchMessage.Length > 0; } }
        public string RemoteCheckoutWarning { get { return _remoteCheckoutWarning; } }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    RefreshCommand.NotifyCanExecuteChanged();
                    OpenTerminalCommand.NotifyCanExecuteChanged();
                    OpenFolderCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(HasUsableRepository));
                }
            }
        }

        public async Task SetRepositoryAsync(GitRepository repository)
        {
            BindRepository(repository);
            if (repository == null)
            {
                return;
            }

            await RefreshCoreAsync();
        }

        public void BindRepository(GitRepository repository)
        {
            bool differentRepository = _repository == null || repository == null;
            if (_repository != null && repository != null)
            {
                StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                differentRepository = string.Equals(_repository.RootPath, repository.RootPath, comparison) == false;
            }
            if (differentRepository)
            {
                _branchChangeVersion++;
                _branchChangeInProgress = false;
                _branchChangeTarget = null;
                PendingBranchSwitchMessage = string.Empty;
                BranchSwitchFailureMessage = string.Empty;
            }
            _snapshotPresenter.BindRepository(repository);
            IsBusy = _branchChangeInProgress;
            _repository = repository;
            RefreshCommand.NotifyCanExecuteChanged();
            OpenTerminalCommand.NotifyCanExecuteChanged();
            OpenFolderCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasUsableRepository));
            ClearItems();
            _remoteCheckoutWarning = string.Empty;
            if (repository == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchRepositoryRequired");
                return;
            }

            StatusMessage = _stringHelper.GetString("ReferenceLoading");
        }

        public void InvalidatePendingRequests()
        {
            _snapshotPresenter.Invalidate();
            IsBusy = _branchChangeInProgress;
        }

        public async Task RefreshAsync()
        {
            await RefreshCoreAsync();
        }

        private async Task<bool> RefreshCoreAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return false;
            }

            IsBusy = true;
            StatusMessage = _stringHelper.GetString("ReferenceLoading");
            ReferenceSnapshotResult result = await _snapshotPresenter.LoadAsync(repository);
            if (result.IsCurrent == false)
            {
                if (_snapshotPresenter.HasActiveLoad == false)
                {
                    IsBusy = _branchChangeInProgress;
                }
                return false;
            }
            if (_repository != repository)
            {
                return false;
            }
            try
            {
                if (result.Error != null)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(result.Error);
                    return false;
                }
                if (result.Snapshot == null)
                {
                    return false;
                }
                GitReferenceSnapshot snapshot = result.Snapshot;

                string selectedKey = SelectedTreeNode?.Key;
                ClearItems();
                CurrentBranch = snapshot.CurrentBranch;
                foreach (GitLocalBranch branch in snapshot.Branches) { _branches.Add(branch); }
                foreach (GitRemote remote in snapshot.Remotes) { _remotes.Add(remote); }
                foreach (GitTag tag in snapshot.Tags) { _tags.Add(tag); }
                foreach (GitSubmodule submodule in snapshot.Submodules) { _submodules.Add(submodule); }
                NotifyEmptyStates();
                BuildTree(selectedKey);
                StatusMessage = string.Empty;
                return true;
            }
            finally
            {
                if (_snapshotPresenter.RequestVersion == result.RequestVersion)
                {
                    if (_branchChangeInProgress == false)
                    {
                        IsBusy = false;
                    }
                }
            }
        }

        public async Task SwitchBranchAsync(GitLocalBranch branch)
        {
            if (_repository == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchRepositoryRequired");
                BranchSwitchFailureMessage = StatusMessage;
                return;
            }
            if (branch == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchBranchRequired");
                BranchSwitchFailureMessage = StatusMessage;
                return;
            }
            await QueueBranchSwitchAsync(_repository, branch.Name,
                _stringHelper.Format("ReferenceSwitchSucceeded", branch.Name));
        }

        public async Task SwitchMenuBranchAsync(string repositoryRoot, GitLocalBranch branch)
        {
            if (_repository == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchRepositoryRequired");
                BranchSwitchFailureMessage = StatusMessage;
                return;
            }
            StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(_repository.RootPath, repositoryRoot, pathComparison) == false)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchMenuRepositoryChanged");
                BranchSwitchFailureMessage = StatusMessage;
                return;
            }
            if (branch == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchBranchRequired");
                BranchSwitchFailureMessage = StatusMessage;
                return;
            }
            await SwitchBranchAsync(branch);
        }

        private async Task<bool> QueueBranchSwitchAsync(GitRepository repository, string branchName, string successMessage)
        {
            GitOperationQueueState state = _branchPresenter.GetQueueState(repository.RootPath);
            if (state.IsRunning)
            {
                if (PendingBranchSwitchMessage.Length == 0)
                {
                    PendingBranchSwitchMessage = _stringHelper.Format("ReferenceSwitchPending", branchName, state.RunningOperationName);
                }
            }
            BranchSwitchFailureMessage = string.Empty;
            try
            {
                return await _branchPresenter.SwitchAsync(repository, branchName,
                    _stringHelper.Format("ReferenceSwitchOperationRunning", branchName), () =>
                    {
                        if (IsCurrentRepository(repository.RootPath))
                        {
                            _branchChangeInProgress = true;
                            _branchChangeTarget = branchName;
                            _snapshotPresenter.Invalidate();
                            IsBusy = true;
                            StatusMessage = _stringHelper.Format("ReferenceSwitchInProgress", branchName);
                        }
                    }, async updated =>
                    {
                        if (IsCurrentRepository(repository.RootPath) == false)
                        {
                            return true;
                        }

                        _repository = updated;
                        bool refreshed = await RefreshCoreAsync();
                        if (IsCurrentRepository(repository.RootPath) == false)
                        {
                            return true;
                        }
                        if (refreshed)
                        {
                            StatusMessage = successMessage;
                        }
                        else
                        {
                            BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchRefreshFailed", branchName, StatusMessage);
                            StatusMessage = BranchSwitchFailureMessage;
                        }
                        RepositoryChanged?.Invoke(updated);
                        return refreshed;
                    }, () =>
                    {
                        if (IsCurrentRepository(repository.RootPath))
                        {
                            _branchChangeInProgress = false;
                            _branchChangeTarget = null;
                            IsBusy = _snapshotPresenter.HasActiveLoad;
                        }
                    });
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(repository.RootPath))
                {
                    await RefreshCoreAsync();
                    BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchFailed", branchName, CurrentBranchDisplay, _errorLocalizer.GetDisplayMessage(exception));
                    StatusMessage = BranchSwitchFailureMessage;
                }
                return false;
            }
            finally
            {
                if (IsCurrentRepository(repository.RootPath))
                {
                    PendingBranchSwitchMessage = string.Empty;
                }
            }
        }

        private bool IsCurrentRepository(string repositoryRoot)
        {
            if (_repository == null)
            {
                return false;
            }
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(_repository.RootPath, repositoryRoot, comparison);
        }

        public bool IsRemoteDefaultBranch(GitRemoteBranch branch)
        {
            if (branch == null)
            {
                return false;
            }
            foreach (GitRemote remote in _remotes)
            {
                if (remote.Name != branch.RemoteName)
                {
                    continue;
                }
                return remote.DefaultBranch == branch.Name;
            }
            return false;
        }

        public async Task<bool> DeleteLocalBranchAsync(string repositoryRoot, GitLocalBranch branch)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            if (branch == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchBranchRequired");
                return false;
            }

            GitRepository repository = _repository;
            return await QueueBranchDeletionAsync(repository,
                _stringHelper.Format("ReferenceLocalBranchDeleted", branch.Name),
                applyResult => _mutationPresenter.DeleteLocalBranchAsync(repository, branch,
                    _stringHelper.Format("ReferenceDeletingBranch", branch.Name), applyResult));
        }

        public async Task<bool> DeleteRemoteBranchAsync(string repositoryRoot, GitRemoteBranch branch)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            if (branch == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceRemoteBranchRequired");
                return false;
            }

            GitRepository repository = _repository;
            return await QueueBranchDeletionAsync(repository,
                _stringHelper.Format("ReferenceRemoteBranchDeleted", branch.FullName),
                applyResult => _mutationPresenter.DeleteRemoteBranchAsync(repository, branch,
                    _stringHelper.Format("ReferenceDeletingBranch", branch.FullName), applyResult));
        }

        private async Task<bool> QueueBranchDeletionAsync(GitRepository repository, string successMessage,
            Func<Func<Task<bool>>, Task<bool>> execute)
        {
            try
            {
                return await execute(async () =>
                {
                    if (IsCurrentRepository(repository.RootPath) == false)
                    {
                        return true;
                    }

                    bool refreshed = await RefreshCoreAsync();
                    if (IsCurrentRepository(repository.RootPath) == false)
                    {
                        return true;
                    }
                    if (refreshed)
                    {
                        StatusMessage = successMessage;
                    }
                    else
                    {
                        StatusMessage = _stringHelper.Format("ReferenceBranchDeleteRefreshFailed", successMessage, StatusMessage);
                    }
                    RepositoryChanged?.Invoke(repository);
                    return true;
                });
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(repository.RootPath))
                {
                    await RefreshCoreAsync();
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
                return false;
            }
        }

        public async Task<GitRemoteTagDeletionPreview> GetRemoteTagDeletionPreviewAsync(string repositoryRoot, GitTag tag,
            string remoteName, CancellationToken cancellationToken = default)
        {
            if (_repository == null)
            {
                throw new GitException("ReferenceSwitchRepositoryRequired", null, Array.Empty<object>());
            }
            if (IsCurrentRepository(repositoryRoot) == false)
            {
                throw new GitException("ReferenceSwitchMenuRepositoryChanged", null, Array.Empty<object>());
            }
            GitRepository repository = _repository;
            GitRemoteTagDeletionPreview preview = await _mutationPresenter.GetRemoteTagDeletionPreviewAsync(repository, tag, remoteName, cancellationToken);
            if (_repository != repository)
            {
                throw new GitException("ReferenceSwitchMenuRepositoryChanged", null, Array.Empty<object>());
            }
            return preview;
        }

        public Task<bool> DeleteLocalTagAsync(string repositoryRoot, GitTag tag, string operationName, string successMessage)
        {
            return RunTagDeletionAsync(repositoryRoot, successMessage,
                (repository, applyResult) => _mutationPresenter.DeleteLocalTagAsync(repository, tag, operationName, applyResult));
        }

        public Task<bool> DeleteRemoteTagAsync(string repositoryRoot, GitRemoteTagDeletionPreview preview, string operationName, string successMessage)
        {
            return RunTagDeletionAsync(repositoryRoot, successMessage,
                (repository, applyResult) => _mutationPresenter.DeleteRemoteTagAsync(repository, preview, operationName, applyResult));
        }

        private async Task<bool> RunTagDeletionAsync(string repositoryRoot, string successMessage,
            Func<GitRepository, Func<Task<bool>>, Task<bool>> execute)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            GitRepository repository = _repository;
            try
            {
                return await execute(repository, async () =>
                {
                    if (IsCurrentRepository(repositoryRoot) == false)
                    {
                        return true;
                    }
                    bool refreshed = await RefreshCoreAsync();
                    if (IsCurrentRepository(repositoryRoot) == false)
                    {
                        return true;
                    }
                    if (refreshed)
                    {
                        StatusMessage = successMessage;
                    }
                    else
                    {
                        StatusMessage = _stringHelper.Format("TagDeleteRefreshFailed", successMessage, StatusMessage);
                    }
                    RepositoryChanged?.Invoke(_repository);
                    return refreshed;
                });
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(repositoryRoot))
                {
                    await RefreshCoreAsync();
                    if (IsCurrentRepository(repositoryRoot) == false)
                    {
                        return false;
                    }
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
                return false;
            }
        }

        public Task<bool> RenameLocalBranchAsync(GitReferenceRenameRequest request, string operationName,
            Func<GitReferenceRenameResult, string> describeResult, Func<string, string, string> describeRefreshFailure)
        {
            return RunReferenceRenameAsync(request, describeResult, describeRefreshFailure,
                (repository, applyResult) => _mutationPresenter.RenameLocalBranchAsync(repository, request, operationName, applyResult));
        }

        public Task<bool> RenameLocalTagAsync(GitReferenceRenameRequest request, string operationName,
            Func<GitReferenceRenameResult, string> describeResult, Func<string, string, string> describeRefreshFailure)
        {
            return RunReferenceRenameAsync(request, describeResult, describeRefreshFailure,
                (repository, applyResult) => _mutationPresenter.RenameLocalTagAsync(repository, request, operationName, applyResult));
        }

        private async Task<bool> RunReferenceRenameAsync(GitReferenceRenameRequest request,
            Func<GitReferenceRenameResult, string> describeResult, Func<string, string, string> describeRefreshFailure,
            Func<GitRepository, Func<GitReferenceRenameResult, Task<bool>>, Task<bool>> execute)
        {
            if (CanRunMenuAction(request.RepositoryRoot) == false)
            {
                return false;
            }
            GitRepository repository = _repository;
            try
            {
                return await execute(repository, async result =>
                {
                    if (IsCurrentRepository(request.RepositoryRoot) == false)
                    {
                        return true;
                    }
                    _repository = result.Repository;
                    bool refreshed = await RefreshCoreAsync();
                    if (IsCurrentRepository(request.RepositoryRoot) == false)
                    {
                        return true;
                    }
                    string message = describeResult(result);
                    if (refreshed)
                    {
                        StatusMessage = message;
                    }
                    else
                    {
                        StatusMessage = describeRefreshFailure(message, StatusMessage);
                    }
                    RepositoryChanged?.Invoke(result.Repository);
                    return refreshed;
                });
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(request.RepositoryRoot) == false)
                {
                    return false;
                }
                await RefreshCoreAsync();
                if (IsCurrentRepository(request.RepositoryRoot) == false)
                {
                    return false;
                }
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                return false;
            }
        }

        public async Task<bool> CreateFromBranchAsync(string repositoryRoot, GitLocalBranch branch, string newName)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            if (branch == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSourceBranchRequired");
                return false;
            }
            return await RunBranchChangeAsync(
                (requested, operationName, runAndApply) => _mutationPresenter.CreateFromBranchAsync(requested, branch, newName, operationName, runAndApply),
                _stringHelper.Format("ReferenceBranchCreated", newName), newName);
        }

        public async Task<bool> TrackMenuRemoteAsync(string repositoryRoot, GitRemoteBranch branch, string localName)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            if (branch == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceRemoteBranchRequired");
                return false;
            }
            GitRepository repository = _repository;
            int request = _snapshotPresenter.RequestVersion;
            try
            {
                IReadOnlyList<GitLocalBranch> localBranches = await _referenceService.GetLocalBranchesAsync(repository);
                if (request != _snapshotPresenter.RequestVersion)
                {
                    return false;
                }
                if (_repository != repository)
                {
                    return false;
                }
                foreach (GitLocalBranch localBranch in localBranches)
                {
                    if (localBranch.Name != localName)
                    {
                        continue;
                    }
                    StatusMessage = _stringHelper.Format("RemoteCheckoutExistingLocal", localName);
                    return false;
                }
            }
            catch (Exception exception)
            {
                if (request == _snapshotPresenter.RequestVersion)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
                return false;
            }
            return await RunBranchChangeAsync(
                (requested, operationName, runAndApply) => _mutationPresenter.TrackRemoteAsync(requested, branch, localName, operationName, runAndApply),
                _stringHelper.Format("ReferenceRemoteTrackingSwitched", branch.FullName, localName), localName);
        }

        public async Task<bool> PrepareRemoteBranchCheckoutAsync(string repositoryRoot, GitRemoteBranch branch)
        {
            _remoteCheckoutWarning = string.Empty;
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            if (branch == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceRemoteBranchRequired");
                return false;
            }
            BranchSwitchFailureMessage = string.Empty;
            GitRepository repository = _repository;
            int request = _snapshotPresenter.RequestVersion;
            using CancellationTokenSource cancellation = new();
            _snapshotPresenter.TrackExternal(cancellation);
            IsBusy = true;
            try
            {
                IReadOnlyList<GitLocalBranch> localBranches = await _referenceService.GetLocalBranchesAsync(repository, cancellation.Token);
                if (request != _snapshotPresenter.RequestVersion)
                {
                    return false;
                }
                if (_repository != repository)
                {
                    return false;
                }

                GitLocalBranch trackingBranch = null;
                int trackingCount = 0;
                bool nameExists = false;
                string trackingNames = string.Empty;
                string upstreamRemoteRef = $"refs/heads/{branch.Name}";
                foreach (GitLocalBranch localBranch in localBranches)
                {
                    if (localBranch.Name == branch.Name)
                    {
                        nameExists = true;
                    }
                    if (localBranch.UpstreamRemoteName != branch.RemoteName)
                    {
                        continue;
                    }
                    if (localBranch.UpstreamRemoteRef != upstreamRemoteRef)
                    {
                        continue;
                    }
                    trackingBranch = localBranch;
                    trackingCount++;
                    if (trackingNames.Length > 0)
                    {
                        trackingNames += ", ";
                    }
                    trackingNames += localBranch.Name;
                }
                if (trackingCount > 1)
                {
                    StatusMessage = _stringHelper.Format("RemoteCheckoutMultipleTracking", branch.FullName, trackingNames);
                    BranchSwitchFailureMessage = StatusMessage;
                    return false;
                }
                if (trackingBranch != null)
                {
                    bool canTreatAsCurrent = trackingBranch.IsCurrent;
                    GitOperationQueueState queueState = _branchPresenter.GetQueueState(repository.RootPath);
                    if (queueState.IsRunning)
                    {
                        canTreatAsCurrent = false;
                    }
                    if (queueState.PendingCount > 0)
                    {
                        canTreatAsCurrent = false;
                    }
                    if (canTreatAsCurrent)
                    {
                        StatusMessage = _stringHelper.Format("RemoteCheckoutAlreadyCurrent", trackingBranch.Name);
                        return false;
                    }
                    await QueueBranchSwitchAsync(repository, trackingBranch.Name,
                        _stringHelper.Format("ReferenceRemoteTrackingSwitched", branch.FullName, trackingBranch.Name));
                    return false;
                }
                if (nameExists)
                {
                    _remoteCheckoutWarning = _stringHelper.Format("RemoteCheckoutExistingLocal", branch.Name);
                }
                return true;
            }
            catch (Exception exception)
            {
                if (request == _snapshotPresenter.RequestVersion)
                {
                    StatusMessage = _stringHelper.Format("ReferenceRemoteCheckoutFailed", branch.FullName, CurrentBranchDisplay, _errorLocalizer.GetDisplayMessage(exception));
                    BranchSwitchFailureMessage = StatusMessage;
                }
                return false;
            }
            finally
            {
                _snapshotPresenter.ReleaseExternal(cancellation);
                if (request == _snapshotPresenter.RequestVersion)
                {
                    IsBusy = false;
                }
            }
        }

        private bool CanRunMenuAction(string repositoryRoot)
        {
            if (_repository == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchRepositoryRequired");
                BranchSwitchFailureMessage = StatusMessage;
                return false;
            }
            StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(_repository.RootPath, repositoryRoot, pathComparison) == false)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchMenuRepositoryChanged");
                BranchSwitchFailureMessage = StatusMessage;
                return false;
            }
            return true;
        }

        public void OpenPullRequest(string repositoryRoot, GitLocalBranch localBranch, GitRemoteBranch remoteBranch)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return;
            }
            try
            {
                GitRemote remote;
                string sourceBranch;
                if (remoteBranch != null)
                {
                    remote = _remotes.FirstOrDefault(item => item.Name == remoteBranch.RemoteName);
                    sourceBranch = remoteBranch.Name;
                }
                else
                {
                    ResolveLocalPullRequestSource(localBranch, out remote, out sourceBranch);
                }
                if (remote == null)
                {
                    throw new GitException("PullRequestRemoteMissing", null, Array.Empty<object>());
                }
                _pullRequestLauncher.Open(remote, sourceBranch);
                StatusMessage = _stringHelper.Format("PullRequestPageOpened", sourceBranch);
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private void ResolveLocalPullRequestSource(GitLocalBranch branch, out GitRemote remote, out string sourceBranch)
        {
            if (branch == null)
            {
                throw new GitException("ReferenceSourceBranchRequired", null, Array.Empty<object>());
            }
            remote = null;
            sourceBranch = branch.Name;
            if (branch.UpstreamRemoteName.Length > 0 && branch.UpstreamRemoteRef.StartsWith("refs/heads/", StringComparison.Ordinal) == true)
            {
                remote = _remotes.FirstOrDefault(item => item.Name == branch.UpstreamRemoteName);
                string upstreamBranch = branch.UpstreamRemoteRef["refs/heads/".Length..];
                sourceBranch = upstreamBranch;
                if (remote?.Branches.Any(item => item.Name == upstreamBranch) == true)
                {
                    return;
                }
                throw new GitException("PullRequestPushRequired", null, branch.Name);
            }

            List<GitRemote> candidates = _remotes.Where(item => item.Branches.Any(remoteBranch => remoteBranch.Name == branch.Name && remoteBranch.CommitHash == branch.CommitHash)).ToList();
            if (candidates.Count == 1)
            {
                remote = candidates[0];
                return;
            }
            GitRemote origin = candidates.FirstOrDefault(item => item.Name == "origin");
            if (origin != null)
            {
                remote = origin;
                return;
            }
            if (candidates.Count > 1)
            {
                throw new GitException("PullRequestRemoteAmbiguous", null, branch.Name);
            }
            throw new GitException("PullRequestPushRequired", null, branch.Name);
        }

        public void SelectTag(GitTag tag)
        {
            if (tag == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(tag.CommitHash) == true)
            {
                StatusMessage = _stringHelper.Format("ReferenceTagNoCommit", tag.Name);
                return;
            }

            TagCommitSelected?.Invoke(tag.CommitHash);
            StatusMessage = _stringHelper.Format("ReferenceTagNavigate", tag.Name);
        }

        public void OpenStashes()
        {
            StashesRequested?.Invoke();
        }

        public void SelectStash(GitStashEntry stash)
        {
            if (stash != null)
            {
                StashSelected?.Invoke(stash.CommitHash);
            }
        }

        public async Task<bool> CreateBranchFromSectionAsync(string repositoryRoot, string name)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            return await RunBranchChangeAsync(
                (requested, operationName, runAndApply) => _mutationPresenter.CreateBranchAsync(requested, name, operationName, runAndApply),
                _stringHelper.Format("ReferenceBranchCreatedAndSwitched", name), name);
        }

        public async Task<bool> CreateTagFromSectionAsync(string repositoryRoot, string name)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }

            GitRepository repository = _repository;
            try
            {
                string operationName = $"{_stringHelper.GetString("ReferenceCreateTag")} {name}";
                return await _mutationPresenter.CreateTagAsync(repository, name, operationName,
                    () => ApplyTagResultAsync(repository, name));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(repository.RootPath))
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
                return false;
            }
        }

        private async Task<bool> ApplyTagResultAsync(GitRepository repository, string name)
        {
            if (_repository == null)
            {
                return true;
            }
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(_repository.RootPath, repository.RootPath, comparison) == false)
            {
                return true;
            }

            bool refreshed = await RefreshCoreAsync();
            string success = _stringHelper.Format("ReferenceLocalTagCreated", name.Trim());
            if (refreshed)
            {
                StatusMessage = success;
            }
            else
            {
                StatusMessage = _stringHelper.Format("ReferenceTagRefreshFailed", success, StatusMessage);
            }
            RepositoryChanged?.Invoke(repository);
            return true;
        }

        private async Task<bool> RunBranchChangeAsync(Func<GitRepository, string, ReferenceBranchRunner, Task<bool>> execute,
            string successMessage, string targetBranch)
        {
            GitRepository requestedRepository = _repository;
            if (requestedRepository == null)
            {
                StatusMessage = _stringHelper.GetString("ReferenceSwitchRepositoryRequired");
                BranchSwitchFailureMessage = StatusMessage;
                return false;
            }
            try
            {
                string operationName = _stringHelper.Format("ReferenceSwitchOperationRunning", targetBranch);
                return await execute(requestedRepository, operationName,
                    action => RunBranchChangeCoreAsync(action, successMessage, targetBranch, requestedRepository));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(requestedRepository.RootPath))
                {
                    BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchFailed", targetBranch, CurrentBranchDisplay, _errorLocalizer.GetDisplayMessage(exception));
                    StatusMessage = BranchSwitchFailureMessage;
                }
                return false;
            }
        }

        private async Task<bool> RunBranchChangeCoreAsync(Func<Task<GitRepository>> action, string successMessage, string targetBranch, GitRepository original)
        {
            if (IsCurrentRepository(original.RootPath) == false)
            {
                await action();
                return true;
            }
            if (_branchChangeInProgress)
            {
                StatusMessage = _stringHelper.Format("ReferenceSwitchOperationBusy", targetBranch, CurrentBranchDisplay);
                BranchSwitchFailureMessage = StatusMessage;
                return false;
            }

            StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            int operation = ++_branchChangeVersion;
            _branchChangeInProgress = true;
            _branchChangeTarget = targetBranch;
            _snapshotPresenter.Invalidate();
            IsBusy = true;
            BranchSwitchFailureMessage = string.Empty;
            StatusMessage = _stringHelper.Format("ReferenceSwitchOperationRunning", targetBranch);
            try
            {
                GitRepository updated = await action();
                if (operation != _branchChangeVersion)
                {
                    return false;
                }
                if (_repository == null)
                {
                    return false;
                }
                if (string.Equals(_repository.RootPath, original.RootPath, pathComparison) == false)
                {
                    return false;
                }
                _repository = updated;
                bool refreshed = await RefreshCoreAsync();
                if (operation != _branchChangeVersion)
                {
                    return false;
                }
                if (_repository == null)
                {
                    return false;
                }
                if (string.Equals(_repository.RootPath, original.RootPath, pathComparison) == false)
                {
                    return false;
                }
                if (refreshed == true)
                {
                    BranchSwitchFailureMessage = string.Empty;
                    StatusMessage = successMessage;
                }
                else
                {
                    BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchRefreshFailed", targetBranch, StatusMessage);
                    StatusMessage = BranchSwitchFailureMessage;
                }
                RepositoryChanged?.Invoke(updated);
                return true;
            }
            catch (Exception exception)
            {
                if (operation != _branchChangeVersion)
                {
                    return false;
                }
                if (_repository == null)
                {
                    return false;
                }
                if (string.Equals(_repository.RootPath, original.RootPath, pathComparison) == false)
                {
                    return false;
                }
                bool refreshed = await RefreshCoreAsync();
                if (operation != _branchChangeVersion)
                {
                    return false;
                }
                if (_repository == null)
                {
                    return false;
                }
                if (string.Equals(_repository.RootPath, original.RootPath, pathComparison) == false)
                {
                    return false;
                }
                string currentBranch = CurrentBranchDisplay;
                if (refreshed == false)
                {
                    currentBranch = _stringHelper.GetString("ReferenceSwitchCurrentUnknown");
                }
                BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchFailed", targetBranch, currentBranch, _errorLocalizer.GetDisplayMessage(exception));
                StatusMessage = BranchSwitchFailureMessage;
                return false;
            }
            finally
            {
                if (operation == _branchChangeVersion)
                {
                    _branchChangeInProgress = false;
                    _branchChangeTarget = null;
                    IsBusy = _snapshotPresenter.HasActiveLoad;
                }
            }
        }

        private void OpenTerminal()
        {
            try
            {
                _terminalLauncher.Open(_repository);
                StatusMessage = _stringHelper.Format("ReferenceTerminalOpened", _repository.RootPath);
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private void OpenFolder()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            if (IsBusy == true)
            {
                return;
            }
            try
            {
                _folderLauncher.Open(repository);
                StatusMessage = _stringHelper.Format("ReferenceFolderOpened", repository.RootPath);
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private void ClearItems()
        {
            _branches.Clear();
            _remotes.Clear();
            _tags.Clear();
            _submodules.Clear();
            _treeRoots.Clear();
            SelectedTreeNode = null;
            CurrentBranch = string.Empty;
            NotifyEmptyStates();
        }

        private void BuildTree(string selectedKey = null)
        {
            if (selectedKey == null)
            {
                selectedKey = SelectedTreeNode?.Key;
            }
            _treeRoots.Clear();
            SelectedTreeNode = null;
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            if (_expansionByRepository.TryGetValue(repository.RootPath, out Dictionary<string, bool> expanded) == false)
            {
                expanded = new Dictionary<string, bool>(StringComparer.Ordinal);
                _expansionByRepository.Add(repository.RootPath, expanded);
            }

            string branchesHeading = ReferenceText("ReferenceBranchesHeading");
            ReferenceTreeNode branches = CreateTreeNode(expanded, "branches", branchesHeading, "⎇", branchesHeading, ReferenceTreeNodeKind.Section, null, false, true, true, ReferenceText("ReferenceCreateBranch"));
            if (_branches.Count == 0)
            {
                string emptyText = ReferenceText("ReferenceNoBranches");
                branches.Children.Add(CreateTreeNode(expanded, "branches:empty", emptyText, string.Empty, emptyText, ReferenceTreeNodeKind.Empty, null, false, false, false));
            }
            string currentBranchText = ReferenceText("ReferenceCurrentBranch");
            foreach (GitLocalBranch branch in _branches)
            {
                string icon = "○";
                if (branch.IsCurrent == true)
                {
                    icon = "●";
                }
                branches.Children.Add(CreateTreeNode(expanded, "branch:" + branch.Name, branch.Name, icon, $"{branch.Name}\n{branch.CommitHash}", ReferenceTreeNodeKind.Branch, branch, branch.IsCurrent, false, false, null, currentBranchText));
            }
            _treeRoots.Add(branches);

            string tagsHeading = ReferenceText("ReferenceTagsHeading");
            ReferenceTreeNode tags = CreateTreeNode(expanded, "tags", tagsHeading, "◇", tagsHeading, ReferenceTreeNodeKind.Section, null, false, false, true, ReferenceText("ReferenceCreateTag"));
            if (_tags.Count == 0)
            {
                string emptyText = ReferenceText("ReferenceNoTags");
                tags.Children.Add(CreateTreeNode(expanded, "tags:empty", emptyText, string.Empty, emptyText, ReferenceTreeNodeKind.Empty, null, false, false, false));
            }
            foreach (GitTag tag in _tags)
            {
                tags.Children.Add(CreateTreeNode(expanded, "tag:" + tag.Name, tag.Name, string.Empty, $"{tag.Name}\n{tag.CommitHash}", ReferenceTreeNodeKind.Tag, tag, false, false, false));
            }
            _treeRoots.Add(tags);

            string remotesHeading = ReferenceText("ReferenceRemotesHeading");
            ReferenceTreeNode remotes = CreateTreeNode(expanded, "remotes", remotesHeading, "☁", remotesHeading, ReferenceTreeNodeKind.Section, null, false, false, true);
            if (_remotes.Count == 0)
            {
                string emptyText = ReferenceText("ReferenceNoRemotes");
                remotes.Children.Add(CreateTreeNode(expanded, "remotes:empty", emptyText, string.Empty, emptyText, ReferenceTreeNodeKind.Empty, null, false, false, false));
            }
            foreach (GitRemote remote in _remotes)
            {
                ReferenceTreeNode remoteNode = CreateTreeNode(expanded, "remote:" + remote.Name, remote.Name, "⌁", $"{remote.Name}\n{remote.Url}", ReferenceTreeNodeKind.Remote, remote, false, false, true);
                if (remote.Branches.Count == 0)
                {
                    remoteNode.Children.Add(CreateTreeNode(expanded, "remote:empty:" + remote.Name, ReferenceText("ReferenceNoTrackingBranches"), string.Empty, remote.Name, ReferenceTreeNodeKind.Empty, null, false, false, false));
                }
                foreach (GitRemoteBranch branch in remote.Branches)
                {
                    remoteNode.Children.Add(CreateTreeNode(expanded, "remote-branch:" + branch.FullName, branch.Name, "○", $"{branch.FullName}\n{branch.CommitHash}", ReferenceTreeNodeKind.RemoteBranch, branch, false, false, false));
                }
                remotes.Children.Add(remoteNode);
            }
            _treeRoots.Add(remotes);

            string stashesHeading = ReferenceText("StashesHeading");
            ReferenceTreeNode stashes = CreateTreeNode(expanded, "stashes", stashesHeading, "▣", stashesHeading, ReferenceTreeNodeKind.Section, null, false, false, true);
            if (Stashes.Count == 0)
            {
                string emptyText = ReferenceText("NoStashes");
                stashes.Children.Add(CreateTreeNode(expanded, "stashes:empty", emptyText, string.Empty, emptyText, ReferenceTreeNodeKind.Empty, null, false, false, false));
            }
            foreach (GitStashEntry stash in Stashes)
            {
                stashes.Children.Add(CreateTreeNode(expanded, "stash:" + stash.CommitHash, stash.DisplayText, string.Empty, $"{stash.DisplayText}\n{stash.CommitHash}", ReferenceTreeNodeKind.Stash, stash, false, false, false));
            }
            _treeRoots.Add(stashes);

            if (_submodules.Count > 0)
            {
                string submodulesHeading = ReferenceText("ReferenceSubmodulesHeading");
                ReferenceTreeNode submodules = CreateTreeNode(expanded, "submodules", submodulesHeading, "▧", submodulesHeading, ReferenceTreeNodeKind.Section, null, false, false, true);
                foreach (GitSubmodule submodule in _submodules)
                {
                    string state = _stringHelper.GetString(submodule.State);
                    string tip = _stringHelper.Format("ReferenceSubmoduleTooltip", submodule.Path, state, submodule.Url, submodule.ExpectedCommit, submodule.CheckedOutCommit);
                    submodules.Children.Add(CreateTreeNode(expanded, "submodule:" + submodule.Path, submodule.Path, string.Empty, tip, ReferenceTreeNodeKind.Submodule, submodule, false, false, false));
                }
                _treeRoots.Add(submodules);
            }

            if (selectedKey != null)
            {
                foreach (ReferenceTreeNode section in _treeRoots)
                {
                    ReferenceTreeNode match = FindTreeNode(section, selectedKey);
                    if (match != null)
                    {
                        SelectedTreeNode = match;
                        break;
                    }
                }
            }
        }

        private static ReferenceTreeNode CreateTreeNode(Dictionary<string, bool> expanded, string key, string label, string icon, string toolTipText, ReferenceTreeNodeKind kind, object target, bool isCurrent, bool isBranchSection, bool defaultExpanded, string actionText = null, string currentBranchText = null)
        {
            bool isExpanded = defaultExpanded;
            if (expanded.TryGetValue(key, out bool saved) == true)
            {
                isExpanded = saved;
            }
            return new ReferenceTreeNode(key, label, icon, toolTipText, kind, target, isCurrent, isBranchSection, isExpanded,
                delegate(ReferenceTreeNode node, bool value) { expanded[node.Key] = value; }, actionText, currentBranchText);
        }

        private static ReferenceTreeNode FindTreeNode(ReferenceTreeNode parent, string key)
        {
            if (parent.Key == key)
            {
                return parent;
            }
            foreach (ReferenceTreeNode child in parent.Children)
            {
                ReferenceTreeNode match = FindTreeNode(child, key);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }

        private void NotifyEmptyStates()
        {
            OnPropertyChanged(nameof(HasBranches));
            OnPropertyChanged(nameof(HasNoBranches));
            OnPropertyChanged(nameof(HasRemotes));
            OnPropertyChanged(nameof(HasNoRemotes));
            OnPropertyChanged(nameof(HasTags));
            OnPropertyChanged(nameof(HasNoTags));
            OnPropertyChanged(nameof(HasStashes));
            OnPropertyChanged(nameof(HasNoStashes));
            OnPropertyChanged(nameof(HasSubmodules));
        }

        private bool CanUseRepository()
        {
            if (_repository == null)
            {
                return false;
            }
            if (IsBusy)
            {
                return false;
            }
            return true;
        }

    }
}
