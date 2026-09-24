using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.Core.Git;

namespace Bough.App.ViewModels
{
    public class ReferenceExplorerViewModel : ViewModelBase
    {
        private readonly GitReferenceService _referenceService;
        private readonly GitCommitActionService _actionService;
        private readonly TerminalLauncher _terminalLauncher;
        private readonly RepositoryFolderLauncher _folderLauncher;
        private readonly StringHelper _stringHelper;
        private readonly GitOperationQueue _operationQueue;
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
        private int _requestVersion;
        private int _branchChangeVersion;
        private bool _branchChangeInProgress;
        private string _branchChangeTarget;
        private CancellationTokenSource _loadCancellation;

        public ReferenceExplorerViewModel(GitReferenceService referenceService, GitCommitActionService actionService, TerminalLauncher terminalLauncher, RepositoryFolderLauncher folderLauncher, StashViewModel stashViewModel, StringHelper stringHelper, GitOperationQueue operationQueue)
        {
            _referenceService = referenceService;
            _actionService = actionService;
            _terminalLauncher = terminalLauncher;
            _folderLauncher = folderLauncher;
            _stringHelper = stringHelper;
            _operationQueue = operationQueue ?? throw new ArgumentNullException(nameof(operationQueue));
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
            private set { SetProperty(ref _currentBranch, value); }
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
            InvalidatePendingRequests();
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

            StatusMessage = "참조 목록을 불러오는 중입니다.";
        }

        public void InvalidatePendingRequests()
        {
            _requestVersion++;
            _loadCancellation?.Cancel();
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

            _loadCancellation?.Cancel();
            using CancellationTokenSource cancellation = new();
            _loadCancellation = cancellation;
            int request = ++_requestVersion;
            IsBusy = true;
            StatusMessage = "참조 목록을 불러오는 중입니다.";
            try
            {
                GitReferenceSnapshot snapshot = await _referenceService.GetSnapshotAsync(repository, cancellation.Token);
                if (request != _requestVersion)
                {
                    return false;
                }
                if (_repository != repository)
                {
                    return false;
                }

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
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    StatusMessage = exception.Message;
                }
                return false;
            }
            finally
            {
                if (_loadCancellation == cancellation)
                {
                    _loadCancellation = null;
                }
                if (request == _requestVersion)
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
            GitOperationQueueState state = _operationQueue.GetState(repository.RootPath);
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
                return await _operationQueue.EnqueueAsync(repository.RootPath,
                    _stringHelper.Format("ReferenceSwitchOperationRunning", branchName), async token =>
                {
                    bool shownRepository = IsCurrentRepository(repository.RootPath);
                    if (shownRepository)
                    {
                        _branchChangeInProgress = true;
                        _branchChangeTarget = branchName;
                        _loadCancellation?.Cancel();
                        _requestVersion++;
                        IsBusy = true;
                        StatusMessage = _stringHelper.Format("ReferenceSwitchInProgress", branchName);
                    }
                    try
                    {
                        GitRepository updated = await _referenceService.SwitchBranchAsync(repository, branchName, token);
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
                    }
                    finally
                    {
                        if (IsCurrentRepository(repository.RootPath))
                        {
                            _branchChangeInProgress = false;
                            _branchChangeTarget = null;
                            IsBusy = _loadCancellation != null;
                        }
                    }
                });
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(repository.RootPath))
                {
                    await RefreshCoreAsync();
                    BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchFailed", branchName, CurrentBranch, exception.Message);
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

        public async Task<bool> CreateFromBranchAsync(string repositoryRoot, GitLocalBranch branch, string newName)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            if (branch == null)
            {
                StatusMessage = "시작 브랜치가 선택되지 않았습니다.";
                return false;
            }
            GitRepository repository = _repository;
            return await RunBranchChangeAsync(() => _actionService.CreateFromBranchAsync(repository, branch.Name, branch.CommitHash, newName, true), $"{newName} 브랜치를 만들었습니다.", newName);
        }

        public async Task<bool> TrackMenuRemoteAsync(string repositoryRoot, GitRemoteBranch branch, string localName)
        {
            if (CanRunMenuAction(repositoryRoot) == false)
            {
                return false;
            }
            if (branch == null)
            {
                StatusMessage = "원격 브랜치가 선택되지 않았습니다.";
                return false;
            }
            GitRepository repository = _repository;
            int request = _requestVersion;
            try
            {
                IReadOnlyList<GitLocalBranch> localBranches = await _referenceService.GetLocalBranchesAsync(repository);
                if (request != _requestVersion)
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
                if (request == _requestVersion)
                {
                    StatusMessage = exception.Message;
                }
                return false;
            }
            return await RunBranchChangeAsync(() => _actionService.TrackRemoteAsync(repository, branch, localName), $"{branch.FullName}을 추적하는 {localName} 브랜치로 전환했습니다.", localName);
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
                StatusMessage = "원격 브랜치가 선택되지 않았습니다.";
                return false;
            }
            BranchSwitchFailureMessage = string.Empty;
            GitRepository repository = _repository;
            int request = _requestVersion;
            using CancellationTokenSource cancellation = new();
            _loadCancellation = cancellation;
            IsBusy = true;
            try
            {
                IReadOnlyList<GitLocalBranch> localBranches = await _referenceService.GetLocalBranchesAsync(repository, cancellation.Token);
                if (request != _requestVersion)
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
                    if (_operationQueue != null)
                    {
                        GitOperationQueueState queueState = _operationQueue.GetState(repository.RootPath);
                        if (queueState.IsRunning)
                        {
                            canTreatAsCurrent = false;
                        }
                        if (queueState.PendingCount > 0)
                        {
                            canTreatAsCurrent = false;
                        }
                    }
                    if (canTreatAsCurrent)
                    {
                        StatusMessage = _stringHelper.Format("RemoteCheckoutAlreadyCurrent", trackingBranch.Name);
                        return false;
                    }
                    await QueueBranchSwitchAsync(repository, trackingBranch.Name,
                        $"{branch.FullName}을 추적하는 {trackingBranch.Name} 브랜치로 전환했습니다.");
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
                if (request == _requestVersion)
                {
                    StatusMessage = $"{branch.FullName} 전환을 준비하지 못했습니다. 현재 브랜치: {CurrentBranch}. 이유: {exception.Message}";
                    BranchSwitchFailureMessage = StatusMessage;
                }
                return false;
            }
            finally
            {
                if (_loadCancellation == cancellation)
                {
                    _loadCancellation = null;
                }
                if (request == _requestVersion)
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

        public void SelectTag(GitTag tag)
        {
            if (tag == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(tag.CommitHash) == true)
            {
                StatusMessage = $"{tag.Name} 태그는 커밋을 가리키지 않습니다.";
                return;
            }

            TagCommitSelected?.Invoke(tag.CommitHash);
            StatusMessage = $"{tag.Name} 태그의 커밋으로 이동합니다.";
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
            GitRepository repository = _repository;
            return await RunBranchChangeAsync(() => _referenceService.CreateBranchAsync(repository, name, "HEAD"), $"{name} 브랜치를 만들고 전환했습니다.", name);
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
                return await _operationQueue.EnqueueAsync(repository.RootPath, $"{_stringHelper.GetString("ReferenceCreateTag")} {name}", async token =>
                {
                    token.ThrowIfCancellationRequested();
                    return await CreateTagCoreAsync(repository, name);
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
                    StatusMessage = exception.Message;
                }
                return false;
            }
        }

        private async Task<bool> CreateTagCoreAsync(GitRepository repository, string name)
        {
            try
            {
                await _referenceService.CreateLightweightTagAsync(repository, name, "HEAD");
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(repository.RootPath))
                {
                    StatusMessage = exception.Message;
                }
                return false;
            }

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
                StatusMessage = $"{success} 참조 목록은 다시 새로 고쳐 주세요. {StatusMessage}";
            }
            RepositoryChanged?.Invoke(repository);
            return true;
        }

        private async Task<bool> RunBranchChangeAsync(Func<Task<GitRepository>> action, string successMessage, string targetBranch)
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
                return await _operationQueue.EnqueueAsync(requestedRepository.RootPath, _stringHelper.Format("ReferenceSwitchOperationRunning", targetBranch), async token =>
                {
                    token.ThrowIfCancellationRequested();
                    return await RunBranchChangeCoreAsync(action, successMessage, targetBranch, requestedRepository);
                });
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(requestedRepository.RootPath))
                {
                    BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchFailed", targetBranch, CurrentBranch, exception.Message);
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
                StatusMessage = _stringHelper.Format("ReferenceSwitchOperationBusy", targetBranch, CurrentBranch);
                BranchSwitchFailureMessage = StatusMessage;
                return false;
            }

            StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            int operation = ++_branchChangeVersion;
            _branchChangeInProgress = true;
            _branchChangeTarget = targetBranch;
            _loadCancellation?.Cancel();
            _requestVersion++;
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
                if (updated.CurrentBranch != targetBranch)
                {
                    throw new GitException(_stringHelper.Format("ReferenceSwitchUnexpectedCurrent", targetBranch, updated.CurrentBranch));
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
                string currentBranch = CurrentBranch;
                if (refreshed == false)
                {
                    currentBranch = _stringHelper.GetString("ReferenceSwitchCurrentUnknown");
                }
                BranchSwitchFailureMessage = _stringHelper.Format("ReferenceSwitchFailed", targetBranch, currentBranch, exception.Message);
                StatusMessage = BranchSwitchFailureMessage;
                return false;
            }
            finally
            {
                if (operation == _branchChangeVersion)
                {
                    _branchChangeInProgress = false;
                    _branchChangeTarget = null;
                    IsBusy = _loadCancellation != null;
                }
            }
        }

        private void OpenTerminal()
        {
            try
            {
                _terminalLauncher.Open(_repository);
                StatusMessage = $"{_repository.RootPath}에서 터미널을 열었습니다.";
            }
            catch (Exception exception)
            {
                StatusMessage = exception.Message;
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
                StatusMessage = $"{repository.RootPath} 폴더를 열었습니다.";
            }
            catch (Exception exception)
            {
                StatusMessage = exception.Message;
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
                    string tip = $"{submodule.Path}\n{submodule.State}\n{submodule.Url}\nExpected: {submodule.ExpectedCommit}\nChecked out: {submodule.CheckedOutCommit}";
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
