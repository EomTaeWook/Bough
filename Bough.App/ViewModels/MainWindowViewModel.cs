using Bough.App.Localization;
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

namespace Bough.App.ViewModels
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class MainWindowViewModel : ViewModelBase, IStashMutationCompletion, IConflictStageCompletion
    {
        private readonly GitRepositoryService _repositoryService;
        private readonly GitOperationQueue _operationQueue;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly StringComparer _pathComparer;
        private readonly Dictionary<string, string> _autoOpenedConflicts;
        private GitRepository _repository;
        private string _repositoryName;
        private string _repositoryMeta;
        private string _statusMessage;
        private string _mainStatusMessage;
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
        private int _repositoryRequestVersion;
        private CancellationTokenSource _repositoryOpenCancellation;

        public MainWindowViewModel(GitRepositoryService repositoryService, GitOperationQueue operationQueue, MainWindowChildren children, ConflictResolutionViewModel conflicts, RepositoryListViewModel repositoryList, StringHelper stringHelper, GitErrorLocalizer errorLocalizer)
        {
            _repositoryService = repositoryService;
            _operationQueue = operationQueue;
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            if (OperatingSystem.IsWindows() == true)
            {
                _pathComparer = StringComparer.OrdinalIgnoreCase;
            }
            else
            {
                _pathComparer = StringComparer.Ordinal;
            }
            _autoOpenedConflicts = new Dictionary<string, string>(_pathComparer);
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
            History.ActionMessage += message => StatusMessage = message;
            History.FileRestored += OnHistoryFileRestored;
            References.PropertyChanged += OnReferencesPropertyChanged;
            References.RepositoryChanged += OnReferenceRepositoryChanged;
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
            ShowGitSettingsCommand = new AsyncRelayCommand(ShowGitSettingsAsync, CanNavigate);
            IsLocalChangesView = true;
        }

        public event Action<string> ConflictWindowRequested;
        public event Action ConflictResolutionCompleted;

        public ConflictResolutionViewModel Conflicts { get; }
        public RepositoryListViewModel RepositoryList { get; }
        public HistoryViewModel History { get; }
        public ReferenceExplorerViewModel References { get; }
        public RemoteOperationsViewModel RemoteOperations { get; }
        public GitSettingsViewModel GitSettings { get; }
        public LocalChangesViewModel LocalChanges { get; }

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
                SetProperty(ref _isLocalChangesView, value);
            }
        }

        public bool IsStashView
        {
            get { return _isStashView; }
            private set
            {
                SetProperty(ref _isStashView, value);
            }
        }

        public bool IsGitSettingsView
        {
            get { return _isGitSettingsView; }
            private set
            {
                SetProperty(ref _isGitSettingsView, value);
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
                    NotifyCommandStates();
                }
            }
        }

        public bool IsLocalChangesLoading
        {
            get { return _isLocalChangesLoading; }
            private set { SetProperty(ref _isLocalChangesLoading, value); }
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

        public async Task OpenRepositoryAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                StatusMessage = "Select a repository path.";
                return;
            }
            string requestedPath;
            try
            {
                requestedPath = Path.GetFullPath(path);
            }
            catch (ArgumentException exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                return;
            }
            catch (NotSupportedException exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                return;
            }
            catch (PathTooLongException exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                return;
            }

            int request = ++_repositoryRequestVersion;
            _repositoryOpenCancellation?.Cancel();
            CancellationTokenSource cancellation = new();
            _repositoryOpenCancellation = cancellation;
            _repository = null;
            ClearGitOperationQueueState();
            Conflicts.BindRepository(null);
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
            RepositoryMeta = $"{opened.CurrentBranch}  ·  {opened.RootPath}";
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
            References.BindRepository(repository);
            RemoteOperations.BindRepository(repository);
            IsLocalChangesLoading = true;
            IsReferencesLoading = true;
            IsRemoteLoading = true;
            _ = ObserveRepositoryAreaAsync(() => RefreshLocalChangesAndConflictsAsync(repository, request), request, () => IsLocalChangesLoading = false);
            _ = ObserveRepositoryAreaAsync(References.RefreshAsync, request, () => IsReferencesLoading = false);
            _ = ObserveRepositoryAreaAsync(RemoteOperations.RefreshAsync, request, () => IsRemoteLoading = false);
            if (IsGitSettingsView == true)
            {
                _ = ObserveRepositoryAreaAsync(() => GitSettings.SetRepositoryAsync(repository), request, null);
            }
            if (loadHistory == true)
            {
                _ = ObserveRepositoryAreaAsync(() => History.LoadAsync(repository), request, null);
            }
        }

        private async Task ObserveRepositoryAreaAsync(Func<Task> load, int request, Action completed)
        {
            try
            {
                await load();
            }
            catch (Exception exception)
            {
                if (request == _repositoryRequestVersion)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
            finally
            {
                if (request == _repositoryRequestVersion)
                {
                    completed?.Invoke();
                }
            }
        }

        private async Task RefreshLocalChangesAndConflictsAsync(GitRepository repository, int request)
        {
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
            RepositoryMeta = $"{repository.CurrentBranch}  ·  {repository.RootPath}";
            StartRepositoryAreaLoads(repository, request, IsHistoryView);
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
                SetStatusMessage($"Restored working file {path}.", false);
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
            RepositoryMeta = $"{updated.CurrentBranch}  ·  {updated.RootPath}";
        }

        public async Task HandleRemoteOperationFinishedAsync(GitRepository updated, RemoteOperationStateSnapshot snapshot,
            bool worktreeMayChange, int repositoryRequestVersion)
        {
            if (repositoryRequestVersion != _repositoryRequestVersion)
            {
                return;
            }
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
            RepositoryMeta = $"{updated.CurrentBranch}  ·  {updated.RootPath}";
            if (snapshot != null)
            {
                RemoteOperations.ApplyOperationStateSnapshot(snapshot);
            }
            int request = _repositoryRequestVersion;
            _ = ObserveRepositoryAreaAsync(() => References.SetRepositoryAsync(updated), request, null);
            if (IsHistoryView == true)
            {
                _ = ObserveRepositoryAreaAsync(() => History.LoadAsync(updated), request, null);
            }
            if (worktreeMayChange == false)
            {
                return;
            }

            try
            {
                await RefreshLocalChangesAndConflictsAsync(updated, request);
            }
            catch (Exception exception)
            {
                if (request == _repositoryRequestVersion)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
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
            RepositoryMeta = $"{updated.CurrentBranch}  ·  {updated.RootPath}";
            int request = _repositoryRequestVersion;
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
                if (IsHistoryView == true)
                {
                    _ = ObserveRepositoryAreaAsync(() => History.LoadAsync(updated), request, null);
                }
            }
            try
            {
                await RefreshLocalChangesAndConflictsAsync(updated, request);
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private async void OnTagCommitSelected(string commitHash)
        {
            if (_repository == null)
            {
                return;
            }
            IsHistoryView = true;
            IsLocalChangesView = false;
            IsStashView = false;
            IsGitSettingsView = false;
            GitRepository requestRepository = _repository;
            int request = _repositoryRequestVersion;
            try
            {
                if (ReferenceEquals(History.CurrentRepository, requestRepository) == false)
                {
                    await History.LoadAsync(requestRepository);
                }
                if (request != _repositoryRequestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, requestRepository) == false)
                {
                    return;
                }
                await History.SelectCommitAsync(commitHash);
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
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
            int request = _repositoryRequestVersion;
            try
            {
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
                Conflicts.BindRepository(updated);
                RepositoryMeta = $"{updated.CurrentBranch}  ·  {updated.RootPath}";
                _ = ObserveRepositoryAreaAsync(() => References.SetRepositoryAsync(updated), request, null);
                _ = ObserveRepositoryAreaAsync(() => RemoteOperations.SetRepositoryAsync(updated), request, null);
                if (IsHistoryView == true)
                {
                    _ = ObserveRepositoryAreaAsync(() => History.LoadAsync(updated), request, null);
                }
                await RefreshConflictsFromLocalChangesAsync(updated);
                SetStatusMessage($"Committed {commitHash}.", false);
            }
            catch (Exception exception)
            {
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
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

        public async Task CompleteStashSaveAsync(StashMutationResult result)
        {
            if (result == null)
            {
                return;
            }
            if (result.Kind != StashMutationKind.Save)
            {
                return;
            }
            await RefreshStashWorktreeAfterMutationAsync(result);
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
                if (History.IsLoading == true)
                {
                    return Task.CompletedTask;
                }
                if (History.ErrorText.Length == 0)
                {
                    return Task.CompletedTask;
                }
            }
            int request = _repositoryRequestVersion;
            _ = ObserveRepositoryAreaAsync(() => History.LoadAsync(repository), request, null);
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
            if (_repository == null)
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
            IReadOnlyList<string> paths = await _repositoryService.GetConflictPathsAsync(repository);
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
            bool hadConflicts = Conflicts.ConflictFiles.Count > 0;
            bool applied = await Conflicts.ApplyPathsAsync(repository, paths);
            if (applied == false)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            if (Conflicts.ConflictFiles.Count == 0)
            {
                _autoOpenedConflicts.Remove(repository.RootPath);
                if (Conflicts.HasUnsavedConflictEdits == true)
                {
                    StatusMessage = Conflicts.StatusMessage;
                    return;
                }
                SetStatusMessage(_stringHelper.GetString("NoUnresolvedConflicts"), false);
                if (hadConflicts == true)
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
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            await RefreshConflictStateAsync(repository);
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            SetStatusMessage(_stringHelper.Format("FileStaged", stagedPath), false);
        }

        public async Task RefreshConflictStateAsync(GitRepository repository)
        {
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
            await RefreshConflictsFromLocalChangesAsync(repository);
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

        private void NotifyCommandStates()
        {
            RefreshCommand.NotifyCanExecuteChanged();
            ShowHistoryCommand.NotifyCanExecuteChanged();
            ShowLocalChangesCommand.NotifyCanExecuteChanged();
            ShowGitSettingsCommand.NotifyCanExecuteChanged();
        }

    }
}
