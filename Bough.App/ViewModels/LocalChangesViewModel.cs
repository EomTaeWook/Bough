using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.ViewModels
{
    public class LocalChangesViewModel : ViewModelBase
    {
        private readonly GitWorkingTreeService _workingTreeService;
        private readonly GitOperationQueue _operationQueue;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly ObservableCollection<GitWorktreeFile> _unstagedFiles;
        private readonly ObservableCollection<GitWorktreeFile> _stagedFiles;
        private readonly ObservableCollection<GitWorktreeFile> _conflictFiles;
        private readonly List<GitWorktreeFile> _selectedUnstagedFiles;
        private readonly Dictionary<string, string> _queuedMutationErrors;
        private GitRepository _repository;
        private GitWorktreeStatus _workingStatus;
        private Task _worktreeLoadTask = Task.CompletedTask;
        private GitWorktreeFile _selectedUnstagedFile;
        private GitWorktreeFile _selectedStagedFile;
        private GitWorktreeFile _selectedConflictFile;
        private string _previewText;
        private bool _previewIsUnifiedDiff;
        private string _previewDescription;
        private string _errorText;
        private string _statusText;
        private string _commitMessage;
        private string _draftCommitMessage;
        private bool _amend;
        private bool _isLoadingAmend;
        private bool _isBusy;
        private int _requestVersion;
        private int _previewVersion;
        private int _amendVersion;

        public LocalChangesViewModel(GitWorkingTreeService workingTreeService, GitStashService stashService, GitOperationQueue operationQueue,
            StringHelper stringHelper, GitErrorLocalizer errorLocalizer)
        {
            if (workingTreeService == null)
            {
                throw new ArgumentNullException(nameof(workingTreeService));
            }

            if (stringHelper == null)
            {
                throw new ArgumentNullException(nameof(stringHelper));
            }

            if (operationQueue == null)
            {
                throw new ArgumentNullException(nameof(operationQueue));
            }
            if (errorLocalizer == null)
            {
                throw new ArgumentNullException(nameof(errorLocalizer));
            }

            _workingTreeService = workingTreeService;
            _operationQueue = operationQueue;
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            Stashes = new StashViewModel(stashService, workingTreeService, operationQueue, stringHelper, errorLocalizer);
            Stashes.ResolveRequested += path => ResolveRequested?.Invoke(path);
            _unstagedFiles = [];
            _stagedFiles = [];
            _conflictFiles = [];
            _selectedUnstagedFiles = [];
            StringComparer repositoryComparer = StringComparer.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                repositoryComparer = StringComparer.OrdinalIgnoreCase;
            }
            _queuedMutationErrors = new Dictionary<string, string>(repositoryComparer);
            UnstagedFiles = new ReadOnlyObservableCollection<GitWorktreeFile>(_unstagedFiles);
            StagedFiles = new ReadOnlyObservableCollection<GitWorktreeFile>(_stagedFiles);
            ConflictFiles = new ReadOnlyObservableCollection<GitWorktreeFile>(_conflictFiles);
            _previewText = string.Empty;
            _previewDescription = _stringHelper.GetString("LocalPreviewPrompt");
            _errorText = string.Empty;
            _statusText = string.Empty;
            _commitMessage = string.Empty;
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
            StageSelectedCommand = new QueuedAsyncRelayCommand(StageSelectedAsync, CanStageSelected, ShowMutationCommandError);
            UnstageSelectedCommand = new QueuedAsyncRelayCommand(UnstageSelectedAsync, CanUnstageSelected, ShowMutationCommandError);
            StageAllCommand = new QueuedAsyncRelayCommand(StageAllAsync, CanStageAll, ShowMutationCommandError);
            DiscardSelectedCommand = new QueuedAsyncRelayCommand(DiscardSelectedAsync, CanDiscardSelected, ShowMutationCommandError);
            UnstageAllCommand = new QueuedAsyncRelayCommand(UnstageAllAsync, CanUnstageAll, ShowMutationCommandError);
            CommitCommand = new QueuedAsyncRelayCommand(CommitAsync, CanCommit, ShowMutationCommandError);
            OpenResolveCommand = new RelayCommand(OpenResolve, CanOpenResolve);
        }

        public event Action<string> ResolveRequested;

        public event Action<string> Committed;

        public event Func<IReadOnlyList<GitLargeFileCandidate>, CancellationToken, Task<bool>> ConfirmLargeFilesRequested;

        public event Func<IReadOnlyList<GitDiscardPlan>, CancellationToken, Task<bool>> ConfirmDiscardRequested;

        public event Func<GitIgnorePlan, CancellationToken, Task<bool>> ConfirmIgnoreRequested;

        public StashViewModel Stashes { get; }

        public ReadOnlyObservableCollection<GitWorktreeFile> UnstagedFiles { get; }

        public ReadOnlyObservableCollection<GitWorktreeFile> StagedFiles { get; }

        public ReadOnlyObservableCollection<GitWorktreeFile> ConflictFiles { get; }

        public bool HasRepository { get { return _repository != null; } }

        public GitRepository CurrentRepository { get { return _repository; } }

        public bool HasConflicts { get { return _conflictFiles.Count > 0; } }

        public bool HasStagedFiles { get { return _stagedFiles.Count > 0; } }

        public bool HasUnstagedFiles { get { return _unstagedFiles.Count > 0; } }

        public AsyncRelayCommand RefreshCommand { get; }

        public QueuedAsyncRelayCommand StageSelectedCommand { get; }

        public QueuedAsyncRelayCommand UnstageSelectedCommand { get; }

        public QueuedAsyncRelayCommand StageAllCommand { get; }

        public QueuedAsyncRelayCommand DiscardSelectedCommand { get; }

        public QueuedAsyncRelayCommand UnstageAllCommand { get; }

        public QueuedAsyncRelayCommand CommitCommand { get; }

        public RelayCommand OpenResolveCommand { get; }

        public string HeadingText { get { return _stringHelper.GetString("LocalChangesHeading"); } }

        public string RefreshText { get { return _stringHelper.GetString("RefreshButton"); } }

        public string UnstagedHeadingText { get { return _stringHelper.GetString("UnstagedHeading"); } }

        public string StagedHeadingText { get { return _stringHelper.GetString("StagedHeading"); } }

        public string ConflictsHeadingText { get { return $"{_stringHelper.GetString("ConflictsHeading")} ({_conflictFiles.Count})"; } }

        public string StageSelectedText { get { return _stringHelper.GetString("StageSelected"); } }

        public string StageAllText { get { return _stringHelper.GetString("StageAll"); } }

        public string DiscardSelectedText { get { return _stringHelper.GetString("DiscardSelected"); } }

        public string DiscardSelectionText { get { return GetDiscardSelectionText(GetDiscardSelection().Count); } }

        public string GetDiscardSelectionText(int count) { return _stringHelper.Format("DiscardSelectionCount", count); }

        public string DiscardTitleText { get { return _stringHelper.GetString("DiscardTitle"); } }

        public string DiscardPathLabelText { get { return _stringHelper.GetString("DiscardPathLabel"); } }

        public string DiscardCancelText { get { return _stringHelper.GetString("ReferenceCancel"); } }

        public string IgnoreMenuText { get { return _stringHelper.GetString("IgnoreMenu"); } }

        public string IgnoreRepositoryText { get { return _stringHelper.GetString("IgnoreRepository"); } }

        public string IgnoreLocalText { get { return _stringHelper.GetString("IgnoreLocal"); } }

        public string IgnoreConfirmTitleText { get { return _stringHelper.GetString("IgnoreConfirmTitle"); } }

        public string IgnoreConfirmButtonText { get { return _stringHelper.GetString("IgnoreConfirmButton"); } }

        public string GetIgnoreDescription(GitIgnorePlan plan)
        {
            string location = IgnoreRepositoryText;
            if (plan.Location == GitIgnoreLocation.Local)
            {
                location = IgnoreLocalText;
            }

            return _stringHelper.Format("IgnoreConfirmDescription", plan.Entries.Count, location);
        }

        public string GetDiscardImpactText(IReadOnlyList<GitDiscardPlan> plans)
        {
            int untrackedCount = plans.Count(plan => plan.IsUntracked);
            int renameCount = plans.Count(plan => plan.IsWorktreeRename);
            int trackedCount = plans.Count - untrackedCount;
            return _stringHelper.Format("DiscardBatchImpact", trackedCount, untrackedCount, renameCount);
        }

        public string GetDiscardConfirmText(IReadOnlyList<GitDiscardPlan> plans)
        {
            if (plans.All(plan => plan.IsUntracked) == true)
            {
                return _stringHelper.GetString("DiscardConfirmUntracked");
            }

            return _stringHelper.GetString("DiscardConfirmTracked");
        }

        public string LargeStageTitleText { get { return _stringHelper.GetString("LargeStageTitle"); } }

        public string LargeStageContinueText { get { return _stringHelper.GetString("LargeStageContinue"); } }

        public string LargeStageCancelText { get { return _stringHelper.GetString("ReferenceCancel"); } }

        public string GetLargeStageDescription(int count) { return _stringHelper.Format("LargeStageDescription", count); }

        public string UnstageSelectedText { get { return _stringHelper.GetString("UnstageSelected"); } }

        public string UnstageAllText { get { return _stringHelper.GetString("UnstageAll"); } }

        public string OpenResolveText { get { return _stringHelper.GetString("OpenResolve"); } }

        public string CommitHeadingText { get { return _stringHelper.GetString("CommitHeading"); } }

        public string CommitMessageLabelText { get { return _stringHelper.GetString("CommitMessageLabel"); } }

        public string CommitMessagePlaceholderText { get { return _stringHelper.GetString("CommitMessagePlaceholder"); } }

        public string AmendPreviousCommitText { get { return _stringHelper.GetString("AmendPreviousCommit"); } }

        public string CommitButtonText { get { return _stringHelper.GetString("CommitButton"); } }

        public string PreviewText
        {
            get { return _previewText; }
            private set { SetProperty(ref _previewText, value); }
        }

        public bool PreviewIsUnifiedDiff
        {
            get { return _previewIsUnifiedDiff; }
            private set { SetProperty(ref _previewIsUnifiedDiff, value); }
        }

        public string PreviewDescription
        {
            get { return _previewDescription; }
            private set { SetProperty(ref _previewDescription, value); }
        }

        public string ErrorText
        {
            get { return _errorText; }
            private set
            {
                if (SetProperty(ref _errorText, value) == true)
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError { get { return ErrorText.Length > 0; } }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        public string CommitMessage
        {
            get { return _commitMessage; }
            set
            {
                if (SetProperty(ref _commitMessage, value) == true)
                {
                    OnPropertyChanged(nameof(CommitHint));
                    CommitCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool Amend
        {
            get { return _amend; }
            set
            {
                if (SetProperty(ref _amend, value) == false)
                {
                    return;
                }

                _amendVersion++;
                if (value == true)
                {
                    ErrorText = string.Empty;
                    _draftCommitMessage = CommitMessage;
                    SetAmendLoading(true);
                    _ = LoadAmendMessageAsync(_repository, _amendVersion);
                }
                else
                {
                    SetAmendLoading(false);
                    CommitMessage = _draftCommitMessage ?? string.Empty;
                    _draftCommitMessage = null;
                }

                CommitCommand.NotifyCanExecuteChanged();
            }
        }

        public bool IsCommitMessageEditable { get { return _isLoadingAmend == false; } }

        public string CommitHint
        {
            get
            {
                if (_isLoadingAmend == true)
                {
                    return _stringHelper.GetString("LoadingAmendMessage");
                }

                if (HasCommitSummary() == false)
                {
                    return _stringHelper.GetString("EnterCommitMessage");
                }

                if (_conflictFiles.Count > 0)
                {
                    return _stringHelper.GetString("ResolveBeforeCommit");
                }

                if (_stagedFiles.Count == 0)
                {
                    return _stringHelper.GetString("StageBeforeCommit");
                }

                return string.Empty;
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    OnPropertyChanged(nameof(IsCommitMessageEditable));
                    NotifyCommandStates();
                }
            }
        }

        public GitWorktreeFile SelectedUnstagedFile
        {
            get { return _selectedUnstagedFile; }
            set
            {
                if (SetProperty(ref _selectedUnstagedFile, value) == false)
                {
                    return;
                }

                if (value != null)
                {
                    SelectedStagedFile = null;
                    SelectedConflictFile = null;
                    _ = LoadPreviewAsync(value, false);
                }

                NotifyCommandStates();
            }
        }

        public void SetUnstagedSelection(IEnumerable<GitWorktreeFile> files)
        {
            _selectedUnstagedFiles.Clear();
            foreach (GitWorktreeFile file in files)
            {
                if (_unstagedFiles.Contains(file) == false)
                {
                    continue;
                }

                if (file.IsConflict == true)
                {
                    continue;
                }

                _selectedUnstagedFiles.Add(file);
            }

            OnPropertyChanged(nameof(DiscardSelectionText));
            DiscardSelectedCommand.NotifyCanExecuteChanged();
        }

        private List<GitWorktreeFile> GetDiscardSelection()
        {
            if (_selectedUnstagedFiles.Count > 0)
            {
                return _selectedUnstagedFiles.ToList();
            }

            if (SelectedUnstagedFile != null)
            {
                return [SelectedUnstagedFile];
            }

            return [];
        }

        public GitWorktreeFile SelectedStagedFile
        {
            get { return _selectedStagedFile; }
            set
            {
                if (SetProperty(ref _selectedStagedFile, value) == false)
                {
                    return;
                }

                if (value != null)
                {
                    SelectedUnstagedFile = null;
                    SelectedConflictFile = null;
                    _ = LoadPreviewAsync(value, true);
                }

                NotifyCommandStates();
            }
        }

        public GitWorktreeFile SelectedConflictFile
        {
            get { return _selectedConflictFile; }
            set
            {
                if (SetProperty(ref _selectedConflictFile, value) == false)
                {
                    return;
                }

                if (value != null)
                {
                    SelectedUnstagedFile = null;
                    SelectedStagedFile = null;
                    _ = LoadPreviewAsync(value, false);
                }

                NotifyCommandStates();
                if (value == null)
                {
                    return;
                }
                if (IsBusy == true)
                {
                    return;
                }
                if (_repository == null)
                {
                    return;
                }
                if (_conflictFiles.Contains(value) == false)
                {
                    return;
                }

                ResolveRequested?.Invoke(value.Path);
            }
        }

        public void Clear()
        {
            Stashes.Clear();
            _requestVersion++;
            _previewVersion++;
            _amendVersion++;
            _repository = null;
            _workingStatus = null;
            _worktreeLoadTask = Task.CompletedTask;
            OnPropertyChanged(nameof(HasRepository));
            _draftCommitMessage = null;
            SetProperty(ref _amend, false, nameof(Amend));
            SetAmendLoading(false);
            CommitMessage = string.Empty;
            SelectedUnstagedFile = null;
            _selectedUnstagedFiles.Clear();
            OnPropertyChanged(nameof(DiscardSelectionText));
            SelectedStagedFile = null;
            SelectedConflictFile = null;
            _unstagedFiles.Clear();
            _stagedFiles.Clear();
            _conflictFiles.Clear();
            PreviewText = string.Empty;
            PreviewIsUnifiedDiff = false;
            PreviewDescription = _stringHelper.GetString("LocalPreviewPrompt");
            ErrorText = string.Empty;
            StatusText = string.Empty;
            IsBusy = false;
            UpdateCounts();
        }

        public void InvalidatePendingRequests()
        {
            _requestVersion++;
            _previewVersion++;
            _amendVersion++;
            Stashes.InvalidatePendingRequests();
            SetAmendLoading(false);
            IsBusy = false;
        }

        public Task LoadAsync(GitRepository repository)
        {
            return LoadWorktreeAsync(repository);
        }

        public Task LoadWorktreeAsync(GitRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            bool changed = _repository == null || _repository.RootPath != repository.RootPath;
            if (changed == true)
            {
                Clear();
            }

            _repository = repository;
            Stashes.BindRepository(repository);
            _workingStatus = null;
            OnPropertyChanged(nameof(HasRepository));
            if (_queuedMutationErrors.TryGetValue(repository.RootPath, out string queuedError) == true)
            {
                ErrorText = queuedError;
            }
            _requestVersion++;
            _worktreeLoadTask = RefreshCoreAsync(repository, _requestVersion);
            return _worktreeLoadTask;
        }

        public async Task LoadStashesAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            int requestVersion = _requestVersion;
            await _worktreeLoadTask;
            if (requestVersion != _requestVersion)
            {
                return;
            }

            GitWorktreeStatus status = _workingStatus;
            if (status == null)
            {
                await Stashes.LoadAsync(repository);
                return;
            }

            await Stashes.LoadAsync(repository, status);
        }

        public async Task<bool> PrepareStashSaveAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return false;
            }

            int requestVersion = _requestVersion;
            await _worktreeLoadTask;
            if (requestVersion != _requestVersion)
            {
                return false;
            }

            GitWorktreeStatus status = _workingStatus;
            if (status == null)
            {
                return false;
            }

            await LoadStashesAsync();
            if (requestVersion != _requestVersion)
            {
                return false;
            }

            return true;
        }

        public Task RefreshAsync()
        {
            return RefreshAsync(null, false);
        }

        private Task RefreshAsync(string preferredPath, bool preferStaged)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return Task.CompletedTask;
            }

            _requestVersion++;
            _worktreeLoadTask = RefreshCoreAsync(repository, _requestVersion, preferredPath, preferStaged);
            return _worktreeLoadTask;
        }

        public void ApplyStashWorktreeStatus(GitRepository repository, GitWorktreeStatus status)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(status);
            if (_repository == null)
            {
                return;
            }
            if (_repository.RootPath != repository.RootPath)
            {
                return;
            }

            _requestVersion++;
            _previewVersion++;
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                ApplyWorktreeStatus(status);
                _worktreeLoadTask = Task.CompletedTask;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public Task RefreshStashWorktreeAsync(GitRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (_repository == null)
            {
                return Task.CompletedTask;
            }
            if (_repository.RootPath != repository.RootPath)
            {
                return Task.CompletedTask;
            }

            _requestVersion++;
            _worktreeLoadTask = RefreshCoreAsync(repository, _requestVersion);
            return _worktreeLoadTask;
        }

        private async Task RefreshCoreAsync(GitRepository repository, int requestVersion, string preferredPath = null, bool preferStaged = false)
        {
            IsBusy = true;
            if (_queuedMutationErrors.ContainsKey(repository.RootPath) == false)
            {
                ErrorText = string.Empty;
            }
            try
            {
                GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository);
                if (requestVersion != _requestVersion)
                {
                    return;
                }

                ApplyWorktreeStatus(status, preferredPath, preferStaged);
                if (_queuedMutationErrors.TryGetValue(repository.RootPath, out string queuedError) == true)
                {
                    ErrorText = queuedError;
                }
            }
            catch (Exception exception)
            {
                if (requestVersion == _requestVersion)
                {
                    ErrorText = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
            finally
            {
                if (requestVersion == _requestVersion)
                {
                    IsBusy = false;
                }
            }
        }

        private async Task LoadPreviewAsync(GitWorktreeFile file, bool staged)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            _previewVersion++;
            int previewVersion = _previewVersion;
            int requestVersion = _requestVersion;
            PreviewText = string.Empty;
            PreviewIsUnifiedDiff = false;
            PreviewDescription = _stringHelper.GetString("LocalPreviewLoading");
            try
            {
                GitFilePreview preview = await _workingTreeService.GetPreviewAsync(repository, file, staged);
                if (previewVersion != _previewVersion)
                {
                    return;
                }
                if (requestVersion != _requestVersion)
                {
                    return;
                }

                PreviewIsUnifiedDiff = file.IsUntracked == false;
                PreviewText = preview.Text;
                PreviewDescription = _stringHelper.Format(preview.DescriptionCode, preview.DescriptionArguments.ToArray());
            }
            catch (Exception exception)
            {
                if (previewVersion == _previewVersion)
                {
                    if (requestVersion == _requestVersion)
                    {
                        ErrorText = _errorLocalizer.GetDisplayMessage(exception);
                        PreviewDescription = _stringHelper.GetString("LocalPreviewFailed");
                    }
                }
            }
        }

        private Task StageSelectedAsync()
        {
            GitRepository repository = _repository;
            GitWorktreeFile file = SelectedUnstagedFile;
            if (repository == null)
            {
                return Task.CompletedTask;
            }
            if (file == null)
            {
                return Task.CompletedTask;
            }

            return RunStageOperationAsync(repository, [file], file.Path, file.Path);
        }

        private Task UnstageSelectedAsync()
        {
            return UnstageFileAsync(_repository, SelectedStagedFile);
        }

        public Task UnstageFileAsync(GitRepository repository, GitWorktreeFile file)
        {
            if (repository == null)
            {
                return Task.CompletedTask;
            }
            if (file == null)
            {
                return Task.CompletedTask;
            }

            return QueueMutationAsync(repository, UnstageSelectedText, async cancellationToken =>
            {
                await RunOperationCoreAsync(repository, async token =>
                {
                    GitWorktreeFile current = await ValidateFileSelectionAsync(repository, file, true, token);
                    await _workingTreeService.UnstageAsync(repository, current, token);
                    return true;
                }, file.Path, false, cancellationToken);
            });
        }

        private Task DiscardSelectedAsync()
        {
            return DiscardPathsAsync(GetDiscardSelection().Select(file => file.Path).ToList());
        }

        public Task DiscardPathsAsync(IReadOnlyList<string> paths)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return Task.CompletedTask;
            }

            if (paths == null)
            {
                return Task.CompletedTask;
            }

            if (paths.Count == 0)
            {
                return Task.CompletedTask;
            }

            List<GitWorktreeFile> files = [];
            foreach (string path in paths.Distinct(StringComparer.Ordinal))
            {
                GitWorktreeFile file = _unstagedFiles.FirstOrDefault(candidate => candidate.Path == path);
                if (file == null)
                {
                    ErrorText = _stringHelper.Format("LocalSelectedFileUnavailable", path);
                    return Task.CompletedTask;
                }

                if (file.IsConflict == true)
                {
                    ErrorText = _stringHelper.Format("LocalResolveBeforeDiscard", path);
                    return Task.CompletedTask;
                }

                files.Add(file);
            }

            return QueueMutationAsync(repository, GetDiscardSelectionText(files.Count), cancellationToken => DiscardCoreAsync(repository, files, cancellationToken));
        }

        private async Task DiscardCoreAsync(GitRepository repository, IReadOnlyList<GitWorktreeFile> files, CancellationToken cancellationToken)
        {
            bool active = IsCurrentRepository(repository);
            if (active == true)
            {
                IsBusy = true;
                ErrorText = string.Empty;
            }

            try
            {
                IReadOnlyList<GitDiscardPlan> plans = await _workingTreeService.PrepareDiscardsAsync(repository, files, cancellationToken);
                Func<IReadOnlyList<GitDiscardPlan>, CancellationToken, Task<bool>> confirm = ConfirmDiscardRequested;
                if (confirm == null)
                {
                    throw new GitException("LocalDiscardRequiresConfirmation", null, Array.Empty<object>());
                }

                bool accepted = await confirm(plans, cancellationToken);
                if (accepted == false)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                GitDiscardBatchResult result = await _workingTreeService.ApplyDiscardsAsync(repository, plans, cancellationToken);
                if (IsCurrentRepository(repository) == true)
                {
                    await RefreshAsync();
                }
                if (result.HasError == true)
                {
                    throw new GitException("DiscardBatchPartialFailure", result.Error, result.CompletedPaths.Count, string.Join("\n", result.CompletedPaths), result.RemainingPaths.Count, string.Join("\n", result.RemainingPaths), _errorLocalizer.GetDisplayMessage(result.Error));
                }
            }
            finally
            {
                if (active == true)
                {
                    if (IsCurrentRepository(repository) == true)
                    {
                        IsBusy = false;
                    }
                }
            }
        }

        public bool CanIgnorePaths(IReadOnlyList<string> paths)
        {
            if (_repository == null)
            {
                return false;
            }

            if (paths == null)
            {
                return false;
            }

            if (paths.Count == 0)
            {
                return false;
            }

            foreach (string path in paths)
            {
                GitWorktreeFile file = _unstagedFiles.FirstOrDefault(candidate => candidate.Path == path);
                if (file == null)
                {
                    return false;
                }

                if (file.IsUntracked == false)
                {
                    return false;
                }
            }

            return true;
        }

        public Task IgnorePathsAsync(IReadOnlyList<string> paths, GitIgnoreLocation location)
        {
            GitRepository repository = _repository;
            if (CanIgnorePaths(paths) == false)
            {
                return Task.CompletedTask;
            }

            List<GitWorktreeFile> files = [];
            foreach (string path in paths.Distinct(StringComparer.Ordinal))
            {
                GitWorktreeFile file = _unstagedFiles.FirstOrDefault(candidate => candidate.Path == path);
                if (file == null)
                {
                    ErrorText = _stringHelper.Format("LocalSelectedFileUnavailable", path);
                    return Task.CompletedTask;
                }

                files.Add(file);
            }

            return QueueMutationAsync(repository, IgnoreMenuText, cancellationToken => IgnoreCoreAsync(repository, files, location, cancellationToken));
        }

        private async Task IgnoreCoreAsync(GitRepository repository, IReadOnlyList<GitWorktreeFile> files, GitIgnoreLocation location, CancellationToken cancellationToken)
        {
            bool active = IsCurrentRepository(repository);
            if (active == true)
            {
                IsBusy = true;
                ErrorText = string.Empty;
            }

            try
            {
                GitIgnorePlan plan = await _workingTreeService.Ignore.PrepareAsync(repository, files, location, cancellationToken);
                Func<GitIgnorePlan, CancellationToken, Task<bool>> confirm = ConfirmIgnoreRequested;
                if (confirm == null)
                {
                    throw new GitException("LocalIgnoreRequiresConfirmation", null, Array.Empty<object>());
                }

                bool accepted = await confirm(plan, cancellationToken);
                if (accepted == false)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _workingTreeService.Ignore.ApplyAsync(repository, plan, cancellationToken);
                if (IsCurrentRepository(repository) == true)
                {
                    await RefreshAsync();
                }
            }
            finally
            {
                if (active == true)
                {
                    if (IsCurrentRepository(repository) == true)
                    {
                        IsBusy = false;
                    }
                }
            }
        }

        private Task StageAllAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return Task.CompletedTask;
            }

            return RunStageOperationAsync(repository, _unstagedFiles.ToArray(), null, SelectedUnstagedFile?.Path);
        }

        private Task RunStageOperationAsync(GitRepository repository, IReadOnlyList<GitWorktreeFile> expectedFiles, string selectedPath, string preferredPath)
        {
            string operationName = StageAllText;
            if (selectedPath != null)
            {
                operationName = StageSelectedText;
            }

            return QueueMutationAsync(repository, operationName, async cancellationToken =>
            {
                await RunOperationCoreAsync(repository, async token =>
                {
                    GitStagePlan plan;
                    if (selectedPath == null)
                    {
                        plan = await _workingTreeService.PrepareStageAllAsync(repository, token);
                    }
                    else
                    {
                        plan = await _workingTreeService.PrepareStageSelectedAsync(repository, selectedPath, token);
                    }

                    ValidateFileSelection(expectedFiles, plan.Files, false);
                    if (plan.LargeFiles.Count > 0)
                    {
                        Func<IReadOnlyList<GitLargeFileCandidate>, CancellationToken, Task<bool>> confirm = ConfirmLargeFilesRequested;
                        if (confirm == null)
                        {
                            throw new GitException("LocalLargeStageRequiresConfirmation", null, Array.Empty<object>());
                        }

                        bool accepted = await confirm(plan.LargeFiles, token);
                        if (accepted == false)
                        {
                            return false;
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    await _workingTreeService.ApplyStagePlanAsync(repository, plan, token);
                    return true;
                }, preferredPath, true, cancellationToken);
            });
        }

        private Task UnstageAllAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return Task.CompletedTask;
            }

            GitWorktreeFile[] expectedFiles = _stagedFiles.ToArray();
            string preferredPath = SelectedStagedFile?.Path;
            return QueueMutationAsync(repository, UnstageAllText, async cancellationToken =>
            {
                await RunOperationCoreAsync(repository, async token =>
                {
                    GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, token);
                    ValidateFileSelection(expectedFiles, status.Files.Where(file => file.IsStaged).ToArray(), true);
                    await _workingTreeService.UnstageAllAsync(repository, token);
                    return true;
                }, preferredPath, false, cancellationToken);
            });
        }

        private Task CommitAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return Task.CompletedTask;
            }

            string message = CommitMessage;
            bool amend = Amend;
            GitWorktreeFile[] expectedFiles = _stagedFiles.ToArray();
            return QueueMutationAsync(repository, CommitButtonText, async cancellationToken =>
            {
                string hash = null;
                await RunOperationCoreAsync(repository, async token =>
                {
                    GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, token);
                    ValidateFileSelection(expectedFiles, status.Files.Where(file => file.IsStaged).ToArray(), true);
                    hash = await _workingTreeService.CommitAsync(repository, message, amend, token);
                    if (IsCurrentRepository(repository) == false)
                    {
                        return true;
                    }

                    if (CommitMessage == message)
                    {
                        if (Amend == amend)
                        {
                            Amend = false;
                            CommitMessage = string.Empty;
                        }
                    }

                    return true;
                }, null, false, cancellationToken);
                if (IsCurrentRepository(repository) == true)
                {
                    StatusText = _stringHelper.Format("LocalCommittedNotice", hash);
                    Committed?.Invoke(hash);
                }
            });
        }

        private async Task RunOperationCoreAsync(GitRepository repository, Func<CancellationToken, Task<bool>> operation, string preferredPath, bool preferStaged, CancellationToken cancellationToken)
        {
            bool active = IsCurrentRepository(repository);
            if (active == true)
            {
                IsBusy = true;
                ErrorText = string.Empty;
            }

            try
            {
                bool changed = await operation(cancellationToken);
                if (changed == true)
                {
                    if (IsCurrentRepository(repository) == true)
                    {
                        await RefreshAsync(preferredPath, preferStaged);
                    }
                }
            }
            catch
            {
                if (IsCurrentRepository(repository) == true)
                {
                    await RefreshAsync();
                }
                throw;
            }
            finally
            {
                if (active == true)
                {
                    if (IsCurrentRepository(repository) == true)
                    {
                        IsBusy = false;
                    }
                }
            }
        }

        private async Task QueueMutationAsync(GitRepository repository, string operationName, Func<CancellationToken, Task> operation)
        {
            bool resultRecorded = false;
            try
            {
                await _operationQueue.EnqueueAsync(repository.RootPath, operationName,
                    cancellationToken => UiQueuedOperation.RunAsync(async () =>
                    {
                        _queuedMutationErrors.Remove(repository.RootPath);
                        try
                        {
                            await operation(cancellationToken);
                            resultRecorded = true;
                        }
                        catch (Exception exception)
                        {
                            resultRecorded = true;
                            RememberMutationError(repository, $"{operationName}: {_errorLocalizer.GetDisplayMessage(exception)}");
                            throw;
                        }
                    }));
            }
            catch (Exception exception)
            {
                if (resultRecorded == true)
                {
                    return;
                }

                await UiQueuedOperation.RunAsync(() =>
                {
                    RememberMutationError(repository, $"{operationName}: {_errorLocalizer.GetDisplayMessage(exception)}");
                    return Task.CompletedTask;
                });
            }
        }

        private void RememberMutationError(GitRepository repository, string message)
        {
            _queuedMutationErrors[repository.RootPath] = message;
            if (IsCurrentRepository(repository) == true)
            {
                ErrorText = message;
            }
        }

        private void ShowMutationCommandError(Exception exception)
        {
            ErrorText = _errorLocalizer.GetDisplayMessage(exception);
        }

        private bool IsCurrentRepository(GitRepository repository)
        {
            if (_repository == null)
            {
                return false;
            }

            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }

            return string.Equals(_repository.RootPath, repository.RootPath, comparison);
        }

        private async Task<GitWorktreeFile> ValidateFileSelectionAsync(GitRepository repository, GitWorktreeFile expected, bool staged, CancellationToken cancellationToken)
        {
            GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, cancellationToken);
            GitWorktreeFile current = status.Files.FirstOrDefault(file => file.Path == expected.Path);
            if (current == null)
            {
                throw new GitException("LocalSelectedFileChanged", null, expected.Path);
            }

            ValidateFileSelection([expected], [current], staged);
            return current;
        }

        private void ValidateFileSelection(IReadOnlyList<GitWorktreeFile> expected, IReadOnlyList<GitWorktreeFile> current, bool staged)
        {
            if (expected.Count != current.Count)
            {
                throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
            }

            Dictionary<string, GitWorktreeFile> currentFiles = current.ToDictionary(file => file.Path, StringComparer.Ordinal);
            foreach (GitWorktreeFile file in expected)
            {
                if (currentFiles.TryGetValue(file.Path, out GitWorktreeFile item) == false)
                {
                    throw new GitException("LocalSelectedFileChanged", null, file.Path);
                }

                bool available = item.IsUnstaged;
                if (staged == true)
                {
                    available = item.IsStaged;
                }
                if (available == false)
                {
                    throw new GitException("LocalSelectedFileUnavailable", null, file.Path);
                }

                if (file.OriginalPath != item.OriginalPath)
                {
                    throw new GitException("LocalSelectedFileChanged", null, file.Path);
                }
                if (file.IndexStatus != item.IndexStatus)
                {
                    throw new GitException("LocalIndexChanged", null, file.Path);
                }
                if (file.WorktreeStatus != item.WorktreeStatus)
                {
                    throw new GitException("LocalWorkingFileChanged", null, file.Path);
                }
            }
        }

        private void OpenResolve()
        {
            GitWorktreeFile file = SelectedConflictFile;
            if (file == null)
            {
                file = _conflictFiles.FirstOrDefault();
            }

            if (file == null)
            {
                return;
            }
            if (IsBusy == true)
            {
                return;
            }

            ResolveRequested?.Invoke(file.Path);
        }

        private async Task LoadAmendMessageAsync(GitRepository repository, int amendVersion)
        {
            if (repository == null)
            {
                Amend = false;
                return;
            }

            try
            {
                string message = await _workingTreeService.GetHeadCommitMessageAsync(repository);
                if (amendVersion != _amendVersion)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (Amend == false)
                {
                    return;
                }

                CommitMessage = message;
                SetAmendLoading(false);
            }
            catch (Exception exception)
            {
                if (amendVersion != _amendVersion)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (Amend == false)
                {
                    return;
                }

                Amend = false;
                ErrorText = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private void SetAmendLoading(bool value)
        {
            if (_isLoadingAmend == value)
            {
                return;
            }

            _isLoadingAmend = value;
            OnPropertyChanged(nameof(IsCommitMessageEditable));
            OnPropertyChanged(nameof(CommitHint));
            CommitCommand.NotifyCanExecuteChanged();
        }

        private bool HasCommitSummary()
        {
            if (string.IsNullOrWhiteSpace(CommitMessage) == true)
            {
                return false;
            }

            string firstLine = CommitMessage.Split('\n')[0];
            return string.IsNullOrWhiteSpace(firstLine) == false;
        }

        private void UpdateCounts()
        {
            OnPropertyChanged(nameof(HasStagedFiles));
            OnPropertyChanged(nameof(HasUnstagedFiles));
            OnPropertyChanged(nameof(DiscardSelectionText));
            OnPropertyChanged(nameof(HasConflicts));
            OnPropertyChanged(nameof(ConflictsHeadingText));
            OnPropertyChanged(nameof(CommitHint));
            CommitCommand.NotifyCanExecuteChanged();
            NotifyCommandStates();
        }

        private void NotifyCommandStates()
        {
            RefreshCommand.NotifyCanExecuteChanged();
            StageSelectedCommand.NotifyCanExecuteChanged();
            UnstageSelectedCommand.NotifyCanExecuteChanged();
            StageAllCommand.NotifyCanExecuteChanged();
            DiscardSelectedCommand.NotifyCanExecuteChanged();
            UnstageAllCommand.NotifyCanExecuteChanged();
            CommitCommand.NotifyCanExecuteChanged();
            OpenResolveCommand.NotifyCanExecuteChanged();
        }

        private bool CanRefresh() { return _repository != null && IsBusy == false; }

        private bool CanStageSelected()
        {
            if (_repository == null)
            {
                return false;
            }

            return SelectedUnstagedFile != null;
        }

        private bool CanDiscardSelected()
        {
            if (_repository == null)
            {
                return false;
            }

            List<GitWorktreeFile> files = GetDiscardSelection();
            if (files.Count == 0)
            {
                return false;
            }

            if (files.Any(file => file.IsConflict) == true)
            {
                return false;
            }

            return files.All(file => file.IsUnstaged);
        }

        private void ApplyWorktreeStatus(GitWorktreeStatus status, string preferredPath = null, bool preferStaged = false)
        {
            _workingStatus = status;
            Stashes.SetWorktreeStatus(_repository, status);
            string selectedPath = SelectedUnstagedFile?.Path ?? SelectedStagedFile?.Path ?? SelectedConflictFile?.Path;
            bool selectedStaged = SelectedStagedFile != null;
            bool selectedConflict = SelectedConflictFile != null;
            if (preferredPath != null)
            {
                selectedPath = preferredPath;
                selectedStaged = preferStaged;
                selectedConflict = false;
            }

            SelectedUnstagedFile = null;
            _selectedUnstagedFiles.Clear();
            OnPropertyChanged(nameof(DiscardSelectionText));
            SelectedStagedFile = null;
            SelectedConflictFile = null;
            _unstagedFiles.Clear();
            _stagedFiles.Clear();
            _conflictFiles.Clear();
            foreach (GitWorktreeFile file in status.Files)
            {
                string statusText = _stringHelper.GetString(file.StatusCode);
                if (file.IsPartiallyStaged == true)
                {
                    statusText = _stringHelper.Format("WorktreeStatusPartiallyStaged", statusText);
                }
                file.DisplayStatusText = statusText;

                if (file.IsConflict == true)
                {
                    _conflictFiles.Add(file);
                    continue;
                }

                if (file.IsUnstaged == true)
                {
                    _unstagedFiles.Add(file);
                }

                if (file.IsStaged == true)
                {
                    _stagedFiles.Add(file);
                }
            }

            UpdateCounts();
            GitWorktreeFile selected = null;
            if (selectedPath != null)
            {
                if (selectedConflict == true)
                {
                    selected = _conflictFiles.FirstOrDefault(file => file.Path == selectedPath);
                    SelectedConflictFile = selected;
                }
                else if (selectedStaged == true)
                {
                    selected = _stagedFiles.FirstOrDefault(file => file.Path == selectedPath);
                    SelectedStagedFile = selected;
                }
                else
                {
                    selected = _unstagedFiles.FirstOrDefault(file => file.Path == selectedPath);
                    SelectedUnstagedFile = selected;
                }

                if (selected == null)
                {
                    selected = _unstagedFiles.FirstOrDefault(file => file.Path == selectedPath);
                    SelectedUnstagedFile = selected;
                }

                if (selected == null)
                {
                    selected = _stagedFiles.FirstOrDefault(file => file.Path == selectedPath);
                    SelectedStagedFile = selected;
                }
            }

            if (selected == null)
            {
                PreviewText = string.Empty;
                PreviewIsUnifiedDiff = false;
                PreviewDescription = _stringHelper.GetString("LocalPreviewPrompt");
            }

            StatusText = _stringHelper.Format("LocalChangesStatusCount", _unstagedFiles.Count, _stagedFiles.Count, _conflictFiles.Count);
        }

        private bool CanUnstageSelected()
        {
            if (_repository == null)
            {
                return false;
            }

            return SelectedStagedFile != null;
        }

        private bool CanStageAll()
        {
            if (_repository == null)
            {
                return false;
            }

            return _unstagedFiles.Count > 0;
        }

        private bool CanUnstageAll()
        {
            if (_repository == null)
            {
                return false;
            }

            return _stagedFiles.Count > 0;
        }

        private bool CanCommit()
        {
            if (_repository == null)
            {
                return false;
            }

            if (_isLoadingAmend == true)
            {
                return false;
            }

            if (HasCommitSummary() == false)
            {
                return false;
            }

            if (_stagedFiles.Count == 0)
            {
                return false;
            }

            if (_conflictFiles.Count > 0)
            {
                return false;
            }

            return true;
        }

        private bool CanOpenResolve() { return HasConflicts == true && IsBusy == false; }
    }
}
