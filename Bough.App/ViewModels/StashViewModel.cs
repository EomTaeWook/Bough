using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.Core.Git;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;
using Bough.App.Internals;
using Bough.App.Presenters;

namespace Bough.App.ViewModels
{
    public class StashViewModel : ViewModelBase
    {
        private readonly GitStashService _stashService;
        private readonly GitWorkingTreeService _workingTreeService;
        private readonly StashMutationPresenter _mutationPresenter;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly ObservableCollection<GitStashEntry> _stashes;
        private readonly ObservableCollection<string> _previewFiles;
        private readonly ObservableCollection<string> _stashTargets;
        private GitRepository _repository;
        private GitWorktreeStatus _workingStatus;
        private GitStashEntry _selectedStash;
        private GitStashEntry _pendingDrop;
        private string _stashMessage;
        private string _previewDiff;
        private string _errorText;
        private string _statusText;
        private string _firstConflictPath;
        private bool _includeUntracked;
        private bool _isBusy;
        private bool _isMutating;
        private int _requestVersion;
        private int _previewVersion;

        public StashViewModel(GitStashService stashService, GitWorkingTreeService workingTreeService, GitOperationQueue operationQueue,
            StringHelper stringHelper, GitErrorLocalizer errorLocalizer)
        {
            if (stashService == null)
            {
                throw new ArgumentNullException(nameof(stashService));
            }

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

            _stashService = stashService;
            _workingTreeService = workingTreeService;
            _mutationPresenter = new StashMutationPresenter(stashService, workingTreeService, operationQueue);
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            _stashes = [];
            _previewFiles = [];
            _stashTargets = [];
            Stashes = new ReadOnlyObservableCollection<GitStashEntry>(_stashes);
            PreviewFiles = new ReadOnlyObservableCollection<string>(_previewFiles);
            StashTargets = new ReadOnlyObservableCollection<string>(_stashTargets);
            _stashMessage = string.Empty;
            _previewDiff = string.Empty;
            _errorText = string.Empty;
            _statusText = string.Empty;
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
            RequestDropCommand = new RelayCommand(RequestDrop, CanUseSelected);
            CancelDropCommand = new RelayCommand(CancelDrop);
            OpenResolveCommand = new RelayCommand(OpenResolve, CanOpenResolve);
        }

        public event Action<string> ResolveRequested;

        public ReadOnlyObservableCollection<GitStashEntry> Stashes { get; }

        public ReadOnlyObservableCollection<string> PreviewFiles { get; }

        public ReadOnlyObservableCollection<string> StashTargets { get; }

        public AsyncRelayCommand RefreshCommand { get; }

        public RelayCommand RequestDropCommand { get; }

        public RelayCommand CancelDropCommand { get; }

        public RelayCommand OpenResolveCommand { get; }

        public string HeadingText { get { return _stringHelper.GetString("StashesHeading"); } }

        public string SaveHeadingText { get { return _stringHelper.GetString("CreateStashHeading"); } }

        public string MessageLabelText { get { return _stringHelper.GetString("StashMessageLabel"); } }

        public string IncludeUntrackedText { get { return _stringHelper.GetString("StashIncludeUntracked"); } }

        public string SaveButtonText { get { return _stringHelper.GetString("CreateStashButton"); } }

        public string ApplyButtonText { get { return _stringHelper.GetString("ApplyStashButton"); } }

        public string PopButtonText { get { return _stringHelper.GetString("PopStashButton"); } }

        public string RequestDropText { get { return _stringHelper.GetString("RequestDropStashButton"); } }

        public string ConfirmDropText { get { return _stringHelper.GetString("ConfirmDropStashButton"); } }

        public string CancelDropText { get { return _stringHelper.GetString("CancelDropStashButton"); } }

        public string RefreshText { get { return _stringHelper.GetString("RefreshButton"); } }

        public string EmptyText { get { return _stringHelper.GetString("NoStashes"); } }

        public string PreviewFilesHeadingText { get { return _stringHelper.GetString("StashChangedFiles"); } }

        public string PreviewDiffHeadingText { get { return _stringHelper.GetString("StashDiffHeading"); } }

        public string OpenResolveText { get { return _stringHelper.GetString("OpenResolve"); } }

        public string TargetSummaryText
        {
            get
            {
                return _stringHelper.Format("StashTargetSummary", _stashTargets.Count);
            }
        }

        public bool HasStashes { get { return _stashes.Count > 0; } }

        public bool HasNoStashes { get { return _stashes.Count == 0; } }

        public bool HasError { get { return ErrorText.Length > 0; } }

        public bool HasDropConfirmation { get { return _pendingDrop != null; } }

        public bool HasConflicts { get { return _firstConflictPath != null; } }

        public bool CanSaveStash { get { return CanSave(); } }

        public bool CanUseSelectedStash { get { return CanUseSelected(); } }

        public bool CanConfirmDropStash { get { return CanConfirmDrop(); } }

        public string DropConfirmationText
        {
            get
            {
                if (_pendingDrop == null)
                {
                    return string.Empty;
                }

                return _stringHelper.Format("StashDropConfirmation", _pendingDrop.Name, _pendingDrop.Message);
            }
        }

        public string StashMessage
        {
            get { return _stashMessage; }
            set { SetProperty(ref _stashMessage, value); }
        }

        public bool IncludeUntracked
        {
            get { return _includeUntracked; }
            set
            {
                if (SetProperty(ref _includeUntracked, value) == true)
                {
                    UpdateTargets();
                }
            }
        }

        public GitStashEntry SelectedStash
        {
            get { return _selectedStash; }
            set
            {
                if (SetProperty(ref _selectedStash, value) == false)
                {
                    return;
                }

                CancelDrop();
                _previewVersion++;
                _previewFiles.Clear();
                PreviewDiff = string.Empty;
                NotifyCommandStates();
                if (value != null)
                {
                    _ = LoadPreviewAsync(value, _previewVersion);
                }
            }
        }

        public string PreviewDiff
        {
            get { return _previewDiff; }
            private set { SetProperty(ref _previewDiff, value); }
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

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    NotifyCommandStates();
                }
            }
        }

        public void Clear()
        {
            _requestVersion++;
            _previewVersion++;
            _repository = null;
            _workingStatus = null;
            SelectedStash = null;
            _stashes.Clear();
            _previewFiles.Clear();
            _stashTargets.Clear();
            PreviewDiff = string.Empty;
            ErrorText = string.Empty;
            StatusText = string.Empty;
            _firstConflictPath = null;
            _isMutating = false;
            IsBusy = false;
            OnPropertyChanged(nameof(HasConflicts));
            UpdateCollectionState();
        }

        public void InvalidatePendingRequests()
        {
            _requestVersion++;
            _previewVersion++;
            if (_isMutating == false)
            {
                IsBusy = false;
            }
        }

        public async Task LoadAsync(GitRepository repository)
        {
            BindRepository(repository);
            if (_workingStatus != null)
            {
                await LoadAsync(repository, _workingStatus);
                return;
            }

            await RefreshAsync();
        }

        public async Task LoadAsync(GitRepository repository, GitWorktreeStatus status)
        {
            SetWorktreeStatus(repository, status);
            _requestVersion++;
            await RefreshCoreAsync(repository, _requestVersion, status);
        }

        public void SetWorktreeStatus(GitRepository repository, GitWorktreeStatus status)
        {
            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }

            BindRepository(repository);
            _workingStatus = status;
            _firstConflictPath = status.Files.FirstOrDefault(file => file.IsConflict)?.Path;
            OnPropertyChanged(nameof(HasConflicts));
            UpdateTargets();
            NotifyCommandStates();
        }

        public void BindRepository(GitRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            if (_repository != null)
            {
                if (_repository.RootPath == repository.RootPath)
                {
                    _repository = repository;
                    NotifyCommandStates();
                    return;
                }
            }

            Clear();
            _repository = repository;
            StashMessage = string.Empty;
            IncludeUntracked = false;
            NotifyCommandStates();
        }

        public async Task RefreshAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            _requestVersion++;
            await RefreshCoreAsync(repository, _requestVersion, null);
        }

        private async Task RefreshCoreAsync(GitRepository repository, int requestVersion, GitWorktreeStatus knownStatus)
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                IReadOnlyList<GitStashEntry> entries = await _stashService.GetStashesAsync(repository);
                if (requestVersion != _requestVersion)
                {
                    return;
                }

                GitWorktreeStatus status = knownStatus;
                if (status == null)
                {
                    status = await _workingTreeService.GetStatusAsync(repository);
                }

                if (requestVersion != _requestVersion)
                {
                    return;
                }

                ApplyEntries(entries);
                SetWorktreeStatus(repository, status);
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
                    if (_isMutating == false)
                    {
                        IsBusy = false;
                    }
                }
            }
        }

        private async Task LoadPreviewAsync(GitStashEntry entry, int previewVersion)
        {
            GitRepository repository = _repository;
            int requestVersion = _requestVersion;
            if (repository == null)
            {
                return;
            }

            try
            {
                GitStashPreview preview = await _stashService.GetPreviewAsync(repository, entry);
                if (previewVersion != _previewVersion)
                {
                    return;
                }
                if (requestVersion != _requestVersion)
                {
                    return;
                }

                foreach (string file in preview.Files)
                {
                    _previewFiles.Add(file);
                }

                PreviewDiff = preview.Diff;
            }
            catch (Exception exception)
            {
                if (previewVersion == _previewVersion)
                {
                    if (requestVersion == _requestVersion)
                    {
                        ErrorText = _errorLocalizer.GetDisplayMessage(exception);
                    }
                }
            }
        }

        private void UpdateTargets()
        {
            _stashTargets.Clear();
            if (_workingStatus != null)
            {
                foreach (GitWorktreeFile file in _workingStatus.Files)
                {
                    if (file.IsConflict == true)
                    {
                        continue;
                    }

                    if (file.IsUntracked == true && IncludeUntracked == false)
                    {
                        continue;
                    }

                    _stashTargets.Add(file.DisplayPath);
                }
            }

            OnPropertyChanged(nameof(TargetSummaryText));
            OnPropertyChanged(nameof(CanSaveStash));
        }

        public Task<StashMutationResult> SaveAsync()
        {
            return RunMutationAsync(StashMutationKind.Save, null);
        }

        public Task<StashMutationResult> ApplyAsync()
        {
            return RunMutationAsync(StashMutationKind.Apply, SelectedStash);
        }

        public Task<StashMutationResult> PopAsync()
        {
            return RunMutationAsync(StashMutationKind.Pop, SelectedStash);
        }

        private void RequestDrop()
        {
            _pendingDrop = SelectedStash;
            OnPropertyChanged(nameof(HasDropConfirmation));
            OnPropertyChanged(nameof(DropConfirmationText));
            OnPropertyChanged(nameof(CanConfirmDropStash));
        }

        public Task<StashMutationResult> ConfirmDropAsync()
        {
            GitStashEntry entry = _pendingDrop;
            CancelDrop();
            return RunMutationAsync(StashMutationKind.Drop, entry);
        }

        private void CancelDrop()
        {
            if (_pendingDrop == null)
            {
                return;
            }

            _pendingDrop = null;
            OnPropertyChanged(nameof(HasDropConfirmation));
            OnPropertyChanged(nameof(DropConfirmationText));
            OnPropertyChanged(nameof(CanConfirmDropStash));
        }

        private async Task<StashMutationResult> RunMutationAsync(StashMutationKind kind, GitStashEntry entry)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return new StashMutationResult(null, kind, false, false, false, null, string.Empty);
            }

            string operationName;
            switch (kind)
            {
                case StashMutationKind.Save:
                    operationName = SaveButtonText;
                    break;
                case StashMutationKind.Apply:
                    operationName = ApplyButtonText;
                    break;
                case StashMutationKind.Pop:
                    operationName = PopButtonText;
                    break;
                case StashMutationKind.Drop:
                    operationName = ConfirmDropText;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            string message = StashMessage;
            bool includeUntracked = IncludeUntracked;
            GitWorktreeFile[] expectedFiles = _workingStatus?.Files.ToArray();
            return await _mutationPresenter.RunAsync(this, repository, kind, entry, message, includeUntracked, expectedFiles, operationName);
        }

        internal bool BeginMutation(GitRepository repository)
        {
            bool active = IsCurrentRepository(repository);
            if (active == true)
            {
                _isMutating = true;
                IsBusy = true;
                ErrorText = string.Empty;
            }

            return active;
        }

        internal int InvalidateMutationReads(GitRepository repository, bool succeeded, bool worktreeMayHaveChanged, bool stashesMayHaveChanged)
        {
            bool invalidateReads = succeeded;
            if (worktreeMayHaveChanged == true)
            {
                invalidateReads = true;
            }
            if (stashesMayHaveChanged == true)
            {
                invalidateReads = true;
            }
            if (invalidateReads == true)
            {
                if (IsCurrentRepository(repository) == true)
                {
                    _requestVersion++;
                }
            }

            return _requestVersion;
        }

        internal void ApplyMutationSuccess(GitRepository repository, int requestVersion, StashMutationKind kind, string entryName, string message)
        {
            if (IsCurrentRepository(repository) == false)
            {
                return;
            }
            if (requestVersion != _requestVersion)
            {
                return;
            }

            if (kind == StashMutationKind.Save)
            {
                StatusText = _stringHelper.Format("StashSavedNotice", entryName);
                if (StashMessage == message)
                {
                    StashMessage = string.Empty;
                }
                return;
            }
            if (kind == StashMutationKind.Apply)
            {
                StatusText = _stringHelper.Format("StashAppliedNotice", entryName);
                return;
            }
            if (kind == StashMutationKind.Pop)
            {
                StatusText = _stringHelper.Format("StashPoppedNotice", entryName);
                return;
            }

            StatusText = _stringHelper.Format("StashDroppedNotice", entryName);
        }

        internal void ApplyMutationEntries(GitRepository repository, int requestVersion, IReadOnlyList<GitStashEntry> entries)
        {
            if (IsCurrentRepository(repository) == false)
            {
                return;
            }
            if (requestVersion != _requestVersion)
            {
                return;
            }

            ApplyEntries(entries);
        }

        internal void ApplyMutationStatus(GitRepository repository, int requestVersion, GitWorktreeStatus status)
        {
            if (IsCurrentRepository(repository) == false)
            {
                return;
            }
            if (requestVersion != _requestVersion)
            {
                return;
            }

            SetWorktreeStatus(repository, status);
        }

        internal string GetMutationErrorText(Exception exception)
        {
            return _errorLocalizer.GetDisplayMessage(exception);
        }

        internal void ApplyMutationError(GitRepository repository, string errorText)
        {
            if (IsCurrentRepository(repository) == true)
            {
                ErrorText = errorText;
            }
        }

        internal void EndMutation(GitRepository repository, bool active)
        {
            if (active == false)
            {
                return;
            }
            if (IsCurrentRepository(repository) == false)
            {
                return;
            }

            _isMutating = false;
            IsBusy = false;
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

        private void ApplyEntries(IReadOnlyList<GitStashEntry> entries)
        {
            string selectedHash = SelectedStash?.CommitHash;
            SelectedStash = null;
            _stashes.Clear();
            foreach (GitStashEntry stash in entries)
            {
                _stashes.Add(stash);
            }

            UpdateCollectionState();
            if (selectedHash != null)
            {
                SelectedStash = _stashes.FirstOrDefault(stash => stash.CommitHash == selectedHash);
            }
        }

        private void OpenResolve()
        {
            if (_firstConflictPath != null)
            {
                ResolveRequested?.Invoke(_firstConflictPath);
            }
        }

        private void UpdateCollectionState()
        {
            OnPropertyChanged(nameof(HasStashes));
            OnPropertyChanged(nameof(HasNoStashes));
            NotifyCommandStates();
        }

        private void NotifyCommandStates()
        {
            RefreshCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanSaveStash));
            OnPropertyChanged(nameof(CanUseSelectedStash));
            OnPropertyChanged(nameof(CanConfirmDropStash));
            RequestDropCommand.NotifyCanExecuteChanged();
            OpenResolveCommand.NotifyCanExecuteChanged();
        }

        private bool CanRefresh() { return _repository != null && IsBusy == false; }

        private bool CanSave() { return _repository != null && _stashTargets.Count > 0 && HasConflicts == false; }

        private bool CanUseSelected() { return SelectedStash != null; }

        private bool CanConfirmDrop() { return _pendingDrop != null && _repository != null; }

        private bool CanOpenResolve() { return HasConflicts == true && IsBusy == false; }
    }
}
