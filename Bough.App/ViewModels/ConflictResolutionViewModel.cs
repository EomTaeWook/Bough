using Bough.App.Commands;
using Bough.App.Interfaces;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.Core.Conflicts;
using Bough.Core.Git;
using Bough.Core.Internals;
using Avalonia.Threading;
using Dignus.DependencyInjection.Attributes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Bough.Core.Conflicts.Models;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;
using Bough.App.Internals;

namespace Bough.App.ViewModels
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class ConflictResolutionViewModel : ViewModelBase
    {
        private LocalizedText _statusMessageLocalization;
        private LocalizedText _currentChoiceTextLocalization;
        private LocalizedText _currentFilePathLocalization;
        internal void SetLocalizedStatusMessage(LocalizedText text)
        {
            StatusMessage = text.GetText(_stringHelper);
            _statusMessageLocalization = text;
        }

        private void SetLocalizedCurrentChoiceText(LocalizedText text)
        {
            CurrentChoiceText = text.GetText(_stringHelper);
            _currentChoiceTextLocalization = text;
        }

        private void SetLocalizedCurrentFilePath(LocalizedText text)
        {
            CurrentFilePath = text.GetText(_stringHelper);
            _currentFilePathLocalization = text;
        }

        private readonly GitOperationQueue _operationQueue;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly ConflictStagePresenter _stagePresenter;
        private readonly ConflictLoadPresenter _loadPresenter;

        private readonly StringComparison _pathComparison;
        private readonly Dictionary<int, ResolutionChoiceType> _choices;
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
        private string _renderedResultText;
        private string _statusMessage;
        private string _currentChoiceText;
        private ConflictStageResult _stageResult;
        private string _queueStatusText = string.Empty;
        private bool _hasDocument;
        private bool _isBusy;
        private bool _isRebaseConflict;
        private bool _isRevertInProgress;
        private bool _isRevertConflict;
        private bool _isResultFileDeleted;
        private bool _loadedResultFileDeleted;
        private int _activeSaveCount;

        public ConflictResolutionViewModel(GitRepositoryService repositoryService, GitOperationQueue operationQueue, ConflictParser parser, StringHelper stringHelper, GitErrorLocalizer errorLocalizer)
        {
            _operationQueue = operationQueue;
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            _stagePresenter = new ConflictStagePresenter(repositoryService, operationQueue, this);
            _loadPresenter = new ConflictLoadPresenter(repositoryService, parser, stringHelper, this);
            _pathComparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                _pathComparison = StringComparison.OrdinalIgnoreCase;
            }
            _choices = [];
            ConflictFiles = [];
            SetLocalizedCurrentFilePath(new LocalizedText("SelectConflictFile"));
            _oursSource = _stringHelper.GetString("CurrentChange");
            _theirsSource = _stringHelper.GetString("IncomingChange");
            _oursText = string.Empty;
            _theirsText = string.Empty;
            _baseText = string.Empty;
            _resultText = string.Empty;
            _loadedResultText = string.Empty;
            _renderedResultText = string.Empty;
            SetLocalizedStatusMessage(new LocalizedText("OpenRepositoryToFindConflicts"));
            SetLocalizedCurrentChoiceText(new LocalizedText("NoChoiceYet"));
            _operationQueue.StateChanged += OnGitOperationQueueStateChanged;

            PreviousHunkCommand = new RelayCommand(PreviousHunk, CanMoveToPreviousHunk);
            NextHunkCommand = new RelayCommand(NextHunk, CanMoveToNextHunk);
            ChooseOursCommand = new RelayCommand(ChooseOurs, CanResolveCurrentHunk);
            ChooseTheirsCommand = new RelayCommand(ChooseTheirs, CanResolveCurrentHunk);
            ChooseBothCommand = new RelayCommand(ChooseBoth, CanResolveCurrentHunk);
            RemoveBothCommand = new RelayCommand(RemoveBoth, CanResolveCurrentHunk);
        }

        public StringHelper Strings { get { return _stringHelper; } }
        public ObservableCollection<ConflictFileItem> ConflictFiles { get; }
        public RelayCommand PreviousHunkCommand { get; }
        public RelayCommand NextHunkCommand { get; }
        public RelayCommand ChooseOursCommand { get; }
        public RelayCommand ChooseTheirsCommand { get; }
        public RelayCommand ChooseBothCommand { get; }
        public RelayCommand RemoveBothCommand { get; }
        public bool IsSaving { get { return _activeSaveCount > 0; } }
        public bool HasStageResult { get { return StageResult != null; } }
        public bool HasGeneralStatus { get { return StageResult == null; } }
        public ConflictStageResult StageResult { get { return _stageResult; } }
        internal int ConflictRequestVersion { get { return _requestVersion; } }
        internal int ConflictLoadVersion
        {
            get { return _loadVersion; }
            set { _loadVersion = value; }
        }
        internal bool IsRevertOperationInProgress { get { return _isRevertInProgress; } }
        internal GitRepository CurrentRepository { get { return _repository; } }
        internal GitConflictFile StageConflict { get { return _currentConflict; } }
        public string QueueStatusText { get { return _queueStatusText; } }
        public bool HasQueueStatus { get { return _queueStatusText.Length > 0; } }
        public bool HasUnsavedConflictEdits
        {
            get { return HasDocument && (ResultText != _loadedResultText || IsResultFileDeleted != _loadedResultFileDeleted); }
        }
        public string OursFileStatusText
        {
            get
            {
                if (_currentConflict == null)
                {
                    return string.Empty;
                }
                if (_currentConflict.HasOurs)
                {
                    return string.Empty;
                }
                return _stringHelper.GetString("WorktreeStatusDeleted");
            }
        }
        public string TheirsFileStatusText
        {
            get
            {
                if (_currentConflict == null)
                {
                    return string.Empty;
                }
                if (_currentConflict.HasTheirs)
                {
                    return string.Empty;
                }
                return _stringHelper.GetString("WorktreeStatusDeleted");
            }
        }
        public string ResultFileStatusText
        {
            get
            {
                if (IsResultFileDeleted)
                {
                    return _stringHelper.GetString("WorktreeStatusDeleted");
                }
                return string.Empty;
            }
        }
        public bool IsResultFileDeleted
        {
            get { return _isResultFileDeleted; }
            private set
            {
                if (SetProperty(ref _isResultFileDeleted, value) == false)
                {
                    return;
                }
                SetStageResult(null);
                OnPropertyChanged(nameof(ResultFileStatusText));
                OnPropertyChanged(nameof(HasUnsavedConflictEdits));
            }
        }
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
        public bool IsRebaseConflict { get { return _isRebaseConflict; } }
        public string RebaseProgressLabel { get { return _stringHelper.GetString("RebaseProgressLabel"); } }
        public string CurrentChangeLabel
        {
            get
            {
                if (_isRebaseConflict)
                {
                    return _stringHelper.GetString("RebaseTargetChange");
                }
                return _stringHelper.GetString("CurrentChange");
            }
        }
        public string IncomingChangeLabel
        {
            get
            {
                if (_isRebaseConflict)
                {
                    return _stringHelper.GetString("RebaseReplayChange");
                }
                if (_isRevertConflict)
                {
                    return _stringHelper.GetString("RevertIncomingChange");
                }
                return _stringHelper.GetString("IncomingChange");
            }
        }
        public string UseCurrentChangeLabel
        {
            get
            {
                if (_isRebaseConflict)
                {
                    return _stringHelper.GetString("RebaseUseTarget");
                }
                return _stringHelper.GetString("UseCurrentChange");
            }
        }
        public string UseIncomingChangeLabel
        {
            get
            {
                if (_isRebaseConflict)
                {
                    return _stringHelper.GetString("RebaseUseReplay");
                }
                if (_isRevertConflict)
                {
                    return _stringHelper.GetString("RevertUseIncomingChange");
                }
                return _stringHelper.GetString("UseIncomingChange");
            }
        }
        public string UseBothLabel { get { return _stringHelper.GetString("UseBoth"); } }
        public string UseBothTooltip
        {
            get
            {
                if (_isRebaseConflict)
                {
                    return _stringHelper.GetString("RebaseBothOrderTip");
                }
                return null;
            }
        }
        public string RemoveBothLabel { get { return _stringHelper.GetString("RemoveBoth"); } }
        public string ConflictBatchCurrentFileText { get { return _stringHelper.Format("ConflictBatchCurrentFileLabel", RemainingHunkCount); } }
        public string ConflictBatchTooltip { get { return _stringHelper.GetString("ConflictBatchTooltip"); } }
        public string ConflictBatchReplaceEditsTitle { get { return _stringHelper.GetString("ConflictBatchReplaceEditsTitle"); } }
        public string ConflictBatchReplaceEditsMessage { get { return _stringHelper.GetString("ConflictBatchReplaceEditsMessage"); } }
        public string ConflictBatchApplyButtonText { get { return _stringHelper.GetString("ConflictBatchApplyButton"); } }
        public string ConflictSelectionSummaryText
        {
            get
            {
                if (_document == null)
                {
                    return string.Empty;
                }
                if (_document.Hunks.Count == 0)
                {
                    return string.Empty;
                }

                int selectedCount = _document.Hunks.Count - RemainingHunkCount;
                return _stringHelper.Format("ConflictSelectionSummary", selectedCount, _document.Hunks.Count);
            }
        }
        public string ConflictCountText { get { return _stringHelper.Format("ConflictFileCount", ConflictFiles.Count); } }
        public bool CanApplyRemaining
        {
            get
            {
                if (_document == null)
                {
                    return false;
                }
                if (IsBusy)
                {
                    return false;
                }
                if (ConflictFiles.Count == 0)
                {
                    return false;
                }
                return RemainingHunkCount > 0;
            }
        }

        private int RemainingHunkCount
        {
            get
            {
                if (_document == null)
                {
                    return 0;
                }

                int remainingCount = 0;
                foreach (ConflictHunk hunk in _document.Hunks)
                {
                    if (_choices.ContainsKey(hunk.Id))
                    {
                        continue;
                    }
                    remainingCount++;
                }
                return remainingCount;
            }
        }

        public string CurrentFilePath
        {
            get
            {
                if (_currentFilePathLocalization != null)
                {
                    return _currentFilePathLocalization.GetText(_stringHelper);
                }
                return _currentFilePath;
            }
            private set { _currentFilePathLocalization = null; SetProperty(ref _currentFilePath, value); }
        }

        public string OursSource
        {
            get
            {
                if (_oursSourceLocalization != null)
                {
                    return _oursSourceLocalization.GetText(_stringHelper);
                }
                return _oursSource;
            }
            private set { _oursSourceLocalization = null; SetProperty(ref _oursSource, value); }
        }

        public string TheirsSource
        {
            get
            {
                if (_theirsSourceLocalization != null)
                {
                    return _theirsSourceLocalization.GetText(_stringHelper);
                }
                return _theirsSource;
            }
            private set { _theirsSourceLocalization = null; SetProperty(ref _theirsSource, value); }
        }

        private LocalizedText _oursSourceLocalization = new("CurrentChange");
        private LocalizedText _theirsSourceLocalization = new("IncomingChange");

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
                    IsResultFileDeleted = false;
                    SetStageResult(null);
                    OnPropertyChanged(nameof(HasUnsavedConflictEdits));
                }
            }
        }

        public string StatusMessage
        {
            get
            {
                if (_statusMessageLocalization != null)
                {
                    return _statusMessageLocalization.GetText(_stringHelper);
                }
                return _statusMessage;
            }
            private set { _statusMessageLocalization = null; SetProperty(ref _statusMessage, value); }
        }

        public string CurrentChoiceText
        {
            get
            {
                if (_currentChoiceTextLocalization != null)
                {
                    return _currentChoiceTextLocalization.GetText(_stringHelper);
                }
                return _currentChoiceText;
            }
            private set { _currentChoiceTextLocalization = null; SetProperty(ref _currentChoiceText, value); }
        }

        public bool HasDocument
        {
            get { return _hasDocument; }
            private set
            {
                if (SetProperty(ref _hasDocument, value) == true)
                {
                    NotifyCommandStates();
                    NotifyBatchState();
                }
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            internal set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    NotifyCommandStates();
                    NotifyBatchState();
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

        public Func<ConflictFileItem, Task<bool>> ConfirmFileChangeAsync { get; set; }

        public ConflictFileItem SelectedFile
        {
            get { return _selectedFile; }
            set
            {
                if (ReferenceEquals(_selectedFile, value))
                {
                    return;
                }
                if (value == null)
                {
                    OnPropertyChanged(nameof(SelectedFile));
                    return;
                }
                _ = SelectFileAsync(value);
            }
        }

        private async Task SelectFileAsync(ConflictFileItem file)
        {
            if (IsBusy)
            {
                OnPropertyChanged(nameof(SelectedFile));
                return;
            }
            GitRepository repository = _repository;
            int request = ++_loadVersion;
            string draftText = ResultText;
            bool draftDeleted = IsResultFileDeleted;
            if (HasUnsavedConflictEdits)
            {
                if (ConfirmFileChangeAsync == null)
                {
                    OnPropertyChanged(nameof(SelectedFile));
                    return;
                }
                bool discard = await ConfirmFileChangeAsync(file);
                if (request != _loadVersion)
                {
                    return;
                }
                if (ReferenceEquals(_repository, repository) == false)
                {
                    return;
                }
                if (draftText != ResultText)
                {
                    OnPropertyChanged(nameof(SelectedFile));
                    return;
                }
                if (draftDeleted != IsResultFileDeleted)
                {
                    OnPropertyChanged(nameof(SelectedFile));
                    return;
                }
                if (discard == false)
                {
                    OnPropertyChanged(nameof(SelectedFile));
                    return;
                }
            }
            if (ConflictFiles.Contains(file) == false)
            {
                OnPropertyChanged(nameof(SelectedFile));
                return;
            }
            if (IsBusy)
            {
                OnPropertyChanged(nameof(SelectedFile));
                return;
            }
            ClearDocument();
            SetSelectedFile(file);
            await LoadConflictAsync(file);
        }

        public void SetCompletion(IConflictStageCompletion completion)
        {
            if (completion == null)
            {
                throw new ArgumentNullException(nameof(completion));
            }
            _stagePresenter.SetCompletion(completion);
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
            _stagePresenter.Invalidate();
            _loadVersion++;
            IsBusy = false;
            if (rootChanged == false)
            {
                return;
            }

            _isRevertInProgress = false;
            SetSelectedFile(null);
            ConflictFiles.Clear();
            OnPropertyChanged(nameof(ConflictCountText));
            ClearDocument();
            SetLocalizedStatusMessage(new LocalizedText("OpenRepositoryToFindConflicts"));
        }

        public void SetRevertState(GitRepository repository, GitRevertState state)
        {
            if (ReferenceEquals(_repository, repository) == false)
            {
                return;
            }
            if (state == null)
            {
                return;
            }
            if (string.Equals(state.RepositoryRoot, repository.RootPath, _pathComparison) == false)
            {
                return;
            }
            _isRevertInProgress = state.IsInProgress;
            if (HasDocument == false)
            {
                return;
            }
            if (HasUnsavedConflictEdits)
            {
                if (_isRevertInProgress == false)
                {
                    return;
                }
            }
            bool wasRevert = _isRevertConflict;
            _isRevertConflict = _isRevertInProgress;
            if (_isRevertConflict)
            {
                _theirsSourceLocalization = new LocalizedText("RevertIncomingIndexSource");
            }
            else if (wasRevert)
            {
                _theirsSourceLocalization = new LocalizedText("ConflictIncomingIndexStage3");
            }
            NotifyChangeLabels();
            RefreshLocalization();
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
            _stagePresenter.Invalidate();
            string previousPath = _currentConflict?.RelativePath ?? _selectedFile?.RelativePath ?? string.Empty;
            bool preserveDraft = HasUnsavedConflictEdits;
            SetSelectedFile(null);
            ConflictFiles.Clear();
            foreach (string path in paths)
            {
                ConflictFiles.Add(new ConflictFileItem(path, _stringHelper));
            }
            OnPropertyChanged(nameof(ConflictCountText));
            NotifyCommandStates();
            NotifyBatchState();

            if (ConflictFiles.Count == 0)
            {
                if (HasUnsavedConflictEdits == true)
                {
                    SetLocalizedStatusMessage(new LocalizedText("ConflictResolvedExternallyWithUnsavedEdits"));
                    return true;
                }
                ClearDocument();
                SetLocalizedStatusMessage(new LocalizedText("NoUnresolvedConflicts"));
                return true;
            }

            ConflictFileItem previousFile = ConflictFiles.FirstOrDefault(file => file.RelativePath == previousPath);
            if (preserveDraft)
            {
                SetSelectedFile(previousFile);
                if (previousFile == null)
                {
                    SetLocalizedStatusMessage(new LocalizedText("ConflictResolvedExternallyWithUnsavedEdits"));
                }
                return true;
            }
            ConflictFileItem nextFile = previousFile ?? ConflictFiles[0];
            SetSelectedFile(nextFile);
            await _loadPresenter.LoadCoreAsync(nextFile);
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

        private Task LoadConflictAsync(ConflictFileItem file)
        {
            return _loadPresenter.LoadAsync(file);
        }

        internal void ApplyConflictDocument(GitConflictFile conflict, ConflictDocument document, string initialResult,
            bool isRebaseConflict, string currentSourceLabel, string incomingSourceLabel, string incomingIndexSource)
        {
            _document = document;
            _currentConflict = conflict;
            OnPropertyChanged(nameof(OursFileStatusText));
            OnPropertyChanged(nameof(TheirsFileStatusText));
            _stagePresenter.Invalidate();
            _isRebaseConflict = isRebaseConflict;
            _isRevertConflict = _isRevertInProgress;
            if (isRebaseConflict)
            {
                _isRevertConflict = false;
            }
            NotifyChangeLabels();
            _choices.Clear();
            _currentHunkIndex = 0;
            CurrentFilePath = conflict.RelativePath;
            OursSource = conflict.OursSource;
            TheirsSource = conflict.TheirsSource;
            if (isRebaseConflict)
            {
                if (conflict.OursSource == currentSourceLabel)
                {
                    _oursSourceLocalization = new LocalizedText("RebaseTargetSource");
                }
            }
            if (conflict.TheirsSource == incomingSourceLabel)
            {
                string sourceKey = "IncomingChange";
                if (isRebaseConflict)
                {
                    sourceKey = "RebaseReplaySource";
                }
                _theirsSourceLocalization = new LocalizedText(sourceKey);
            }
            if (conflict.TheirsSource == incomingIndexSource)
            {
                _theirsSourceLocalization = new LocalizedText("ConflictIncomingIndexStage3");
            }
            if (_isRevertConflict)
            {
                _theirsSourceLocalization = new LocalizedText("RevertIncomingIndexSource");
            }
            OnPropertyChanged(nameof(OursSource));
            OnPropertyChanged(nameof(TheirsSource));
            BaseText = conflict.BaseText;
            _renderedResultText = initialResult;
            ResultText = initialResult;
            IsResultFileDeleted = document.IsWholeFileConflict && conflict.WorkingFileExists == false;
            _loadedResultText = ResultText;
            _loadedResultFileDeleted = IsResultFileDeleted;
            OnPropertyChanged(nameof(HasUnsavedConflictEdits));
            HasDocument = true;
            ShowCurrentHunk();
            SetLocalizedStatusMessage(new LocalizedText("ResolveConflictsPrompt", _document.Hunks.Count));
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
            _renderedResultText = _document.Render(_choices);
            ResultText = _renderedResultText;
            IsResultFileDeleted = IsDeletedChoice(choice);
            CurrentChoiceText = GetChoiceName(choice);
            int resolvedCount = _choices.Values.Count(selectedChoice => selectedChoice != ResolutionChoiceType.Unresolved);
            SetLocalizedStatusMessage(new LocalizedText("ConflictsSelectedNotice", resolvedCount, _document.Hunks.Count));
            NotifyBatchState();
        }

        private bool IsDeletedChoice(ResolutionChoiceType choice)
        {
            if (_document.IsWholeFileConflict == false)
            {
                return false;
            }
            switch (choice)
            {
                case ResolutionChoiceType.Ours:
                    return _currentConflict.HasOurs == false;
                case ResolutionChoiceType.Theirs:
                    return _currentConflict.HasTheirs == false;
                case ResolutionChoiceType.Both:
                    return _currentConflict.HasOurs == false && _currentConflict.HasTheirs == false;
                case ResolutionChoiceType.Remove:
                    return _currentConflict.HasOurs == false || _currentConflict.HasTheirs == false;
                default:
                    return false;
            }
        }

        public async Task ApplyRemainingAsync(ResolutionChoiceType choice, Func<Task<bool>> confirmReplaceEdits)
        {
            if (confirmReplaceEdits == null)
            {
                throw new ArgumentNullException(nameof(confirmReplaceEdits));
            }
            if (CanApplyRemaining == false)
            {
                return;
            }

            ConflictDocument document = _document;
            GitConflictFile conflict = _currentConflict;
            int loadVersion = _loadVersion;
            int requestVersion = _requestVersion;
            string resultBeforeConfirmation = ResultText;
            bool deletedBeforeConfirmation = IsResultFileDeleted;
            if (resultBeforeConfirmation != _renderedResultText)
            {
                bool replaceEdits = await confirmReplaceEdits();
                if (replaceEdits == false)
                {
                    return;
                }
                if (loadVersion != _loadVersion)
                {
                    return;
                }
                if (requestVersion != _requestVersion)
                {
                    return;
                }
                if (ReferenceEquals(_document, document) == false)
                {
                    return;
                }
                if (ReferenceEquals(_currentConflict, conflict) == false)
                {
                    return;
                }
                if (ResultText != resultBeforeConfirmation)
                {
                    return;
                }
                if (IsResultFileDeleted != deletedBeforeConfirmation)
                {
                    return;
                }
                if (CanApplyRemaining == false)
                {
                    return;
                }
            }

            int firstAppliedIndex = -1;
            for (int index = 0; index < document.Hunks.Count; index++)
            {
                ConflictHunk hunk = document.Hunks[index];
                if (_choices.ContainsKey(hunk.Id))
                {
                    continue;
                }
                _choices.Add(hunk.Id, choice);
                if (firstAppliedIndex < 0)
                {
                    firstAppliedIndex = index;
                }
            }

            if (firstAppliedIndex >= 0)
            {
                _currentHunkIndex = firstAppliedIndex;
            }
            _renderedResultText = document.Render(_choices);
            ResultText = _renderedResultText;
            if (firstAppliedIndex >= 0)
            {
                IsResultFileDeleted = IsDeletedChoice(choice);
            }
            ShowCurrentHunk();
            int resolvedCount = _choices.Values.Count(selectedChoice => selectedChoice != ResolutionChoiceType.Unresolved);
            SetLocalizedStatusMessage(new LocalizedText("ConflictsSelectedNotice", resolvedCount, document.Hunks.Count));
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
            SetLocalizedCurrentChoiceText(new LocalizedText("NoChoiceYet"));
            if (_choices.TryGetValue(hunk.Id, out ResolutionChoiceType choice) == true)
            {
                CurrentChoiceText = GetChoiceName(choice);
            }
            OnPropertyChanged(nameof(CurrentHunkText));
            NotifyCommandStates();
            NotifyBatchState();
        }

        public Task<ConflictStageResult> SaveAndStageAsync(string operationName)
        {
            return _stagePresenter.SaveAndStageAsync(operationName);
        }

        internal void SetStageResult(ConflictStageResult result)
        {
            if (SetProperty(ref _stageResult, result, nameof(StageResult)) == false)
            {
                return;
            }
            OnPropertyChanged(nameof(HasStageResult));
            OnPropertyChanged(nameof(HasGeneralStatus));
        }

        internal void MarkStageSaved(GitConflictFile conflict, string resultText, bool deleteFile)
        {
            if (ReferenceEquals(_currentConflict, conflict) == false)
            {
                return;
            }
            // The saved snapshot becomes the baseline; later edits stay in ResultText.
            _loadedResultText = resultText;
            _loadedResultFileDeleted = deleteFile;
            OnPropertyChanged(nameof(HasUnsavedConflictEdits));
        }

        internal void BeginStageRequest(bool ownsView)
        {
            _activeSaveCount++;
            OnPropertyChanged(nameof(IsSaving));
            if (ownsView)
            {
                _loadVersion++;
                IsBusy = true;
            }
        }

        internal void EndStageRequest(bool ownsView)
        {
            _activeSaveCount--;
            OnPropertyChanged(nameof(IsSaving));
            if (ownsView)
            {
                IsBusy = false;
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
            OnPropertyChanged(nameof(OursFileStatusText));
            OnPropertyChanged(nameof(TheirsFileStatusText));
            _stagePresenter.Invalidate();
            _isRebaseConflict = false;
            _isRevertConflict = false;
            NotifyChangeLabels();
            _choices.Clear();
            HasDocument = false;
            SetLocalizedCurrentFilePath(new LocalizedText("SelectConflictFile"));
            OursSource = _stringHelper.GetString("CurrentChange");
            TheirsSource = _stringHelper.GetString("IncomingChange");
            OursText = string.Empty;
            TheirsText = string.Empty;
            BaseText = string.Empty;
            ResultText = string.Empty;
            IsResultFileDeleted = false;
            _loadedResultText = string.Empty;
            _loadedResultFileDeleted = false;
            _renderedResultText = string.Empty;
            OnPropertyChanged(nameof(HasUnsavedConflictEdits));
            SetLocalizedCurrentChoiceText(new LocalizedText("NoChoiceYet"));
            OnPropertyChanged(nameof(CurrentHunkText));
            NotifyBatchState();
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

        public bool CanSaveAndStage
        {
            get
            {
                if (HasDocument == false)
                {
                    return false;
                }
                if (ConflictFiles.Count == 0)
                {
                    return false;
                }
                if (_currentConflict == null)
                {
                    return false;
                }
                return ConflictFiles.Any(file => file.RelativePath == _currentConflict.RelativePath);
            }
        }

        private void NotifyCommandStates()
        {
            PreviousHunkCommand.NotifyCanExecuteChanged();
            NextHunkCommand.NotifyCanExecuteChanged();
            ChooseOursCommand.NotifyCanExecuteChanged();
            ChooseTheirsCommand.NotifyCanExecuteChanged();
            ChooseBothCommand.NotifyCanExecuteChanged();
            RemoveBothCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanSaveAndStage));
        }

        private void NotifyBatchState()
        {
            OnPropertyChanged(nameof(ConflictBatchCurrentFileText));
            OnPropertyChanged(nameof(ConflictSelectionSummaryText));
            OnPropertyChanged(nameof(CanApplyRemaining));
        }

        private void NotifyChangeLabels()
        {
            OnPropertyChanged(nameof(IsRebaseConflict));
            OnPropertyChanged(nameof(CurrentChangeLabel));
            OnPropertyChanged(nameof(IncomingChangeLabel));
            OnPropertyChanged(nameof(UseCurrentChangeLabel));
            OnPropertyChanged(nameof(UseIncomingChangeLabel));
            OnPropertyChanged(nameof(UseBothTooltip));
        }

        public override void RefreshLocalization()
        {
            if (_document != null)
            {
                ConflictHunk hunk = _document.Hunks[_currentHunkIndex];
                if (_choices.TryGetValue(hunk.Id, out ResolutionChoiceType choice))
                {
                    CurrentChoiceText = GetChoiceName(choice);
                }
            }
            if (HasDocument == false)
            {
                _oursSourceLocalization = new LocalizedText("CurrentChange");
                _theirsSourceLocalization = new LocalizedText("IncomingChange");
            }
            base.RefreshLocalization();
        }

        private string GetChoiceName(ResolutionChoiceType choice)
        {
            switch (choice)
            {
                case ResolutionChoiceType.Ours:
                    return UseCurrentChangeLabel;
                case ResolutionChoiceType.Theirs:
                    return UseIncomingChangeLabel;
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
