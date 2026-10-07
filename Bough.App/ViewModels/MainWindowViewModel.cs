using Bough.App.Commands;
using Bough.App.Composition;
using Bough.App.Interfaces;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.Core.Git;
using Dignus.DependencyInjection.Attributes;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;
using Bough.Core.Internals;
using Bough.App.Internals;

namespace Bough.App.ViewModels
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class MainWindowViewModel : ViewModelBase, IStashMutationCompletion, IConflictStageCompletion
    {
        private readonly GitRepositoryService _repositoryService;
        private readonly GitOperationQueue _operationQueue;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly MainWindowRemoteCompletionPresenter _remoteCompletionPresenter;
        private readonly HistoryActionPresenter _revertPresenter;
        private readonly StringComparer _pathComparer;
        private readonly Dictionary<string, string> _autoOpenedConflicts;
        private readonly Dictionary<string, int> _historyReferenceVersions;
        private GitRepository _repository;
        private string _repositoryName;
        private string _repositoryMeta;
        private string _statusMessage;
        private string _mainStatusMessage;
        private string _visibleGitSettingsStatusMessage = string.Empty;
        private string _visibleLocalChangesErrorText = string.Empty;
        private string _visibleStashErrorText = string.Empty;
        private string _visibleStashStatusText = string.Empty;
        private int _gitSettingsStatusViewVersion;
        private int _localChangesStatusViewVersion;
        private int _stashStatusViewVersion;
        private string _runningGitOperationName = string.Empty;
        private int _pendingGitOperationCount;
        private bool _hasRepository;
        private bool _isBusy;
        private bool _isHistoryView;
        private bool _isLocalChangesView;
        private bool _isStashView;
        private bool _isGitSettingsView;
        private bool _isLocalChangesLoading;
        private bool _isReferencesLoading;
        private bool _isRemoteLoading;
        private bool _isRebaseInProgress;
        private GitRevertState _revertState;
        private bool _isRevertStateLoading;
        private int _revertReadVersion;
        private int _repositoryRequestVersion;
        private int _historyReferenceChangeVersion;
        private int _tagCommitSelectionRequestVersion;
        private CancellationTokenSource _repositoryOpenCancellation;

        public MainWindowViewModel(GitRepositoryService repositoryService, GitCommitActionService actionService, GitOperationQueue operationQueue, MainWindowChildren children, ConflictResolutionViewModel conflicts, RepositoryListViewModel repositoryList, StringHelper stringHelper, GitErrorLocalizer errorLocalizer)
        {
            _repositoryService = repositoryService;
            _operationQueue = operationQueue;
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            _remoteCompletionPresenter = new MainWindowRemoteCompletionPresenter(this);
            _revertPresenter = new HistoryActionPresenter(actionService, operationQueue);
            if (OperatingSystem.IsWindows() == true)
            {
                _pathComparer = StringComparer.OrdinalIgnoreCase;
            }
            else
            {
                _pathComparer = StringComparer.Ordinal;
            }
            _autoOpenedConflicts = new Dictionary<string, string>(_pathComparer);
            _historyReferenceVersions = new Dictionary<string, int>(_pathComparer);
            History = children.History;
            LocalChanges = children.LocalChanges;
            References = children.References;
            RemoteOperations = children.RemoteOperations;
            GitSettings = children.GitSettings;
            Conflicts = conflicts;
            RepositoryList = repositoryList;
            Conflicts.SetCompletion(this);
            Conflicts.PropertyChanged += OnConflictsPropertyChanged;
            _operationQueue.StateChanged += OnGitOperationQueueStateChanged;
            History.RepositoryChanged += OnHistoryRepositoryChanged;
            History.ReferenceRefreshRequired += OnReferenceRefreshRequired;
            History.ActionMessage += message => StatusMessage = message;
            History.FileRestored += OnHistoryFileRestored;
            References.PropertyChanged += OnReferencesPropertyChanged;
            GitSettings.PropertyChanged += OnGitSettingsPropertyChanged;
            LocalChanges.PropertyChanged += OnLocalChangesPropertyChanged;
            LocalChanges.Stashes.PropertyChanged += OnStashPropertyChanged;
            References.RepositoryChanged += OnReferenceRepositoryChanged;
            References.ReferenceRefreshRequired += OnReferenceRefreshRequired;
            References.TagCommitSelected += OnTagCommitSelected;
            References.StashesRequested += OnStashesRequested;
            References.StashSelected += OnStashSelected;
            RemoteOperations.OperationCompleted += OnRemoteOperationCompleted;
            LocalChanges.Committed += OnLocalCommitted;
            LocalChanges.ResolveRequested += OnLocalResolveRequested;
            _repositoryName = _stringHelper.GetString("OpenRepositoryPrompt");
            _repositoryMeta = _stringHelper.GetString("RepositorySidebarHint");
            _statusMessage = string.Empty;
            _mainStatusMessage = string.Empty;
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
            ShowHistoryCommand = new AsyncRelayCommand(ShowHistoryAsync, CanNavigate);
            ShowLocalChangesCommand = new AsyncRelayCommand(ShowLocalChangesAsync, CanNavigate);
            ShowGitSettingsCommand = new AsyncRelayCommand(ShowGitSettingsAsync, CanShowGitSettings);
            ContinueRebaseCommand = new AsyncRelayCommand(ContinueRebaseAsync, CanContinueRebase);
            IsLocalChangesView = true;
        }

        public event Action<string> ConflictWindowRequested;
        public event Action ConflictResolutionCompleted;

        public ConflictResolutionViewModel Conflicts { get; }
        public override void RefreshLocalization()
        {
            RepositoryList.RefreshLocalization();
            if (HasRepository == false)
            {
                RepositoryName = _stringHelper.GetString("OpenRepositoryPrompt");
                RepositoryMeta = _stringHelper.GetString("RepositorySidebarHint");
            }
            base.RefreshLocalization();
        }
        public RepositoryListViewModel RepositoryList { get; }
        public HistoryViewModel History { get; }
        public ReferenceExplorerViewModel References { get; }
        public RemoteOperationsViewModel RemoteOperations { get; }
        public GitSettingsViewModel GitSettings { get; }
        public LocalChangesViewModel LocalChanges { get; }

        public string LocalChangesNavigationText { get { return _stringHelper.GetString("LocalChangesHeading"); } }
        public string HistoryNavigationText { get { return _stringHelper.GetString("MainHistoryNavigation"); } }
        public string SettingsNavigationText { get { return _stringHelper.GetString("SettingsTitle"); } }
        public string OpenFolderToolTipText { get { return _stringHelper.GetString("MainOpenFolderToolTip"); } }
        public string OpenFolderAutomationName { get { return _stringHelper.GetString("MainOpenFolderAutomationName"); } }
        public string OpenFolderActionText { get { return _stringHelper.GetString("MainOpenFolderAction"); } }
        public string OpenInText { get { return _stringHelper.GetString("MainOpenIn"); } }
        public string ConsoleToolTipText { get { return _stringHelper.GetString("MainConsoleToolTip"); } }
        public string ConsoleAutomationName { get { return _stringHelper.GetString("MainConsoleAutomationName"); } }
        public string ConsoleText { get { return _stringHelper.GetString("MainConsole"); } }
        public string RebaseProgressLabel { get { return _stringHelper.GetString("RebaseProgressLabel"); } }
        public string ContinueRebaseText { get { return _stringHelper.GetString("RebaseContinueAction"); } }
        public string RebaseStatusText
        {
            get
            {
                if (IsLocalChangesLoading)
                {
                    return _stringHelper.GetString("RebaseCheckingConflicts");
                }
                int conflictCount = Conflicts.ConflictFiles.Count;
                if (conflictCount > 0)
                {
                    return _stringHelper.Format("RebaseResolveBeforeContinue", conflictCount);
                }
                return _stringHelper.GetString("RebaseReadyToContinue");
            }
        }

        public GitRepository CurrentRepository { get { return _repository; } }
        public GitRevertState RevertState { get { return _revertState; } }
        public bool IsRevertInProgress
        {
            get
            {
                if (_revertState == null)
                {
                    return false;
                }
                return _revertState.IsInProgress;
            }
        }
        public string RevertProgressLabel { get { return _stringHelper.GetString("MainRevertProgressLabel"); } }
        public string ContinueRevertText { get { return _stringHelper.GetString("MainRevertContinueAction"); } }
        public string AbortRevertText { get { return _stringHelper.GetString("MainRevertAbortAction"); } }
        public string RevertStatusText
        {
            get
            {
                if (_isRevertStateLoading)
                {
                    return _stringHelper.GetString("MainRevertCheckingConflicts");
                }
                if (IsLocalChangesLoading)
                {
                    return _stringHelper.GetString("MainRevertCheckingConflicts");
                }
                int conflicts = Conflicts.ConflictFiles.Count;
                if (_revertState != null)
                {
                    conflicts = Math.Max(conflicts, _revertState.ConflictPaths.Count);
                }
                if (conflicts > 0)
                {
                    return _stringHelper.Format("MainRevertResolveBeforeContinue", conflicts);
                }
                if (Conflicts.HasUnsavedConflictEdits)
                {
                    return _stringHelper.GetString("MainRevertSaveDraftBeforeContinue");
                }
                return _stringHelper.GetString("MainRevertReadyToContinue");
            }
        }
        public bool CanContinueRevert
        {
            get
            {
                if (CanAbortRevert == false)
                {
                    return false;
                }
                if (IsLocalChangesLoading)
                {
                    return false;
                }
                if (Conflicts.ConflictFiles.Count > 0)
                {
                    return false;
                }
                if (_revertState.ConflictPaths.Count > 0)
                {
                    return false;
                }
                return Conflicts.HasUnsavedConflictEdits == false;
            }
        }
        public bool CanAbortRevert
        {
            get
            {
                if (HasRepository == false)
                {
                    return false;
                }
                if (IsRevertInProgress == false)
                {
                    return false;
                }
                if (_isRevertStateLoading)
                {
                    return false;
                }
                if (IsBusy)
                {
                    return false;
                }
                return Conflicts.IsBusy == false;
            }
        }

        public string CurrentRepositoryRoot { get { return _repository?.RootPath; } }
        public int RepositoryRequestVersion { get { return _repositoryRequestVersion; } }
        public bool IsRepositoryMutationInProgress
        {
            get
            {
                if (Conflicts.IsSaving == true)
                {
                    return true;
                }
                return RemoteOperations.IsBusy;
            }
        }

        public string GitOperationQueueStatusText
        {
            get
            {
                if (_pendingGitOperationCount == 0)
                {
                    return _runningGitOperationName;
                }
                if (_runningGitOperationName.Length == 0)
                {
                    return $"+{_pendingGitOperationCount}";
                }
                return $"{_runningGitOperationName} · +{_pendingGitOperationCount}";
            }
        }

        public bool HasGitOperationQueueStatus
        {
            get { return _runningGitOperationName.Length > 0 || _pendingGitOperationCount > 0; }
        }

        public bool IsGitOperationRunning
        {
            get { return _runningGitOperationName.Length > 0; }
        }

        public bool IsMainProgress
        {
            get
            {
                if (IsBusy == true)
                {
                    return true;
                }
                return IsGitOperationRunning;
            }
        }

        public string SidebarStatusText
        {
            get
            {
                List<string> messages = new();
                AddSidebarStatusMessage(messages, References.BranchSwitchFailureMessage);
                AddSidebarStatusMessage(messages, References.PendingBranchSwitchMessage);
                AddSidebarStatusMessage(messages, GitOperationQueueStatusText);
                AddSidebarStatusMessage(messages, MainStatusMessage);
                if (IsLocalChangesView == true)
                {
                    AddSidebarStatusMessage(messages, _visibleLocalChangesErrorText);
                }
                if (IsStashView == true)
                {
                    AddSidebarStatusMessage(messages, _visibleStashErrorText);
                    if (string.IsNullOrWhiteSpace(_visibleStashErrorText) == true)
                    {
                        AddSidebarStatusMessage(messages, _visibleStashStatusText);
                    }
                }
                if (IsGitSettingsView == true)
                {
                    AddSidebarStatusMessage(messages, _visibleGitSettingsStatusMessage);
                }
                AddSidebarStatusMessage(messages, References.StatusMessage);
                return string.Join(Environment.NewLine, messages);
            }
        }

        public bool HasSidebarStatusText { get { return SidebarStatusText.Length > 0; } }
        public bool HasSidebarStatus { get { return HasSidebarStatusText || IsMainProgress; } }

        public AsyncRelayCommand RefreshCommand { get; }

        public AsyncRelayCommand ShowHistoryCommand { get; }
        public AsyncRelayCommand ShowLocalChangesCommand { get; }
        public AsyncRelayCommand ShowGitSettingsCommand { get; }
        public AsyncRelayCommand ContinueRebaseCommand { get; }

        public bool IsRebaseInProgress
        {
            get { return _isRebaseInProgress; }
            private set
            {
                if (SetProperty(ref _isRebaseInProgress, value))
                {
                    NotifyRebaseState();
                }
            }
        }

        public bool IsHistoryView
        {
            get { return _isHistoryView; }
            private set
            {
                SetProperty(ref _isHistoryView, value);
            }
        }

        public bool IsLocalChangesView
        {
            get { return _isLocalChangesView; }
            private set
            {
                if (SetProperty(ref _isLocalChangesView, value) == false)
                {
                    return;
                }
                _localChangesStatusViewVersion++;
                _visibleLocalChangesErrorText = string.Empty;
                NotifySidebarStatusChanged();
            }
        }

        public bool IsStashView
        {
            get { return _isStashView; }
            private set
            {
                if (SetProperty(ref _isStashView, value) == false)
                {
                    return;
                }
                _stashStatusViewVersion++;
                _visibleStashErrorText = string.Empty;
                _visibleStashStatusText = string.Empty;
                NotifySidebarStatusChanged();
            }
        }

        public bool IsGitSettingsView
        {
            get { return _isGitSettingsView; }
            private set
            {
                if (SetProperty(ref _isGitSettingsView, value) == false)
                {
                    return;
                }
                _gitSettingsStatusViewVersion++;
                _visibleGitSettingsStatusMessage = string.Empty;
                OnPropertyChanged(nameof(IsRepositoryStartView));
                NotifySidebarStatusChanged();
            }
        }


        public string RepositoryName
        {
            get
            {
                return _repositoryName;
            }
            private set
            {
                SetProperty(ref _repositoryName, value);
            }
        }

        public string RepositoryMeta
        {
            get
            {
                return _repositoryMeta;
            }
            private set
            {
                SetProperty(ref _repositoryMeta, value);
            }
        }

        public string StatusMessage
        {
            get
            {
                return _statusMessage;
            }
            private set
            {
                SetStatusMessage(value, true);
            }
        }

        public string MainStatusMessage
        {
            get
            {
                return _mainStatusMessage;
            }
            private set
            {
                if (SetProperty(ref _mainStatusMessage, value) == true)
                {
                    NotifySidebarStatusChanged();
                }
            }
        }

        private void SetStatusMessage(string message, bool showInMainWindow)
        {
            SetProperty(ref _statusMessage, message);
            if (showInMainWindow == true)
            {
                MainStatusMessage = message;
                return;
            }
            MainStatusMessage = string.Empty;
        }

        public bool IsRepositoryStartView
        {
            get { return HasRepository == false && IsGitSettingsView == false; }
        }

        public bool HasRepository
        {
            get
            {
                return _hasRepository;
            }
            private set
            {
                if (SetProperty(ref _hasRepository, value) == true)
                {
                    OnPropertyChanged(nameof(IsRepositoryStartView));
                    NotifyCommandStates();
                }
            }
        }

        public bool IsLocalChangesLoading
        {
            get { return _isLocalChangesLoading; }
            private set
            {
                if (SetProperty(ref _isLocalChangesLoading, value))
                {
                    NotifyRebaseState();
                }
            }
        }

        public bool IsReferencesLoading
        {
            get { return _isReferencesLoading; }
            private set { SetProperty(ref _isReferencesLoading, value); }
        }

        public bool IsRemoteLoading
        {
            get { return _isRemoteLoading; }
            private set { SetProperty(ref _isRemoteLoading, value); }
        }

        public bool IsBusy
        {
            get
            {
                return _isBusy;
            }
            private set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    OnPropertyChanged(nameof(IsMainProgress));
                    NotifySidebarStatusChanged();
                    NotifyCommandStates();
                }
            }
        }

        public void RegisterClonedRepository(string path)
        {
            RepositoryList.AddRecent(path);
            try
            {
                RepositoryList.Save();
            }
            catch (IOException exception)
            {
                StatusMessage = _stringHelper.Format("RepositoryListSaveFailed", _errorLocalizer.GetDisplayMessage(exception));
            }
            catch (UnauthorizedAccessException exception)
            {
                StatusMessage = _stringHelper.Format("RepositoryListSaveFailed", _errorLocalizer.GetDisplayMessage(exception));
            }
        }

        public async Task OpenClonedRepositoryAsync(string path)
        {
            int request = _repositoryRequestVersion + 1;
            await OpenRepositoryAsync(path);
            if (_repositoryRequestVersion != request)
            {
                return;
            }
            if (_repository != null)
            {
                if (_pathComparer.Equals(_repository.RootPath, path))
                {
                    return;
                }
            }
            StatusMessage = _stringHelper.Format("CloneOpenFailed", StatusMessage);
        }

        public async Task OpenRepositoryAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                StatusMessage = _stringHelper.GetString("MainSelectRepositoryPath");
                return;
            }
            string requestedPath;
            try
            {
                requestedPath = Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                StatusMessage = _stringHelper.Format("MainInvalidRepositoryPath", path);
                return;
            }
            catch (NotSupportedException)
            {
                StatusMessage = _stringHelper.Format("MainInvalidRepositoryPath", path);
                return;
            }
            catch (PathTooLongException)
            {
                StatusMessage = _stringHelper.Format("MainInvalidRepositoryPath", path);
                return;
            }

            int request = ++_repositoryRequestVersion;
            _repositoryOpenCancellation?.Cancel();
            CancellationTokenSource cancellation = new();
            _repositoryOpenCancellation = cancellation;
            _repository = null;
            ClearGitOperationQueueState();
            Conflicts.BindRepository(null);
            IsRebaseInProgress = false;
            ClearRevertState();
            HasRepository = false;
            IsLocalChangesLoading = false;
            IsReferencesLoading = false;
            IsRemoteLoading = false;
            RepositoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(requestedPath));
            if (string.IsNullOrWhiteSpace(RepositoryName) == true)
            {
                RepositoryName = requestedPath;
            }
            RepositoryMeta = requestedPath;
            StatusMessage = $"{_stringHelper.GetString("OpenGitRepository")}… {requestedPath}";
            RepositoryList.SetActive(requestedPath);
            IsHistoryView = false;
            IsLocalChangesView = true;
            IsStashView = false;
            IsGitSettingsView = false;
            History.Clear();
            LocalChanges.Clear();
            await GitSettings.SetRepositoryAsync(null);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (_repository != null)
            {
                return;
            }
            References.BindRepository(null);
            RemoteOperations.BindRepository(null);
            IsBusy = true;

            GitRepository opened;
            try
            {
                opened = await _repositoryService.OpenAsync(requestedPath, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                if (request == _repositoryRequestVersion)
                {
                    RepositoryList.SetActive(null);
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
                return;
            }
            finally
            {
                if (ReferenceEquals(_repositoryOpenCancellation, cancellation) == true)
                {
                    _repositoryOpenCancellation = null;
                }
                cancellation.Dispose();
                if (request == _repositoryRequestVersion)
                {
                    IsBusy = false;
                }
            }

            if (request != _repositoryRequestVersion)
            {
                return;
            }

            _repository = opened;
            ApplyGitOperationQueueState(_operationQueue.GetState(opened.RootPath));
            Conflicts.BindRepository(opened);
            HasRepository = true;
            RepositoryName = opened.DisplayName;
            RepositoryMeta = FormatRepositoryMeta(opened);
            SetStatusMessage(opened.RootPath, false);
            RepositoryList.RememberOpened(opened.RootPath);
            StartRepositoryAreaLoads(opened, request, false);
            try
            {
                RepositoryList.Save();
            }
            catch (IOException exception)
            {
                StatusMessage = _stringHelper.Format("RepositoryListSaveFailed", _errorLocalizer.GetDisplayMessage(exception));
            }
            catch (UnauthorizedAccessException exception)
            {
                StatusMessage = _stringHelper.Format("RepositoryListSaveFailed", _errorLocalizer.GetDisplayMessage(exception));
            }
        }

        private void StartRepositoryAreaLoads(GitRepository repository, int request, bool loadHistory)
        {
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            References.BindRepository(repository);
            RemoteOperations.BindRepository(repository);
            IsLocalChangesLoading = true;
            IsReferencesLoading = true;
            IsRemoteLoading = true;
            _ = ObserveRepositoryAreaAsync(() => RefreshLocalChangesAndConflictsAsync(repository, request), request, () => IsLocalChangesLoading = false);
            _ = ObserveRepositoryAreaAsync(() => RefreshRebaseStateAsync(repository, request), request, null);
            _ = ObserveRepositoryAreaAsync(() => RefreshRevertStateAsync(repository, request), request, null);
            _ = ObserveRepositoryAreaAsync(References.RefreshAsync, request, () => IsReferencesLoading = false);
            _ = ObserveRepositoryAreaAsync(RemoteOperations.RefreshAsync, request, () => IsRemoteLoading = false);
            if (IsGitSettingsView == true)
            {
                _ = ObserveRepositoryAreaAsync(() => GitSettings.SetRepositoryAsync(repository), request, null);
            }
            if (loadHistory == true)
            {
                _ = ObserveRepositoryAreaAsync(() => LoadHistoryAsync(repository, request), request, null);
            }
        }

        internal async Task ObserveRepositoryAreaAsync(Func<Task> load, int request, Action completed)
        {
            GitRepository repository = _repository;
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            try
            {
                await load();
            }
            catch (Exception exception)
            {
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, repository) == false)
                {
                    return;
                }
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
            finally
            {
                if (request == _repositoryRequestVersion)
                {
                    if (ReferenceEquals(_repository, repository))
                    {
                        completed?.Invoke();
                    }
                }
            }
        }

        internal async Task RefreshLocalChangesAndConflictsAsync(GitRepository repository, int request)
        {
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            await LocalChanges.LoadWorktreeAsync(repository);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            await RefreshConflictsFromLocalChangesAsync(repository);
        }

        internal bool IsCurrentRepositoryRequest(GitRepository repository, int request)
        {
            if (request != _repositoryRequestVersion)
            {
                return false;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return false;
            }
            return true;
        }

        internal async Task RefreshRebaseStateAsync(GitRepository repository, int request)
        {
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            bool isRebaseInProgress = await _repositoryService.IsRebaseInProgressAsync(repository);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            IsRebaseInProgress = isRebaseInProgress;
        }

        public void RemoveRepository(RepositoryItem item)
        {
            if (IsBusy == true)
            {
                return;
            }

            if (RepositoryList.Remove(item) == false)
            {
                return;
            }

            if (_pathComparer.Equals(_repository?.RootPath, item.RootPath))
            {
                _repositoryRequestVersion++;
                _repositoryOpenCancellation?.Cancel();
                _repository = null;
                ClearGitOperationQueueState();
                Conflicts.BindRepository(null);
                IsRebaseInProgress = false;
                ClearRevertState();
                HasRepository = false;
                IsLocalChangesLoading = false;
                IsReferencesLoading = false;
                IsRemoteLoading = false;
                RepositoryName = _stringHelper.GetString("OpenRepositoryPrompt");
                RepositoryMeta = _stringHelper.GetString("RepositorySidebarHint");
                History.Clear();
                LocalChanges.Clear();
                _ = GitSettings.SetRepositoryAsync(null);
                References.BindRepository(null);
                RemoteOperations.BindRepository(null);
                IsHistoryView = false;
                IsLocalChangesView = true;
                IsStashView = false;
                IsGitSettingsView = false;
                SetStatusMessage(_stringHelper.GetString("RepositoryRemoved"), false);
            }

            try
            {
                RepositoryList.Save();
            }
            catch (IOException exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        public async Task RefreshAsync()
        {
            if (CanRefresh() == false)
            {
                return;
            }

            GitRepository previousRepository = _repository;
            int request = _repositoryRequestVersion;
            CancellationTokenSource cancellation = new();
            _repositoryOpenCancellation = cancellation;
            IsBusy = true;
            GitRepository repository;
            try
            {
                repository = await _repositoryService.OpenAsync(previousRepository.RootPath, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                if (request == _repositoryRequestVersion)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
                return;
            }
            finally
            {
                if (ReferenceEquals(_repositoryOpenCancellation, cancellation) == true)
                {
                    _repositoryOpenCancellation = null;
                }
                cancellation.Dispose();
                if (request == _repositoryRequestVersion)
                {
                    IsBusy = false;
                }
            }

            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, previousRepository) == false)
            {
                return;
            }
            if (_pathComparer.Equals(previousRepository.RootPath, repository.RootPath) == false)
            {
                return;
            }

            _repository = repository;
            Conflicts.BindRepository(repository);
            RepositoryMeta = FormatRepositoryMeta(repository);
            StartRepositoryAreaLoads(repository, request, IsHistoryView);
        }

        private void OnReferenceRefreshRequired(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
            {
                return;
            }
            _historyReferenceVersions[repositoryRoot] = ++_historyReferenceChangeVersion;
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            if (_pathComparer.Equals(repository.RootPath, repositoryRoot) == false)
            {
                return;
            }
            int request = _repositoryRequestVersion;
            // Revert can remain pending even when the mutation's repository read failed.
            // This reads its state only; it never adopts the original object or binds children.
            _ = ObserveRepositoryAreaAsync(() => RefreshRevertStateAsync(repository, request, true), request, null);
        }

        private async void OnHistoryRepositoryChanged(GitRepository updated)
        {
            await ApplyActionRepositoryAsync(updated, true, true, false);
        }

        private async void OnHistoryFileRestored(string repositoryRoot, string path)
        {
            if (_repository == null)
            {
                return;
            }
            if (_pathComparer.Equals(_repository.RootPath, repositoryRoot) == false)
            {
                return;
            }
            GitRepository requestRepository = _repository;
            int request = _repositoryRequestVersion;
            try
            {
                await LocalChanges.LoadWorktreeAsync(requestRepository);
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, requestRepository) == false)
                {
                    return;
                }
                await RefreshConflictsFromLocalChangesAsync(requestRepository);
                SetStatusMessage(_stringHelper.Format("MainWorkingFileRestored", path), false);
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private async void OnReferenceRepositoryChanged(GitRepository updated)
        {
            await ApplyActionRepositoryAsync(updated, false, true, true);
        }

        private void OnRemoteOperationCompleted(GitRepository updated)
        {
            if (_repository == null)
            {
                return;
            }
            if (_pathComparer.Equals(_repository.RootPath, updated.RootPath) == false)
            {
                return;
            }

            _repository = updated;
            Conflicts.BindRepository(updated);
            RepositoryMeta = FormatRepositoryMeta(updated);
        }

        public Task HandleRemoteOperationFinishedAsync(GitRepository updated, RemoteOperationStateSnapshot snapshot,
            bool worktreeMayChange, int repositoryRequestVersion)
        {
            return _remoteCompletionPresenter.CompleteAsync(updated, snapshot, worktreeMayChange, repositoryRequestVersion);
        }

        internal bool TryAdoptRemoteRepository(GitRepository updated, int repositoryRequestVersion)
        {
            if (repositoryRequestVersion != _repositoryRequestVersion)
            {
                return false;
            }
            if (_repository == null)
            {
                return false;
            }
            if (_pathComparer.Equals(_repository.RootPath, updated.RootPath) == false)
            {
                return false;
            }

            _repository = updated;
            Conflicts.BindRepository(updated);
            RepositoryMeta = FormatRepositoryMeta(updated);
            return true;
        }

        internal void ReportRemoteCompletionFailure(Exception exception, GitRepository repository, int request)
        {
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
        }

        private async Task ApplyActionRepositoryAsync(GitRepository updated, bool refreshReferences, bool refreshRemote, bool refreshHistory)
        {
            if (_repository == null)
            {
                return;
            }
            if (_pathComparer.Equals(_repository.RootPath, updated.RootPath) == false)
            {
                return;
            }

            _repository = updated;
            Conflicts.BindRepository(updated);
            RepositoryMeta = FormatRepositoryMeta(updated);
            int request = _repositoryRequestVersion;
            _ = ObserveRepositoryAreaAsync(() => RefreshRebaseStateAsync(updated, request), request, null);
            _ = ObserveRepositoryAreaAsync(() => RefreshRevertStateAsync(updated, request), request, null);
            if (refreshReferences == true)
            {
                _ = ObserveRepositoryAreaAsync(() => References.SetRepositoryAsync(updated), request, null);
            }
            if (refreshRemote == true)
            {
                _ = ObserveRepositoryAreaAsync(() => RemoteOperations.SetRepositoryAsync(updated), request, null);
            }
            if (refreshHistory == true)
            {
                _historyReferenceVersions[updated.RootPath] = ++_historyReferenceChangeVersion;
                if (IsHistoryView == true)
                {
                    _ = ObserveRepositoryAreaAsync(() => LoadHistoryAsync(updated, request), request, null);
                }
            }
            try
            {
                await RefreshLocalChangesAndConflictsAsync(updated, request);
            }
            catch (Exception exception)
            {
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, updated) == false)
                {
                    return;
                }
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        internal void MarkRemoteHistoryReferencesDirty(GitRepository repository)
        {
            _historyReferenceVersions[repository.RootPath] = ++_historyReferenceChangeVersion;
        }

        internal async Task LoadHistoryAsync(GitRepository repository, int request)
        {
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            _historyReferenceVersions.TryGetValue(repository.RootPath, out int referenceVersion);
            await History.LoadAsync(repository);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            if (ReferenceEquals(History.CurrentRepository, repository) == false)
            {
                return;
            }
            if (History.IsLoading == true)
            {
                return;
            }
            if (History.ErrorText.Length > 0)
            {
                return;
            }
            if (_historyReferenceVersions.TryGetValue(repository.RootPath, out int currentVersion) == false)
            {
                return;
            }
            if (currentVersion != referenceVersion)
            {
                return;
            }
            _historyReferenceVersions.Remove(repository.RootPath);
        }

        private async void OnTagCommitSelected(string commitHash)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            int request = _repositoryRequestVersion;
            int tagRequest = ++_tagCommitSelectionRequestVersion;
            bool hasDirtyReferences = _historyReferenceVersions.TryGetValue(repository.RootPath, out int referenceVersion);
            IsHistoryView = true;
            IsLocalChangesView = false;
            IsStashView = false;
            IsGitSettingsView = false;
            try
            {
                if (hasDirtyReferences == true)
                {
                    History.Clear();
                }
                HistoryCommitSelectionResult result = await History.SelectCommitAsync(repository, commitHash, GitHistoryScope.All);
                if (IsCurrentTagCommitSelection(repository, request, tagRequest) == false)
                {
                    return;
                }
                if (_pathComparer.Equals(result.RepositoryRoot, repository.RootPath) == false)
                {
                    return;
                }
                if (result.CommitHash != commitHash)
                {
                    return;
                }
                if (result.Scope != GitHistoryScope.All)
                {
                    return;
                }
                if (result.Outcome == HistoryCommitSelectionOutcome.Superseded)
                {
                    return;
                }
                switch (result.Outcome)
                {
                    case HistoryCommitSelectionOutcome.Found:
                    case HistoryCommitSelectionOutcome.NotFoundInScope:
                        CompleteTagHistoryReferenceRefresh(repository, referenceVersion, hasDirtyReferences);
                        break;
                }
                History.ReportCommitSelectionResult(result);
            }
            catch (Exception exception)
            {
                if (IsCurrentTagCommitSelection(repository, request, tagRequest) == false)
                {
                    return;
                }
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private bool IsCurrentTagCommitSelection(GitRepository repository, int request, int tagRequest)
        {
            if (ReferenceEquals(_repository, repository) == false)
            {
                return false;
            }
            if (request != _repositoryRequestVersion)
            {
                return false;
            }
            if (tagRequest != _tagCommitSelectionRequestVersion)
            {
                return false;
            }
            if (IsHistoryView == false)
            {
                return false;
            }
            return true;
        }

        private void CompleteTagHistoryReferenceRefresh(GitRepository repository, int referenceVersion, bool hasDirtyReferences)
        {
            if (hasDirtyReferences == false)
            {
                return;
            }
            if (ReferenceEquals(History.CurrentRepository, repository) == false)
            {
                return;
            }
            if (History.SelectedScope != GitHistoryScope.All)
            {
                return;
            }
            if (History.IsLoading == true)
            {
                return;
            }
            if (History.HistoryQueryError != null)
            {
                return;
            }
            if (_historyReferenceVersions.TryGetValue(repository.RootPath, out int currentVersion) == false)
            {
                return;
            }
            if (currentVersion != referenceVersion)
            {
                return;
            }
            _historyReferenceVersions.Remove(repository.RootPath);
        }

        private async void OnStashesRequested()
        {
            await ShowStashesAsync(null);
        }

        private async void OnStashSelected(string commitHash)
        {
            await ShowStashesAsync(commitHash);
        }

        private async Task ShowStashesAsync(string commitHash)
        {
            if (_repository == null)
            {
                return;
            }
            IsHistoryView = false;
            IsLocalChangesView = false;
            IsStashView = true;
            IsGitSettingsView = false;
            GitRepository requestRepository = _repository;
            int request = _repositoryRequestVersion;
            try
            {
                await LocalChanges.LoadStashesAsync();
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, requestRepository) == false)
                {
                    return;
                }
                if (IsStashView == false)
                {
                    return;
                }
                if (commitHash != null)
                {
                    LocalChanges.Stashes.SelectedStash = LocalChanges.Stashes.Stashes.FirstOrDefault(item => item.CommitHash == commitHash);
                }
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private async void OnLocalCommitted(string commitHash)
        {
            if (_repository == null)
            {
                return;
            }
            GitRepository requestRepository = _repository;
            GitRepository completionRepository = requestRepository;
            int request = _repositoryRequestVersion;
            _historyReferenceVersions[requestRepository.RootPath] = ++_historyReferenceChangeVersion;
            string completionMessage;
            if (string.IsNullOrWhiteSpace(commitHash))
            {
                completionMessage = _stringHelper.GetString("LocalCommittedWithoutHash");
            }
            else
            {
                completionMessage = _stringHelper.Format("MainCommitCompleted", commitHash);
            }
            SetStatusMessage(completionMessage, false);
            try
            {
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, requestRepository) == false)
                {
                    return;
                }
                GitRepository updated = await _repositoryService.OpenAsync(requestRepository.RootPath);
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, requestRepository) == false)
                {
                    return;
                }
                _repository = updated;
                completionRepository = updated;
                Conflicts.BindRepository(updated);
                RepositoryMeta = FormatRepositoryMeta(updated);
                _ = ObserveRepositoryAreaAsync(() => References.SetRepositoryAsync(updated), request, null);
                _ = ObserveRepositoryAreaAsync(() => RemoteOperations.SetRepositoryAsync(updated), request, null);
                _ = ObserveRepositoryAreaAsync(() => RefreshRevertStateAsync(updated, request), request, null);
                if (IsHistoryView == true)
                {
                    _ = ObserveRepositoryAreaAsync(() => LoadHistoryAsync(updated, request), request, null);
                }
                await RefreshConflictsFromLocalChangesAsync(updated);
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, updated) == false)
                {
                    return;
                }
                SetStatusMessage(completionMessage, false);
            }
            catch (Exception exception)
            {
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, completionRepository) == false)
                {
                    return;
                }
                StatusMessage = completionMessage + Environment.NewLine + _errorLocalizer.GetDisplayMessage(exception);
            }
        }
        private async void OnLocalResolveRequested(string path)
        {
            try
            {
                await RefreshCoreAsync();
                ConflictFileItem file = Conflicts.ConflictFiles.FirstOrDefault(item => item.RelativePath == path);
                if (file != null)
                {
                    Conflicts.SelectPath(path);
                    ConflictWindowRequested?.Invoke(path);
                }
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        public async Task<StashMutationResult> CompleteStashSaveAsync(StashMutationResult result)
        {
            if (result == null)
            {
                return result;
            }
            if (result.Kind != StashMutationKind.Save)
            {
                return result;
            }
            if (result.WorktreeMayHaveChanged == false)
            {
                return result;
            }
            if (result.Repository == null)
            {
                return result;
            }
            GitRepository repository = _repository;
            if (repository == null)
            {
                return result;
            }
            if (_pathComparer.Equals(repository.RootPath, result.Repository.RootPath) == false)
            {
                return result;
            }

            int request = _repositoryRequestVersion;
            GitWorktreeStatus status = result.WorktreeStatus;
            string refreshError = string.Empty;
            try
            {
                if (status == null)
                {
                    status = await LocalChanges.RefreshStashSaveWorktreeAsync(result.Repository);
                    if (IsCurrentStashSaveCompletion(repository, request) == false)
                    {
                        return result;
                    }
                    if (status == null)
                    {
                        return result;
                    }
                }
                else
                {
                    LocalChanges.ApplyStashWorktreeStatus(result.Repository, status);
                    if (IsCurrentStashSaveCompletion(repository, request) == false)
                    {
                        return result;
                    }
                }
                IReadOnlyList<string> paths = status.Files
                    .Where(file => file.IsConflict).Select(file => file.Path).ToArray();
                await ApplyConflictPathsAsync(repository, paths);
            }
            catch (Exception exception)
            {
                refreshError = _errorLocalizer.GetDisplayMessage(exception);
            }
            if (IsCurrentStashSaveCompletion(repository, request) == false)
            {
                return result;
            }

            string errorText = result.ErrorText;
            if (string.IsNullOrWhiteSpace(refreshError) == false)
            {
                if (string.IsNullOrWhiteSpace(errorText) == true)
                {
                    errorText = refreshError;
                }
                else if (errorText != refreshError)
                {
                    errorText = string.Concat(errorText, Environment.NewLine, refreshError);
                }
            }
            StashMutationResult completion = new StashMutationResult(result.Repository, result.Kind, result.Succeeded,
                result.WorktreeMayHaveChanged, result.StashesMayHaveChanged, status, errorText);
            if (string.IsNullOrWhiteSpace(completion.ErrorText) == false)
            {
                StatusMessage = completion.ErrorText;
            }
            return completion;
        }

        private bool IsCurrentStashSaveCompletion(GitRepository repository, int request)
        {
            if (request != _repositoryRequestVersion)
            {
                return false;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return false;
            }
            return true;
        }

        public async Task CompleteStashApplyAsync(StashMutationResult result)
        {
            if (result == null)
            {
                return;
            }
            if (result.Kind != StashMutationKind.Apply)
            {
                return;
            }
            await RefreshStashWorktreeAfterMutationAsync(result);
        }

        public async Task CompleteStashPopAsync(StashMutationResult result)
        {
            if (result == null)
            {
                return;
            }
            if (result.Kind != StashMutationKind.Pop)
            {
                return;
            }
            await RefreshStashWorktreeAfterMutationAsync(result);
        }

        public Task CompleteStashDropAsync(StashMutationResult result)
        {
            return Task.CompletedTask;
        }

        private async Task RefreshStashWorktreeAfterMutationAsync(StashMutationResult result)
        {
            if (result.WorktreeMayHaveChanged == false)
            {
                return;
            }
            if (result.Repository == null)
            {
                return;
            }
            if (_repository == null)
            {
                return;
            }
            if (_pathComparer.Equals(_repository.RootPath, result.Repository.RootPath) == false)
            {
                return;
            }

            int request = _repositoryRequestVersion;
            try
            {
                if (result.WorktreeStatus != null)
                {
                    LocalChanges.ApplyStashWorktreeStatus(result.Repository, result.WorktreeStatus);
                }
                else
                {
                    await LocalChanges.RefreshStashWorktreeAsync(result.Repository);
                }
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (_repository == null)
                {
                    return;
                }
                if (_pathComparer.Equals(_repository.RootPath, result.Repository.RootPath) == false)
                {
                    return;
                }
                await RefreshConflictsFromLocalChangesAsync(_repository);
            }
            catch (Exception exception)
            {
                if (request == _repositoryRequestVersion)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
        }

        private Task ShowHistoryAsync()
        {
            if (_repository == null)
            {
                return Task.CompletedTask;
            }

            IsHistoryView = true;
            IsLocalChangesView = false;
            IsStashView = false;
            IsGitSettingsView = false;
            GitRepository repository = _repository;
            if (ReferenceEquals(History.CurrentRepository, repository) == true)
            {
                if (_historyReferenceVersions.ContainsKey(repository.RootPath) == false)
                {
                    if (History.IsLoading == true)
                    {
                        return Task.CompletedTask;
                    }
                    if (History.ErrorText.Length == 0)
                    {
                        return Task.CompletedTask;
                    }
                }
            }
            int request = _repositoryRequestVersion;
            _ = ObserveRepositoryAreaAsync(() => LoadHistoryAsync(repository, request), request, null);
            return Task.CompletedTask;
        }

        private Task ShowLocalChangesAsync()
        {
            if (_repository == null)
            {
                return Task.CompletedTask;
            }
            IsHistoryView = false;
            IsLocalChangesView = true;
            IsStashView = false;
            IsGitSettingsView = false;
            if (IsLocalChangesLoading == true)
            {
                return Task.CompletedTask;
            }
            if (LocalChanges.HasRepository == true)
            {
                if (LocalChanges.HasError == false)
                {
                    return Task.CompletedTask;
                }
            }
            GitRepository repository = _repository;
            int request = _repositoryRequestVersion;
            IsLocalChangesLoading = true;
            _ = ObserveRepositoryAreaAsync(() => RefreshLocalChangesAndConflictsAsync(repository, request), request, () => IsLocalChangesLoading = false);
            return Task.CompletedTask;
        }

        private async Task ShowGitSettingsAsync()
        {
            if (CanShowGitSettings() == false)
            {
                return;
            }

            IsHistoryView = false;
            IsLocalChangesView = false;
            IsStashView = false;
            IsGitSettingsView = true;
            await GitSettings.SetRepositoryAsync(_repository);
        }

        private async Task RefreshCoreAsync()
        {
            if (_repository == null)
            {
                return;
            }
            GitRepository repository = _repository;
            int request = _repositoryRequestVersion;
            await RefreshRevertStateAsync(repository, request);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            IReadOnlyList<string> paths = await _repositoryService.GetConflictPathsAsync(repository);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            await ApplyConflictPathsAsync(repository, paths);
        }

        private async Task RefreshConflictsFromLocalChangesAsync(GitRepository repository)
        {
            if (_repository == null)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            if (LocalChanges.HasError == true)
            {
                await RefreshCoreAsync();
                return;
            }

            IReadOnlyList<string> paths = LocalChanges.ConflictFiles.Select(file => file.Path).ToArray();
            await ApplyConflictPathsAsync(repository, paths);
        }

        private async Task ApplyConflictPathsAsync(GitRepository repository, IReadOnlyList<string> paths)
        {
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            int request = _repositoryRequestVersion;
            bool hadConflicts = Conflicts.ConflictFiles.Count > 0;
            bool applied = await Conflicts.ApplyPathsAsync(repository, paths);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            if (applied == false)
            {
                return;
            }
            NotifyRebaseState();
            if (Conflicts.ConflictFiles.Count == 0)
            {
                _autoOpenedConflicts.Remove(repository.RootPath);
                if (Conflicts.HasUnsavedConflictEdits)
                {
                    StatusMessage = Conflicts.StatusMessage;
                    return;
                }
                SetStatusMessage(_stringHelper.GetString("NoUnresolvedConflicts"), false);
                if (hadConflicts)
                {
                    ConflictResolutionCompleted?.Invoke();
                }
                return;
            }
            string signature = string.Join("\0", paths);
            _autoOpenedConflicts.TryGetValue(repository.RootPath, out string previousSignature);
            if (previousSignature == signature)
            {
                return;
            }
            _autoOpenedConflicts[repository.RootPath] = signature;
            if (Conflicts.SelectedFile != null)
            {
                ConflictWindowRequested?.Invoke(Conflicts.SelectedFile.RelativePath);
            }
        }

        public async Task CompleteConflictStageAsync(GitRepository repository, string stagedPath)
        {
            if (repository == null)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            int request = _repositoryRequestVersion;
            await RefreshConflictStateAsync(repository);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            SetStatusMessage(_stringHelper.Format("FileStaged", stagedPath), false);
        }

        public async Task RefreshConflictStateAsync(GitRepository repository)
        {
            if (repository == null)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            int request = _repositoryRequestVersion;
            await LocalChanges.LoadWorktreeAsync(repository);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            await RefreshRevertStateAsync(repository, request);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            await RefreshRebaseStateAsync(repository, request);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            await RefreshConflictsFromLocalChangesAsync(repository);
            if (request != _repositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
        }

        private void ClearRevertState()
        {
            _revertReadVersion++;
            _revertState = null;
            _isRevertStateLoading = false;
            NotifyRevertState();
        }

        private void NotifyRevertState()
        {
            OnPropertyChanged(nameof(RevertState));
            OnPropertyChanged(nameof(IsRevertInProgress));
            OnPropertyChanged(nameof(RevertStatusText));
            OnPropertyChanged(nameof(CanContinueRevert));
            OnPropertyChanged(nameof(CanAbortRevert));
        }

        private async Task RefreshRevertStateAsync(GitRepository repository, int request, bool applyConflicts = false)
        {
            if (IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            int read = ++_revertReadVersion;
            _isRevertStateLoading = true;
            NotifyRevertState();
            try
            {
                GitRevertState state = await _revertPresenter.GetRevertStateAsync(repository);
                if (read != _revertReadVersion)
                {
                    return;
                }
                if (IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                if (_pathComparer.Equals(state.RepositoryRoot, repository.RootPath) == false)
                {
                    return;
                }
                _revertState = state;
                Conflicts.SetRevertState(repository, state);
                NotifyRevertState();
                if (applyConflicts)
                {
                    if (state.IsInProgress)
                    {
                        await ApplyConflictPathsAsync(repository, state.ConflictPaths);
                        if (read != _revertReadVersion)
                        {
                            return;
                        }
                        if (IsCurrentRepositoryRequest(repository, request) == false)
                        {
                            return;
                        }
                    }
                }
            }
            catch
            {
                if (read != _revertReadVersion)
                {
                    return;
                }
                if (IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                throw;
            }
            finally
            {
                if (read == _revertReadVersion)
                {
                    if (IsCurrentRepositoryRequest(repository, request))
                    {
                        _isRevertStateLoading = false;
                        NotifyRevertState();
                    }
                }
            }
        }

        public async Task ContinueRevertAsync()
        {
            if (CanContinueRevert == false)
            {
                return;
            }
            GitRepository repository = _repository;
            GitRevertState state = _revertState;
            int request = _repositoryRequestVersion;
            try
            {
                GitRevertResult result = await _revertPresenter.ContinueRevertAsync(repository, state,
                    _stringHelper.GetString("MainRevertContinueAction"),
                    completion => ApplyRevertResultAsync(repository, completion, request, null, null));
                ReportRevertResult(repository, result, request, state.TargetHash);
            }
            catch (Exception exception)
            {
                if (IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        public async Task<GitRevertResult> AbortRevertAsync(GitRepository repository, GitRevertState state,
            int request, string confirmedPath, string confirmedDraft)
        {
            if (IsCurrentRepositoryRequest(repository, request) == false)
            {
                return null;
            }
            if (ReferenceEquals(_revertState, state) == false)
            {
                return null;
            }
            if (CanAbortRevert == false)
            {
                return null;
            }
            try
            {
                GitRevertResult result = await _revertPresenter.AbortRevertAsync(repository, state,
                    _stringHelper.GetString("MainRevertAbortAction"),
                    completion => ApplyRevertResultAsync(repository, completion, request, confirmedPath, confirmedDraft));
                ReportRevertResult(repository, result, request, state.TargetHash);
                return result;
            }
            catch (Exception exception)
            {
                if (IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return null;
                }
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                return null;
            }
        }

        private async Task ApplyRevertResultAsync(GitRepository original, GitRevertResult result, int request,
            string confirmedPath, string confirmedDraft)
        {
            if (_pathComparer.Equals(result.RepositoryRoot, original.RootPath) == false)
            {
                throw new GitException("CommitRevertRepositoryChanged", null, Array.Empty<object>());
            }
            // The original root remains dirty even if its completion belongs to an older screen.
            _historyReferenceVersions[original.RootPath] = ++_historyReferenceChangeVersion;
            if (IsCurrentRepositoryRequest(original, request) == false)
            {
                return;
            }
            GitRepository repository = original;
            if (result.Repository != null)
            {
                if (_pathComparer.Equals(result.Repository.RootPath, original.RootPath) == false)
                {
                    throw new GitException("CommitRevertRepositoryChanged", null, Array.Empty<object>());
                }
                repository = result.Repository;
                _repository = repository;
                Conflicts.BindRepository(repository);
                RepositoryMeta = FormatRepositoryMeta(repository);
            }
            _revertReadVersion++;
            _isRevertStateLoading = false;
            if (result.State != null)
            {
                if (_pathComparer.Equals(result.State.RepositoryRoot, original.RootPath) == false)
                {
                    throw new GitException("CommitRevertRepositoryChanged", null, Array.Empty<object>());
                }
                _revertState = result.State;
                Conflicts.SetRevertState(repository, result.State);
            }
            NotifyRevertState();
            if (result.Outcome == GitRevertOutcome.Aborted)
            {
                if (result.State != null)
                {
                    if (result.State.IsInProgress == false)
                    {
                        if (Conflicts.CurrentFilePath == confirmedPath)
                        {
                            if (Conflicts.ResultText == confirmedDraft)
                            {
                                Conflicts.DiscardClosedWindowEdits();
                            }
                        }
                    }
                }
            }
            if (IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            IsLocalChangesLoading = true;
            try
            {
                await LocalChanges.LoadWorktreeAsync(repository);
                if (IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                IReadOnlyList<string> paths = LocalChanges.ConflictFiles.Select(file => file.Path).ToArray();
                if (result.State != null)
                {
                    paths = result.State.ConflictPaths;
                }
                await ApplyConflictPathsAsync(repository, paths);
                if (IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
            }
            finally
            {
                if (IsCurrentRepositoryRequest(repository, request))
                {
                    IsLocalChangesLoading = false;
                }
            }
            if (IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            await References.SetRepositoryAsync(repository);
            if (IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            await RemoteOperations.SetRepositoryAsync(repository);
            if (IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            if (IsHistoryView)
            {
                await LoadHistoryAsync(repository, request);
                if (IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
            }
        }

        private void ReportRevertResult(GitRepository original, GitRevertResult result, int request, string targetHash)
        {
            if (_pathComparer.Equals(result.RepositoryRoot, original.RootPath) == false)
            {
                return;
            }
            GitRepository repository = original;
            if (result.Repository != null)
            {
                repository = result.Repository;
            }
            if (IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            if (result.Outcome == GitRevertOutcome.Failed)
            {
                if (result.OperationError == null)
                {
                    return;
                }
                SetStatusMessage(_errorLocalizer.GetDisplayMessage(result.OperationError), true);
                return;
            }
            string message;
            switch (result.Outcome)
            {
                case GitRevertOutcome.Completed:
                    string shortHash = targetHash.Substring(0, Math.Min(8, targetHash.Length));
                    message = _stringHelper.Format("HistoryRevertSucceeded", shortHash);
                    break;
                case GitRevertOutcome.Aborted:
                    message = _stringHelper.GetString("HistoryRevertAborted");
                    break;
                case GitRevertOutcome.Paused:
                    message = _stringHelper.GetString("HistoryRevertPaused");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }
            if (result.OperationError != null)
            {
                message += Environment.NewLine + _errorLocalizer.GetDisplayMessage(result.OperationError);
            }
            if (result.ReadError != null)
            {
                string refreshError = _stringHelper.Format("CommitRevertRefreshFailed",
                    _errorLocalizer.GetDisplayMessage(result.ReadError));
                message += Environment.NewLine + refreshError;
            }
            SetStatusMessage(message, true);
        }

        private async Task ContinueRebaseAsync()
        {
            if (CanContinueRebase() == false)
            {
                return;
            }

            GitRepository requestedRepository = _repository;
            int request = _repositoryRequestVersion;
            try
            {
                await _operationQueue.EnqueueAsync(requestedRepository.RootPath, _stringHelper.GetString("RebaseContinueAction"), async token =>
                {
                    GitCommandResult result = await _repositoryService.ContinueRebaseAsync(requestedRepository, token);
                    GitRepository updated = await _repositoryService.OpenAsync(requestedRepository.RootPath, token);
                    bool rebaseInProgress = await _repositoryService.IsRebaseInProgressAsync(updated, token);
                    IReadOnlyList<string> conflicts = await _repositoryService.GetConflictPathsAsync(updated, token);

                    if (IsCurrentRebaseRequest(requestedRepository.RootPath, request))
                    {
                        await ApplyRebaseResultAsync(updated, rebaseInProgress, conflicts, request);
                    }

                    if (conflicts.Count > 0)
                    {
                        if (IsCurrentRebaseRequest(requestedRepository.RootPath, request))
                        {
                            StatusMessage = _stringHelper.Format("RebaseNextConflicts", conflicts.Count);
                        }
                        return;
                    }

                    if (result.ExitCode != 0)
                    {
                        throw new GitException("RebaseContinueFailed", null, result.ExitCode, result.Error.Trim());
                    }

                    if (IsCurrentRebaseRequest(requestedRepository.RootPath, request) == false)
                    {
                        return;
                    }
                    if (rebaseInProgress)
                    {
                        StatusMessage = _stringHelper.GetString("RebaseContinueStillInProgress");
                        return;
                    }
                    StatusMessage = _stringHelper.GetString("RebaseContinueCompleted");
                });
            }
            catch (Exception exception)
            {
                if (IsCurrentRebaseRequest(requestedRepository.RootPath, request))
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
        }

        private async Task ApplyRebaseResultAsync(GitRepository updated, bool rebaseInProgress,
            IReadOnlyList<string> conflicts, int request)
        {
            if (IsCurrentRebaseRequest(updated.RootPath, request) == false)
            {
                return;
            }

            _repository = updated;
            Conflicts.BindRepository(updated);
            RepositoryMeta = FormatRepositoryMeta(updated);
            IsRebaseInProgress = rebaseInProgress;
            await ApplyConflictPathsAsync(updated, conflicts);
            if (IsCurrentRebaseRequest(updated.RootPath, request) == false)
            {
                return;
            }

            await LocalChanges.LoadWorktreeAsync(updated);
            if (IsCurrentRebaseRequest(updated.RootPath, request) == false)
            {
                return;
            }

            Task history = History.LoadAsync(updated);
            Task references = References.SetRepositoryAsync(updated);
            Task remote = RemoteOperations.SetRepositoryAsync(updated);
            await Task.WhenAll(history, references, remote);
        }

        private bool IsCurrentRebaseRequest(string repositoryRoot, int request)
        {
            if (request != _repositoryRequestVersion)
            {
                return false;
            }
            if (_repository == null)
            {
                return false;
            }
            return _pathComparer.Equals(_repository.RootPath, repositoryRoot);
        }

        private void OnGitOperationQueueStateChanged(GitOperationQueueState state)
        {
            if (Dispatcher.UIThread.CheckAccess() == true)
            {
                ApplyGitOperationQueueState(_operationQueue.GetState(state.RepositoryRoot));
                return;
            }
            Dispatcher.UIThread.Post(() => ApplyGitOperationQueueState(_operationQueue.GetState(state.RepositoryRoot)));
        }

        private void ApplyGitOperationQueueState(GitOperationQueueState state)
        {
            if (_repository == null)
            {
                return;
            }
            if (_pathComparer.Equals(_repository.RootPath, state.RepositoryRoot) == false)
            {
                return;
            }

            _runningGitOperationName = state.RunningOperationName;
            _pendingGitOperationCount = state.PendingCount;
            NotifyGitOperationQueueState();
        }

        private void ClearGitOperationQueueState()
        {
            _runningGitOperationName = string.Empty;
            _pendingGitOperationCount = 0;
            NotifyGitOperationQueueState();
        }

        private void NotifyGitOperationQueueState()
        {
            OnPropertyChanged(nameof(GitOperationQueueStatusText));
            OnPropertyChanged(nameof(HasGitOperationQueueStatus));
            OnPropertyChanged(nameof(IsGitOperationRunning));
            OnPropertyChanged(nameof(IsMainProgress));
            NotifySidebarStatusChanged();
            RefreshCommand.NotifyCanExecuteChanged();
        }

        private void OnReferencesPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            switch (eventArgs.PropertyName)
            {
                case nameof(ReferenceExplorerViewModel.StatusMessage):
                case nameof(ReferenceExplorerViewModel.PendingBranchSwitchMessage):
                case nameof(ReferenceExplorerViewModel.BranchSwitchFailureMessage):
                    break;
                default:
                    return;
            }
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(NotifySidebarStatusChanged);
                return;
            }
            NotifySidebarStatusChanged();
        }

        private void OnGitSettingsPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(GitSettingsViewModel.StatusMessage))
            {
                return;
            }
            if (IsGitSettingsView == false)
            {
                return;
            }
            int viewVersion = _gitSettingsStatusViewVersion;
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(() => UpdateGitSettingsStatusMessage(viewVersion));
                return;
            }
            UpdateGitSettingsStatusMessage(viewVersion);
        }

        private void UpdateGitSettingsStatusMessage(int viewVersion)
        {
            if (viewVersion != _gitSettingsStatusViewVersion)
            {
                return;
            }
            if (IsGitSettingsView == false)
            {
                return;
            }
            string message = GitSettings.StatusMessage;
            if (_visibleGitSettingsStatusMessage == message)
            {
                return;
            }
            _visibleGitSettingsStatusMessage = message;
            NotifySidebarStatusChanged();
        }

        private void OnLocalChangesPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(LocalChangesViewModel.ErrorText))
            {
                return;
            }
            if (IsLocalChangesView == false)
            {
                return;
            }
            int viewVersion = _localChangesStatusViewVersion;
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(() => UpdateLocalChangesErrorText(viewVersion));
                return;
            }
            UpdateLocalChangesErrorText(viewVersion);
        }

        private void UpdateLocalChangesErrorText(int viewVersion)
        {
            if (viewVersion != _localChangesStatusViewVersion)
            {
                return;
            }
            if (IsLocalChangesView == false)
            {
                return;
            }
            string message = LocalChanges.ErrorText;
            if (_visibleLocalChangesErrorText == message)
            {
                return;
            }
            _visibleLocalChangesErrorText = message;
            NotifySidebarStatusChanged();
        }

        private void OnStashPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            string propertyName = eventArgs.PropertyName;
            switch (propertyName)
            {
                case nameof(StashViewModel.ErrorText):
                case nameof(StashViewModel.StatusText):
                    break;
                default:
                    return;
            }
            if (IsStashView == false)
            {
                return;
            }
            int viewVersion = _stashStatusViewVersion;
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(() => UpdateStashStatus(propertyName, viewVersion));
                return;
            }
            UpdateStashStatus(propertyName, viewVersion);
        }

        private void UpdateStashStatus(string propertyName, int viewVersion)
        {
            if (viewVersion != _stashStatusViewVersion)
            {
                return;
            }
            if (IsStashView == false)
            {
                return;
            }
            if (propertyName == nameof(StashViewModel.ErrorText))
            {
                _visibleStashErrorText = LocalChanges.Stashes.ErrorText;
                if (string.IsNullOrWhiteSpace(_visibleStashErrorText) == false)
                {
                    _visibleStashStatusText = string.Empty;
                }
                NotifySidebarStatusChanged();
                return;
            }
            _visibleStashStatusText = LocalChanges.Stashes.StatusText;
            _visibleStashErrorText = string.Empty;
            NotifySidebarStatusChanged();
        }

        private void NotifySidebarStatusChanged()
        {
            OnPropertyChanged(nameof(SidebarStatusText));
            OnPropertyChanged(nameof(HasSidebarStatusText));
            OnPropertyChanged(nameof(HasSidebarStatus));
        }

        private static void AddSidebarStatusMessage(List<string> messages, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            if (messages.Contains(message))
            {
                return;
            }
            messages.Add(message);
        }

        private void OnConflictsPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(ConflictResolutionViewModel.HasUnsavedConflictEdits))
            {
                NotifyRevertState();
                return;
            }
            if (eventArgs.PropertyName != nameof(ConflictResolutionViewModel.IsBusy))
            {
                return;
            }
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(NotifyCommandStates);
                return;
            }
            NotifyCommandStates();
        }

        private bool CanNavigate()
        {
            if (HasRepository == false)
            {
                return false;
            }
            if (IsBusy == true)
            {
                return false;
            }
            if (Conflicts.IsBusy == true)
            {
                return false;
            }
            return true;
        }

        private bool CanShowGitSettings()
        {
            if (Conflicts.IsBusy == true)
            {
                return false;
            }
            return true;
        }

        private bool CanContinueRebase()
        {
            if (HasRepository == false)
            {
                return false;
            }
            if (IsRebaseInProgress == false)
            {
                return false;
            }
            if (IsBusy)
            {
                return false;
            }
            if (IsLocalChangesLoading)
            {
                return false;
            }
            if (Conflicts.IsBusy)
            {
                return false;
            }
            return Conflicts.ConflictFiles.Count == 0;
        }

        private void NotifyRebaseState()
        {
            OnPropertyChanged(nameof(RebaseStatusText));
            ContinueRebaseCommand.NotifyCanExecuteChanged();
            NotifyRevertState();
        }

        private bool CanRefresh()
        {
            if (HasRepository == false)
            {
                return false;
            }
            if (IsGitOperationRunning == true)
            {
                return false;
            }
            if (IsBusy == true)
            {
                return false;
            }
            if (Conflicts.IsBusy == true)
            {
                return false;
            }
            if (IsLocalChangesLoading == true)
            {
                return false;
            }
            if (IsReferencesLoading == true)
            {
                return false;
            }
            if (IsRemoteLoading == true)
            {
                return false;
            }
            if (References.IsBusy == true)
            {
                return false;
            }
            if (RemoteOperations.IsBusy == true)
            {
                return false;
            }
            if (RemoteOperations.IsLoading == true)
            {
                return false;
            }
            if (LocalChanges.IsBusy == true)
            {
                return false;
            }
            if (LocalChanges.Stashes.IsBusy == true)
            {
                return false;
            }
            if (History.IsLoading == true)
            {
                return false;
            }
            return true;
        }

        private string FormatRepositoryMeta(GitRepository repository)
        {
            string branch = repository.CurrentBranch;
            if (branch == "Detached HEAD")
            {
                branch = _stringHelper.GetString("ReferenceDetachedHead");
            }
            return $"{branch}  ·  {repository.RootPath}";
        }

        private void NotifyCommandStates()
        {
            RefreshCommand.NotifyCanExecuteChanged();
            ShowHistoryCommand.NotifyCanExecuteChanged();
            ShowLocalChangesCommand.NotifyCanExecuteChanged();
            ShowGitSettingsCommand.NotifyCanExecuteChanged();
            ContinueRebaseCommand.NotifyCanExecuteChanged();
            NotifyRevertState();
        }

    }
}
