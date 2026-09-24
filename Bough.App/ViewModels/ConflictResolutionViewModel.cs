using Bough.App.Localization;
using Bough.Core.Conflicts;
using Bough.Core.Conflicts.Exceptions;
using Bough.Core.Git;
using Bough.Core.Internals;
using Avalonia.Threading;
using Dignus.DependencyInjection.Attributes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

    public sealed class ConflictStageResult
    {
        public ConflictStageResult(GitRepository repository, string path, ConflictStageOutcome outcome, string message, Exception exception = null)
        {
            Repository = repository;
            Path = path;
            Outcome = outcome;
            Message = message;
            Exception = exception;
        }

        public GitRepository Repository { get; }
        public string Path { get; }
        public ConflictStageOutcome Outcome { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public bool Succeeded { get { return Outcome == ConflictStageOutcome.Succeeded; } }
        public bool WasStaged { get { return Outcome == ConflictStageOutcome.Succeeded || Outcome == ConflictStageOutcome.RefreshFailed; } }
    }

    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class ConflictResolutionViewModel : ViewModelBase
    {
        private readonly GitRepositoryService _repositoryService;
        private readonly GitOperationQueue _operationQueue;
        private readonly ConflictParser _parser;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly StringComparison _pathComparison;
        private readonly Dictionary<int, ResolutionChoiceType> _choices;
        private IConflictStageCompletion _completion;
        private GitRepository _repository;
        private GitConflictFile _currentConflict;
        private ConflictDocument _document;
        private ConflictFileItem _selectedFile;
        private int _currentHunkIndex;
        private int _requestVersion;
        private int _loadVersion;
        private string _currentFilePath;
        private string _oursSource;
        private string _theirsSource;
        private string _oursText;
        private string _theirsText;
        private string _baseText;
        private string _resultText;
        private string _loadedResultText;
        private string _statusMessage;
        private string _currentChoiceText;
        private string _queueStatusText = string.Empty;
        private bool _hasDocument;
        private bool _isBusy;
        private int _activeSaveCount;

        public ConflictResolutionViewModel(GitRepositoryService repositoryService, GitOperationQueue operationQueue, ConflictParser parser, StringHelper stringHelper, GitErrorLocalizer errorLocalizer)
        {
            _repositoryService = repositoryService;
            _operationQueue = operationQueue;
            _parser = parser;
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            _pathComparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                _pathComparison = StringComparison.OrdinalIgnoreCase;
            }
            _choices = [];
            ConflictFiles = [];
            _currentFilePath = _stringHelper.GetString("SelectConflictFile");
            _oursSource = _stringHelper.GetString("CurrentChange");
            _theirsSource = _stringHelper.GetString("IncomingChange");
            _oursText = string.Empty;
            _theirsText = string.Empty;
            _baseText = string.Empty;
            _resultText = string.Empty;
            _loadedResultText = string.Empty;
            _statusMessage = _stringHelper.GetString("OpenRepositoryToFindConflicts");
            _currentChoiceText = _stringHelper.GetString("NoChoiceYet");
            _operationQueue.StateChanged += OnGitOperationQueueStateChanged;

            PreviousHunkCommand = new RelayCommand(PreviousHunk, CanMoveToPreviousHunk);
            NextHunkCommand = new RelayCommand(NextHunk, CanMoveToNextHunk);
            ChooseOursCommand = new RelayCommand(ChooseOurs, CanResolveCurrentHunk);
            ChooseTheirsCommand = new RelayCommand(ChooseTheirs, CanResolveCurrentHunk);
            ChooseBothCommand = new RelayCommand(ChooseBoth, CanResolveCurrentHunk);
            RemoveBothCommand = new RelayCommand(RemoveBoth, CanResolveCurrentHunk);
            SaveAndStageCommand = new RelayCommand(() => _ = SaveAndStageAsync(), CanSaveAndStage);
        }

        public ObservableCollection<ConflictFileItem> ConflictFiles { get; }
        public RelayCommand PreviousHunkCommand { get; }
        public RelayCommand NextHunkCommand { get; }
        public RelayCommand ChooseOursCommand { get; }
        public RelayCommand ChooseTheirsCommand { get; }
        public RelayCommand ChooseBothCommand { get; }
        public RelayCommand RemoveBothCommand { get; }
        public RelayCommand SaveAndStageCommand { get; }
        public bool IsSaving { get { return _activeSaveCount > 0; } }
        public string QueueStatusText { get { return _queueStatusText; } }
        public bool HasQueueStatus { get { return _queueStatusText.Length > 0; } }
        public bool HasUnsavedConflictEdits { get { return HasDocument && ResultText != _loadedResultText; } }
        public string FilesToResolveText { get { return _stringHelper.GetString("FilesToResolve"); } }
        public string WindowTitle { get { return _stringHelper.GetString("ConflictWindowTitle"); } }
        public string DiscardResolutionTitle { get { return _stringHelper.GetString("DiscardResolutionTitle"); } }
        public string DiscardResolutionCloseMessage { get { return _stringHelper.GetString("DiscardResolutionCloseConflictMessage"); } }
        public string DiscardResolutionConfirmText { get { return _stringHelper.GetString("DiscardResolutionConfirm"); } }
        public string PreviousButtonText { get { return _stringHelper.GetString("PreviousButton"); } }
        public string NextButtonText { get { return _stringHelper.GetString("NextButton"); } }
        public string FinalFileText { get { return _stringHelper.GetString("FinalFile"); } }
        public string FinalFileHintText { get { return _stringHelper.GetString("FinalFileHint"); } }
        public string SaveAndStageText { get { return _stringHelper.GetString("SaveAndStage"); } }
        public string CurrentChangeLabel { get { return _stringHelper.GetString("CurrentChange"); } }
        public string IncomingChangeLabel { get { return _stringHelper.GetString("IncomingChange"); } }
        public string UseCurrentChangeLabel { get { return _stringHelper.GetString("UseCurrentChange"); } }
        public string UseIncomingChangeLabel { get { return _stringHelper.GetString("UseIncomingChange"); } }
        public string UseBothLabel { get { return _stringHelper.GetString("UseBoth"); } }
        public string RemoveBothLabel { get { return _stringHelper.GetString("RemoveBoth"); } }
        public string ConflictCountText { get { return _stringHelper.Format("ConflictFileCount", ConflictFiles.Count); } }

        public string CurrentFilePath
        {
            get { return _currentFilePath; }
            private set { SetProperty(ref _currentFilePath, value); }
        }

        public string OursSource
        {
            get { return _oursSource; }
            private set { SetProperty(ref _oursSource, value); }
        }

        public string TheirsSource
        {
            get { return _theirsSource; }
            private set { SetProperty(ref _theirsSource, value); }
        }

        public string OursText
        {
            get { return _oursText; }
            private set { SetProperty(ref _oursText, value); }
        }

        public string TheirsText
        {
            get { return _theirsText; }
            private set { SetProperty(ref _theirsText, value); }
        }

        public string BaseText
        {
            get { return _baseText; }
            private set { SetProperty(ref _baseText, value); }
        }

        public string ResultText
        {
            get { return _resultText; }
            set
            {
                if (SetProperty(ref _resultText, value) == true)
                {
                    OnPropertyChanged(nameof(HasUnsavedConflictEdits));
                }
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { SetProperty(ref _statusMessage, value); }
        }

        public string CurrentChoiceText
        {
            get { return _currentChoiceText; }
            private set { SetProperty(ref _currentChoiceText, value); }
        }

        public bool HasDocument
        {
            get { return _hasDocument; }
            private set
            {
                if (SetProperty(ref _hasDocument, value) == true)
                {
                    NotifyCommandStates();
                }
            }
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

        public string CurrentHunkText
        {
            get
            {
                if (_document == null)
                {
                    return _stringHelper.GetString("NoConflictHunks");
                }
                if (_document.Hunks.Count == 0)
                {
                    return _stringHelper.GetString("NoConflictHunks");
                }
                ConflictHunk hunk = _document.Hunks[_currentHunkIndex];
                return _stringHelper.Format("ConflictHunkPosition", _currentHunkIndex + 1, _document.Hunks.Count, hunk.StartLine);
            }
        }

        public ConflictFileItem SelectedFile
        {
            get { return _selectedFile; }
            set
            {
                if (SetProperty(ref _selectedFile, value) == false)
                {
                    return;
                }
                if (value != null)
                {
                    _ = LoadConflictAsync(value);
                }
            }
        }

        public void SetCompletion(IConflictStageCompletion completion)
        {
            if (completion == null)
            {
                throw new ArgumentNullException(nameof(completion));
            }
            _completion = completion;
        }

        public void BindRepository(GitRepository repository)
        {
            if (ReferenceEquals(_repository, repository) == true)
            {
                return;
            }
            bool rootChanged = false;
            if (_repository == null)
            {
                rootChanged = true;
            }
            else if (repository == null)
            {
                rootChanged = true;
            }
            else if (_repository.RootPath != repository.RootPath)
            {
                rootChanged = true;
            }

            _repository = repository;
            if (repository == null)
            {
                SetQueueStatusText(string.Empty);
            }
            else
            {
                ApplyGitOperationQueueState(_operationQueue.GetState(repository.RootPath));
            }
            _requestVersion++;
            _loadVersion++;
            IsBusy = false;
            if (rootChanged == false)
            {
                return;
            }

            SetSelectedFile(null);
            ConflictFiles.Clear();
            OnPropertyChanged(nameof(ConflictCountText));
            ClearDocument();
            StatusMessage = _stringHelper.GetString("OpenRepositoryToFindConflicts");
        }

        public void DiscardClosedWindowEdits()
        {
            ClearDocument();
        }

        public async Task<bool> ApplyPathsAsync(GitRepository repository, IReadOnlyList<string> paths)
        {
            if (ReferenceEquals(_repository, repository) == false)
            {
                return false;
            }

            int request = ++_requestVersion;
            string previousPath = _selectedFile?.RelativePath ?? string.Empty;
            SetSelectedFile(null);
            ConflictFiles.Clear();
            foreach (string path in paths)
            {
                ConflictFiles.Add(new ConflictFileItem(path, _stringHelper));
            }
            OnPropertyChanged(nameof(ConflictCountText));
            NotifyCommandStates();

            if (ConflictFiles.Count == 0)
            {
                if (HasUnsavedConflictEdits == true)
                {
                    StatusMessage = _stringHelper.GetString("ConflictResolvedExternallyWithUnsavedEdits");
                    return true;
                }
                ClearDocument();
                StatusMessage = _stringHelper.GetString("NoUnresolvedConflicts");
                return true;
            }

            ConflictFileItem nextFile = ConflictFiles[0];
            ConflictFileItem previousFile = ConflictFiles.FirstOrDefault(file => file.RelativePath == previousPath);
            if (previousFile != null)
            {
                nextFile = previousFile;
            }
            SetSelectedFile(nextFile);
            if (HasUnsavedConflictEdits == true)
            {
                if (previousPath == nextFile.RelativePath)
                {
                    return true;
                }
            }
            await LoadConflictCoreAsync(nextFile);
            if (request != _requestVersion)
            {
                return false;
            }
            return ReferenceEquals(_repository, repository);
        }

        public void SelectPath(string path)
        {
            ConflictFileItem file = ConflictFiles.FirstOrDefault(item => item.RelativePath == path);
            if (file == null)
            {
                return;
            }
            SelectedFile = file;
        }

        private void SetSelectedFile(ConflictFileItem file)
        {
            _selectedFile = file;
            OnPropertyChanged(nameof(SelectedFile));
        }

        private async Task LoadConflictAsync(ConflictFileItem file)
        {
            await RunBusyAsync(async delegate
            {
                await LoadConflictCoreAsync(file);
            });
        }

        private async Task LoadConflictCoreAsync(ConflictFileItem file)
        {
            if (_repository == null)
            {
                return;
            }

            GitRepository repository = _repository;
            int loadVersion = ++_loadVersion;
            GitConflictFile conflict = await _repositoryService.LoadConflictAsync(repository, file.RelativePath,
                _stringHelper.GetString("IncomingChange"), _stringHelper.GetString("ConflictIncomingIndexStage3"));
            if (loadVersion != _loadVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            if (_selectedFile?.RelativePath != file.RelativePath)
            {
                return;
            }
            if (conflict.WorkingText.Contains('\0') == true)
            {
                throw new GitException("ConflictBinaryFileCannotEdit", null, file.RelativePath);
            }

            (ConflictDocument Document, string InitialResult) parsed = await Task.Run(() =>
            {
                ConflictDocument document = _parser.Parse(conflict.WorkingText,
                    _stringHelper.GetString("CurrentChange"), _stringHelper.GetString("IncomingChange"));
                string initialResult = document.Render(new Dictionary<int, ResolutionChoiceType>());
                return (document, initialResult);
            });
            if (loadVersion != _loadVersion)
            {
                return;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            if (_selectedFile?.RelativePath != file.RelativePath)
            {
                return;
            }
            if (parsed.Document.Hunks.Count == 0)
            {
                throw new ConflictParseException("ConflictTextMarkersMissing", file.RelativePath);
            }

            _document = parsed.Document;
            _currentConflict = conflict;
            _choices.Clear();
            _currentHunkIndex = 0;
            CurrentFilePath = conflict.RelativePath;
            OursSource = conflict.OursSource;
            TheirsSource = conflict.TheirsSource;
            BaseText = conflict.BaseText;
            ResultText = parsed.InitialResult;
            _loadedResultText = ResultText;
            OnPropertyChanged(nameof(HasUnsavedConflictEdits));
            HasDocument = true;
            ShowCurrentHunk();
            StatusMessage = _stringHelper.Format("ResolveConflictsPrompt", _document.Hunks.Count);
        }

        private void ChooseOurs() { ResolveCurrent(ResolutionChoiceType.Ours); }
        private void ChooseTheirs() { ResolveCurrent(ResolutionChoiceType.Theirs); }
        private void ChooseBoth() { ResolveCurrent(ResolutionChoiceType.Both); }
        private void RemoveBoth() { ResolveCurrent(ResolutionChoiceType.Remove); }

        private void ResolveCurrent(ResolutionChoiceType choice)
        {
            if (_document == null)
            {
                return;
            }
            ConflictHunk hunk = _document.Hunks[_currentHunkIndex];
            _choices[hunk.Id] = choice;
            ResultText = _document.Render(_choices);
            CurrentChoiceText = GetChoiceName(choice);
            int resolvedCount = _choices.Values.Count(selectedChoice => selectedChoice != ResolutionChoiceType.Unresolved);
            StatusMessage = _stringHelper.Format("ConflictsSelectedNotice", resolvedCount, _document.Hunks.Count);
        }

        private void PreviousHunk()
        {
            if (CanMoveToPreviousHunk() == false)
            {
                return;
            }
            _currentHunkIndex--;
            ShowCurrentHunk();
        }

        private void NextHunk()
        {
            if (CanMoveToNextHunk() == false)
            {
                return;
            }
            _currentHunkIndex++;
            ShowCurrentHunk();
        }

        private void ShowCurrentHunk()
        {
            if (_document == null)
            {
                return;
            }
            ConflictHunk hunk = _document.Hunks[_currentHunkIndex];
            OursText = hunk.OursText;
            TheirsText = hunk.TheirsText;
            CurrentChoiceText = _stringHelper.GetString("NoChoiceYet");
            if (_choices.TryGetValue(hunk.Id, out ResolutionChoiceType choice) == true)
            {
                CurrentChoiceText = GetChoiceName(choice);
            }
            OnPropertyChanged(nameof(CurrentHunkText));
            NotifyCommandStates();
        }

        public async Task<ConflictStageResult> SaveAndStageAsync()
        {
            GitRepository repository = _repository;
            GitConflictFile conflict = _currentConflict;
            if (_completion == null)
            {
                string message = _stringHelper.GetString("ConflictStageUnavailable");
                StatusMessage = message;
                return new ConflictStageResult(repository, conflict?.RelativePath ?? string.Empty, ConflictStageOutcome.Failed, message);
            }
            if (repository == null)
            {
                string message = _stringHelper.GetString("OpenRepositoryToFindConflicts");
                StatusMessage = message;
                return new ConflictStageResult(null, string.Empty, ConflictStageOutcome.Failed, message);
            }
            if (conflict == null)
            {
                string message = _stringHelper.GetString("SelectConflictFile");
                StatusMessage = message;
                return new ConflictStageResult(repository, string.Empty, ConflictStageOutcome.Failed, message);
            }
            int request = _requestVersion;
            string resultText = ResultText;
            string stagedPath = conflict.RelativePath;
            ConflictStageResult result;
            try
            {
                result = await _operationQueue.EnqueueAsync(repository.RootPath, $"{_stringHelper.GetString("SaveAndStage")} · {stagedPath}",
                    token => SaveAndStageCoreAsync(repository, conflict, resultText, request, token));
            }
            catch (OperationCanceledException exception)
            {
                result = new ConflictStageResult(repository, stagedPath, ConflictStageOutcome.Canceled,
                    _stringHelper.GetString("ConflictStageCanceled"), exception);
            }
            catch (Exception exception)
            {
                result = new ConflictStageResult(repository, stagedPath, ConflictStageOutcome.Failed,
                    _errorLocalizer.GetDisplayMessage(exception), exception);
            }

            if (IsCurrentStageRequest(repository, conflict, request) == false)
            {
                return result;
            }
            if (result.Succeeded)
            {
                _loadedResultText = resultText;
                OnPropertyChanged(nameof(HasUnsavedConflictEdits));
                try
                {
                    await _completion.CompleteConflictStageAsync(repository, stagedPath);
                }
                catch (Exception exception)
                {
                    result = new ConflictStageResult(repository, stagedPath, ConflictStageOutcome.RefreshFailed,
                        _stringHelper.Format("ConflictStageRefreshFailed", stagedPath, _errorLocalizer.GetDisplayMessage(exception)), exception);
                }
            }
            if (result.Outcome == ConflictStageOutcome.NoLongerConflicted)
            {
                result = await RefreshAfterStaleStageAsync(result);
            }
            if (result.Outcome == ConflictStageOutcome.FileChanged)
            {
                result = await RefreshAfterStaleStageAsync(result);
            }
            if (IsCurrentStageRequest(repository, conflict, request))
            {
                StatusMessage = result.Message;
            }
            return result;
        }

        private async Task<ConflictStageResult> RefreshAfterStaleStageAsync(ConflictStageResult result)
        {
            try
            {
                await _completion.RefreshConflictStateAsync(result.Repository);
            }
            catch (Exception exception)
            {
                string message = $"{result.Message} {_errorLocalizer.GetDisplayMessage(exception)}";
                return new ConflictStageResult(result.Repository, result.Path, result.Outcome, message, exception);
            }
            return result;
        }

        private async Task<ConflictStageResult> SaveAndStageCoreAsync(GitRepository repository, GitConflictFile conflict,
            string resultText, int request, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ownsView = IsCurrentStageRequest(repository, conflict, request);
            _activeSaveCount++;
            if (ownsView)
            {
                _loadVersion++;
                IsBusy = true;
            }
            try
            {
                IReadOnlyList<string> conflictPaths = await _repositoryService.GetConflictPathsAsync(repository, cancellationToken);
                bool stillConflicted = conflictPaths.Any(path => string.Equals(path, conflict.RelativePath, _pathComparison));
                if (stillConflicted == false)
                {
                    return new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.NoLongerConflicted,
                        _stringHelper.Format("ConflictStageNoLongerConflicted", conflict.RelativePath));
                }

                GitConflictFile currentConflict;
                try
                {
                    currentConflict = await _repositoryService.LoadConflictAsync(repository, conflict.RelativePath,
                        _stringHelper.GetString("IncomingChange"), _stringHelper.GetString("ConflictIncomingIndexStage3"), cancellationToken);
                }
                catch (FileNotFoundException exception)
                {
                    return new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.FileChanged,
                        _stringHelper.Format("ConflictStageFileChanged", conflict.RelativePath), exception);
                }
                catch (DirectoryNotFoundException exception)
                {
                    return new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.FileChanged,
                        _stringHelper.Format("ConflictStageFileChanged", conflict.RelativePath), exception);
                }
                if (currentConflict.OriginalContentHash != conflict.OriginalContentHash)
                {
                    return new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.FileChanged,
                        _stringHelper.Format("ConflictStageFileChanged", conflict.RelativePath));
                }

                ConflictDocument resultDocument;
                try
                {
                    resultDocument = await Task.Run(() => _parser.Parse(resultText,
                        _stringHelper.GetString("CurrentChange"), _stringHelper.GetString("IncomingChange")), cancellationToken);
                }
                catch (ConflictParseException exception)
                {
                    return new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.InvalidResolution,
                        _stringHelper.GetString("IncompleteConflictMarkers"), exception);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (resultDocument.Hunks.Count > 0)
                {
                    return new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.InvalidResolution,
                        _stringHelper.GetString("UnresolvedConflictMarkers"));
                }

                await _repositoryService.SaveAndStageAsync(repository, conflict, resultText, cancellationToken);
                return new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.Succeeded,
                    _stringHelper.Format("FileStaged", conflict.RelativePath));
            }
            finally
            {
                _activeSaveCount--;
                if (IsCurrentStageRequest(repository, conflict, request))
                {
                    IsBusy = false;
                }
            }
        }

        private bool IsCurrentStageRequest(GitRepository repository, GitConflictFile conflict, int request)
        {
            if (request != _requestVersion)
            {
                return false;
            }
            if (ReferenceEquals(_repository, repository) == false)
            {
                return false;
            }
            if (ReferenceEquals(_currentConflict, conflict) == false)
            {
                return false;
            }
            return true;
        }

        private async Task RunBusyAsync(Func<Task> action)
        {
            if (IsBusy == true)
            {
                return;
            }
            int request = _requestVersion;
            GitRepository repository = _repository;
            IsBusy = true;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
            finally
            {
                if (ReferenceEquals(_repository, repository) == true)
                {
                    IsBusy = false;
                }
            }
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
            if (string.Equals(_repository.RootPath, state.RepositoryRoot, _pathComparison) == false)
            {
                return;
            }
            string status = state.RunningOperationName;
            if (state.PendingCount > 0)
            {
                if (status.Length > 0)
                {
                    status += " · ";
                }
                status += $"+{state.PendingCount}";
            }
            SetQueueStatusText(status);
        }

        private void SetQueueStatusText(string status)
        {
            if (_queueStatusText == status)
            {
                return;
            }
            _queueStatusText = status;
            OnPropertyChanged(nameof(QueueStatusText));
            OnPropertyChanged(nameof(HasQueueStatus));
        }

        private void ClearDocument()
        {
            _document = null;
            _currentConflict = null;
            _choices.Clear();
            HasDocument = false;
            CurrentFilePath = _stringHelper.GetString("SelectConflictFile");
            OursText = string.Empty;
            TheirsText = string.Empty;
            BaseText = string.Empty;
            ResultText = string.Empty;
            _loadedResultText = string.Empty;
            OnPropertyChanged(nameof(HasUnsavedConflictEdits));
            CurrentChoiceText = _stringHelper.GetString("NoChoiceYet");
            OnPropertyChanged(nameof(CurrentHunkText));
        }

        private bool CanMoveToPreviousHunk() { return _currentHunkIndex > 0; }

        private bool CanMoveToNextHunk()
        {
            if (_document == null)
            {
                return false;
            }
            return _currentHunkIndex < _document.Hunks.Count - 1;
        }

        private bool CanResolveCurrentHunk() { return HasDocument; }

        private bool CanSaveAndStage()
        {
            if (HasDocument == false)
            {
                return false;
            }
            if (ConflictFiles.Count == 0)
            {
                return false;
            }
            return true;
        }

        private void NotifyCommandStates()
        {
            PreviousHunkCommand.NotifyCanExecuteChanged();
            NextHunkCommand.NotifyCanExecuteChanged();
            ChooseOursCommand.NotifyCanExecuteChanged();
            ChooseTheirsCommand.NotifyCanExecuteChanged();
            ChooseBothCommand.NotifyCanExecuteChanged();
            RemoveBothCommand.NotifyCanExecuteChanged();
            SaveAndStageCommand.NotifyCanExecuteChanged();
        }

        private string GetChoiceName(ResolutionChoiceType choice)
        {
            switch (choice)
            {
                case ResolutionChoiceType.Ours:
                    return _stringHelper.GetString("UseCurrentChange");
                case ResolutionChoiceType.Theirs:
                    return _stringHelper.GetString("UseIncomingChange");
                case ResolutionChoiceType.Both:
                    return _stringHelper.GetString("UseBoth");
                case ResolutionChoiceType.Remove:
                    return _stringHelper.GetString("RemoveBoth");
                default:
                    return _stringHelper.GetString("NoChoiceYet");
            }
        }
    }
}
