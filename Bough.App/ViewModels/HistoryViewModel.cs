using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Controls;
using Bough.App.Commands;
using Bough.App.Localization;
using Bough.App.Services;
using Bough.App.Presenters;
using Bough.Core.Git;
using DataContainer.Generated;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.ViewModels
{
    public class HistoryViewModel : ViewModelBase
    {
        private LocalizedText _previewReasonLocalization;
        private LocalizedText _errorTextLocalization;
        private LocalizedText _pageErrorTextLocalization;
        private void SetLocalizedPreviewReason(LocalizedText text)
        {
            PreviewReason = text.GetText(_stringHelper);
            _previewReasonLocalization = text;
        }

        private void SetLocalizedErrorText(LocalizedText text)
        {
            ErrorText = text.GetText(_stringHelper);
            _errorTextLocalization = text;
        }

        private void SetLocalizedPageErrorText(LocalizedText text)
        {
            PageErrorText = text.GetText(_stringHelper);
            _pageErrorTextLocalization = text;
        }

        private readonly HistoryListPresenter _listPresenter;
        private readonly HistoryCommitPresenter _commitPresenter;
        private readonly HistoryFileTreePresenter _fileTreePresenter;
        private readonly HistoryActionPresenter _actionPresenter;
        private readonly GitCommitInspectionService _inspectionService;
        private readonly GitCommitFileActionService _fileActionService;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly HistoryGraphBuilder _graphBuilder;
        private HistoryGraphBuilder.Cursor _graphCursor;
        private double _graphWidth;
        private GitRepository _repository;
        private string _loadedBranchName = string.Empty;
        private GitHistoryScope _selectedScope = GitHistoryScope.All;
        private HistoryCommitItem _selectedCommit;
        private bool _hasMore;
        private bool _isLoading;
        private bool _isLoadingMore;
        private bool _isLoadingInspection;
        private bool _isLoadingChanges;
        private bool _hasLoadedChanges;
        private string _pageErrorText = string.Empty;
        private int _listVersion;
        private string _selectedMessage;
        private string _errorText;
        private GitCommitInspection _inspection;
        private string _selectedParent;
        private string _fileSearch;
        private string _treeSearch;
        private string _previewPath;
        private string _previewText;
        private string _previewReason;
        private int _selectedTab;
        private bool _isFileTreeView;
        private Task _inspectionLoadTask = Task.CompletedTask;
        private int _inspectionRequest;
        private int _expandRequest;
        private int _treeRequest;
        private int _treeFilterRequest;
        private int _previewRequest;
        private HistoryInspectionFileItem _selectedChangedFile;
        private HistoryTreeItem _selectedTreeFile;

        public HistoryViewModel(GitHistoryService historyService, GitCommitActionService actionService, GitCommitInspectionService inspectionService, GitCommitMessageService commitMessageService, GitCommitFileActionService fileActionService, GitOperationQueue operationQueue, StringHelper stringHelper)
        {
            _listPresenter = new HistoryListPresenter(historyService);
            _commitPresenter = new HistoryCommitPresenter(inspectionService, commitMessageService);
            _fileTreePresenter = new HistoryFileTreePresenter(inspectionService);
            _actionPresenter = new HistoryActionPresenter(actionService, operationQueue);
            _inspectionService = inspectionService;
            _fileActionService = fileActionService;
            _stringHelper = stringHelper;
            _errorLocalizer = new GitErrorLocalizer(stringHelper);
            Labels = new HistoryLabels(stringHelper);
            _graphBuilder = new HistoryGraphBuilder();
            _graphCursor = _graphBuilder.CreateCursor();
            Commits = [];
            ChangedFiles = [];
            VisibleChangedFiles = [];
            TreeRoots = [];
            VisibleTreeRoots = [];
            _selectedMessage = string.Empty;
            _errorText = string.Empty;
            _selectedParent = string.Empty;
            _fileSearch = string.Empty;
            _treeSearch = string.Empty;
            _previewPath = string.Empty;
            _previewText = string.Empty;
            _previewReason = string.Empty;
            LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, CanLoadMore);
            RetryLoadMoreCommand = new AsyncRelayCommand(RetryLoadMoreAsync, CanRetryLoadMore);
        }

        public ObservableCollection<HistoryCommitItem> Commits { get; }
        public override void RefreshLocalization()
        {
            Labels.RefreshLocalization();
            foreach (HistoryCommitItem commit in Commits)
            {
                commit.RefreshLocalization();
            }
            foreach (HistoryInspectionFileItem file in ChangedFiles)
            {
                file.RefreshLocalization();
            }
            foreach (HistoryTreeItem root in TreeRoots)
            {
                root.RefreshLocalization();
            }
            foreach (HistoryTreeItem root in VisibleTreeRoots)
            {
                root.RefreshLocalization();
            }
            base.RefreshLocalization();
        }
        public StringHelper Strings { get { return _stringHelper; } }
        public HistoryLabels Labels { get; }
        public event Action<GitRepository> RepositoryChanged;
        public event Action<string> ActionMessage;
        public GitRepository CurrentRepository { get { return _repository; } }
        public GitHistoryScope SelectedScope { get { return _selectedScope; } }
        public bool IsAllScope { get { return SelectedScope == GitHistoryScope.All; } }
        public bool IsCurrentBranchScope { get { return SelectedScope == GitHistoryScope.CurrentBranch; } }
        public ObservableCollection<HistoryInspectionFileItem> ChangedFiles { get; }
        public ObservableCollection<HistoryInspectionFileItem> VisibleChangedFiles { get; }
        public ObservableCollection<HistoryTreeItem> TreeRoots { get; }
        public ObservableCollection<HistoryTreeItem> VisibleTreeRoots { get; }
        public GitCommitInspection Inspection { get { return _inspection; } }
        public string AuthorDescription
        {
            get
            {
                string name = _inspection?.AuthorName ?? SelectedCommit?.AuthorDisplayName ?? string.Empty;
                string email = _inspection?.AuthorEmail ?? SelectedCommit?.AuthorEmail ?? string.Empty;
                if (name.Length == 0)
                {
                    return string.Empty;
                }
                if (string.IsNullOrWhiteSpace(email))
                {
                    return name;
                }
                return $"{name} <{email}>";
            }
        }
        public string AuthoredAtText { get { if (_inspection == null) return string.Empty; return _inspection.AuthoredAt.ToString("yyyy-MM-dd HH:mm:ss zzz"); } }
        public string ReferencesText
        {
            get
            {
                if (_inspection == null)
                {
                    return string.Empty;
                }
                List<string> references = _inspection.References.ToList();
                HistoryCommitItem item = Commits.FirstOrDefault(commit => commit.Hash == _inspection.Hash);
                if (item != null)
                {
                    foreach (HistoryReferenceItem reference in item.References)
                    {
                        if (reference.IsTag == false)
                        {
                            continue;
                        }
                        string fullName = $"refs/tags/{reference.Name}";
                        if (references.Contains(fullName, StringComparer.Ordinal) == false)
                        {
                            references.Add(fullName);
                        }
                    }
                }
                return string.Join("  ·  ", references);
            }
        }
        public bool HasParents { get { return _inspection != null && _inspection.Parents.Count > 0; } }
        public bool HasMultipleParents { get { return _inspection != null && _inspection.Parents.Count > 1; } }
        public string SelectedParent
        {
            get { return _selectedParent; }
            set
            {
                string parent = value;
                if (parent == null) parent = string.Empty;
                if (SetProperty(ref _selectedParent, parent) == false)
                {
                    return;
                }
                _previewRequest++;
                PreviewPath = string.Empty;
                PreviewText = string.Empty;
                PreviewReason = string.Empty;
                _ = LoadChangesAsync();
            }
        }
        public string ComparisonText { get { if (string.IsNullOrEmpty(SelectedParent) == true) return _stringHelper.GetString("HistoryComparedWithEmptyTree"); return _stringHelper.Format("HistoryComparedWithParent", SelectedParent.Substring(0, 8)); } }
        public string FileSearch { get { return _fileSearch; } set { if (SetProperty(ref _fileSearch, value) == true) FilterChangedFiles(); } }
        public string TreeSearch { get { return _treeSearch; } set { if (SetProperty(ref _treeSearch, value) == true) _ = FilterTreeAsync(); } }
        public string PreviewPath { get { return _previewPath; } private set { SetProperty(ref _previewPath, value); } }
        public string PreviewText { get { return _previewText; } private set { SetProperty(ref _previewText, value); } }
        public string PreviewReason
        {
            get
            {
                if (_previewReasonLocalization != null)
                {
                    return _previewReasonLocalization.GetText(_stringHelper);
                }
                return _previewReason;
            }
            private set
            {
                _previewReasonLocalization = null; SetProperty(ref _previewReason, value);
            }
        }
        public int SelectedTab
        {
            get { return _selectedTab; }
            set { SetProperty(ref _selectedTab, value); }
        }
        public bool IsFileTreeView
        {
            get { return _isFileTreeView; }
            private set
            {
                if (SetProperty(ref _isFileTreeView, value))
                {
                    OnPropertyChanged(nameof(IsCommitDetailView));
                }
            }
        }
        public bool IsCommitDetailView { get { return HasSelection && IsFileTreeView == false; } }
        public HistoryInspectionFileItem SelectedChangedFile
        {
            get { return _selectedChangedFile; }
            set
            {
                if (SetProperty(ref _selectedChangedFile, value) == false)
                {
                    return;
                }
                if (value == null)
                {
                    return;
                }
                _ = OpenFileAsync(value.Path, value.File.StatusCode == 'D');
            }
        }
        public HistoryTreeItem SelectedTreeFile
        {
            get { return _selectedTreeFile; }
            set
            {
                if (SetProperty(ref _selectedTreeFile, value) == false)
                {
                    return;
                }
                if (value == null)
                {
                    return;
                }
                _ = SelectTreeItemAsync(value);
            }
        }
        public AsyncRelayCommand LoadMoreCommand { get; }
        public AsyncRelayCommand RetryLoadMoreCommand { get; }
        public int ListVersion { get { return _listVersion; } }
        public string PageErrorText
        {
            get
            {
                if (_pageErrorTextLocalization != null)
                {
                    return _pageErrorTextLocalization.GetText(_stringHelper);
                }
                return _pageErrorText;
            }
            private set
            {
                _pageErrorTextLocalization = null;
                if (SetProperty(ref _pageErrorText, value) == false)
                {
                    return;
                }
                OnPropertyChanged(nameof(HasPageError));
                OnPropertyChanged(nameof(ShowPageStatus));
                RetryLoadMoreCommand.NotifyCanExecuteChanged();
                LoadMoreCommand.NotifyCanExecuteChanged();
            }
        }
        public bool HasPageError { get { return PageErrorText.Length > 0; } }
        public bool IsLoadingMore
        {
            get { return _isLoadingMore; }
            private set
            {
                if (SetProperty(ref _isLoadingMore, value))
                {
                    OnPropertyChanged(nameof(ShowPageStatus));
                }
            }
        }
        public bool ShowPageStatus { get { return IsLoadingMore || HasPageError; } }
        public bool HasHistory { get { return Commits.Count > 0; } }
        public bool HasSelection { get { return SelectedCommit != null; } }
        public bool HasNoSelection { get { return SelectedCommit == null; } }
        public bool IsEmpty { get { return Commits.Count == 0 && IsLoading == false && HasLoadError == false; } }
        public bool HasLoadError { get { return Commits.Count == 0 && ErrorText.Length > 0; } }
        public string CountText { get { return _stringHelper.Format("HistoryCommitCount", Commits.Count); } }
        public bool IsLoadingDetails { get { return _isLoadingInspection || _isLoadingChanges; } }
        public bool HasDetailError { get { return ErrorText.Length > 0; } }
        public bool HasNoChangedFiles
        {
            get
            {
                if (_hasLoadedChanges == false)
                {
                    return false;
                }
                if (IsLoadingDetails == true)
                {
                    return false;
                }
                if (HasDetailError == true)
                {
                    return false;
                }
                return ChangedFiles.Count == 0;
            }
        }
        public string ChangesSummary { get { if (_inspection == null) return string.Empty; return $"{_inspection.AuthorName}  ·  {_inspection.Hash.Substring(0, 8)}  ·  {_inspection.AuthoredAt:yyyy-MM-dd HH:mm zzz}"; } }
        public string MessageFirstLine { get { return GitCommitMessageService.GetFirstLine(SelectedMessage); } }

        public bool HasMore
        {
            get { return _hasMore; }
            private set
            {
                if (SetProperty(ref _hasMore, value) == true)
                {
                    LoadMoreCommand.NotifyCanExecuteChanged();
                    RetryLoadMoreCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set
            {
                if (SetProperty(ref _isLoading, value) == true)
                {
                    LoadMoreCommand.NotifyCanExecuteChanged();
                    RetryLoadMoreCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        public string ErrorText
        {
            get
            {
                if (_errorTextLocalization != null)
                {
                    return _errorTextLocalization.GetText(_stringHelper);
                }
                return _errorText;
            }
            private set
            {
                _errorTextLocalization = null;
                if (SetProperty(ref _errorText, value) == true)
                {
                    OnPropertyChanged(nameof(HasLoadError));
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(HasDetailError));
                    OnPropertyChanged(nameof(HasNoChangedFiles));
                }
            }
        }

        public string SelectedMessage
        {
            get { return _selectedMessage; }
            private set
            {
                if (SetProperty(ref _selectedMessage, value) == true)
                {
                    OnPropertyChanged(nameof(MessageFirstLine));
                }
            }
        }

        public HistoryCommitItem SelectedCommit
        {
            get { return _selectedCommit; }
            set
            {
                if (SetProperty(ref _selectedCommit, value) == false)
                {
                    return;
                }

                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
                OnPropertyChanged(nameof(IsCommitDetailView));
                SelectedMessage = string.Empty;
                ClearInspection();
                _commitPresenter.Invalidate();
                _inspectionLoadTask = Task.CompletedTask;
                if (value != null)
                {
                    _inspectionLoadTask = LoadInspectionAsync(value);
                }
            }
        }

        public void Clear()
        {
            _repository = null;
            _loadedBranchName = string.Empty;
            ResetHistoryList();
        }

        public async Task SetScopeAsync(GitHistoryScope scope)
        {
            if (_selectedScope == scope)
            {
                return;
            }
            _selectedScope = scope;
            OnPropertyChanged(nameof(SelectedScope));
            OnPropertyChanged(nameof(IsAllScope));
            OnPropertyChanged(nameof(IsCurrentBranchScope));
            ResetHistoryList();
            if (_repository == null)
            {
                return;
            }
            try
            {
                await LoadCoreAsync();
            }
            catch (Exception)
            {
                // LoadCoreAsync keeps the error visible in the history panel.
            }
        }

        private void ResetHistoryList()
        {
            _listPresenter.Invalidate();
            _graphCursor = _graphBuilder.CreateCursor();
            _graphWidth = 0;
            _listVersion++;
            OnPropertyChanged(nameof(ListVersion));
            SelectedCommit = null;
            Commits.Clear();
            ClearInspection();
            IsLoading = false;
            IsLoadingMore = false;
            HasMore = false;
            ErrorText = string.Empty;
            PageErrorText = string.Empty;
            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CountText));
        }

        public async Task<GitResetPreview> GetResetPreviewAsync(string repositoryRoot, string commitHash)
        {
            GitRepository repository = RequireRepository(repositoryRoot);
            return await _actionPresenter.GetResetPreviewAsync(repository, commitHash);
        }

        public async Task<bool> ResetAsync(string repositoryRoot, GitResetPreview preview, GitResetMode mode, bool hardConfirmed)
        {
            string successMessage = _stringHelper.Format("HistoryResetSucceeded", preview.BranchName, preview.ShortHash);
            return await RunActionAsync(repositoryRoot, successMessage,
                (repository, applyResult) => _actionPresenter.ResetAsync(repository, preview, mode, hardConfirmed, successMessage, applyResult));
        }

        public async Task<bool> SwitchDetachedAsync(string repositoryRoot, string commitHash)
        {
            string successMessage = _stringHelper.Format("HistoryDetachedCheckoutSucceeded", commitHash.Substring(0, 8));
            return await RunActionAsync(repositoryRoot, successMessage,
                (repository, applyResult) => _actionPresenter.SwitchDetachedAsync(repository, commitHash, successMessage, applyResult));
        }

        public async Task<bool> CreateBranchAsync(string repositoryRoot, string commitHash, string branchName, bool switchToBranch)
        {
            string successMessage = _stringHelper.Format("HistoryBranchCreated", branchName);
            return await RunActionAsync(repositoryRoot, successMessage,
                (repository, applyResult) => _actionPresenter.CreateBranchAsync(repository, commitHash, branchName, switchToBranch, successMessage, applyResult));
        }

        public async Task<bool> CreateTagAsync(string repositoryRoot, string commitHash, string tagName, string successMessage)
        {
            GitRepository repository = null;
            try
            {
                repository = RequireRepository(repositoryRoot);
                return await _actionPresenter.CreateTagAsync(repository, commitHash, tagName, successMessage,
                    () => ApplyTagResultAsync(repository, commitHash, tagName, successMessage));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (repository == null)
                {
                    ReportActionError(exception);
                    return false;
                }
                if (_repository?.RootPath == repository.RootPath)
                {
                    ReportActionError(exception);
                }
                return false;
            }
        }

        private Task<bool> ApplyTagResultAsync(GitRepository repository, string commitHash, string tagName, string successMessage)
        {
            GitRepository current = _repository;
            if (current == null)
            {
                return Task.FromResult(true);
            }
            if (current.RootPath != repository.RootPath)
            {
                return Task.FromResult(true);
            }

            HistoryCommitItem item = Commits.FirstOrDefault(commit => commit.Hash == commitHash);
            item?.AddTagReference(tagName.Trim());
            if (_inspection?.Hash == commitHash)
            {
                OnPropertyChanged(nameof(ReferencesText));
            }
            RepositoryChanged?.Invoke(current);
            ActionMessage?.Invoke(successMessage);
            return Task.FromResult(true);
        }

        public void ReportActionError(Exception exception)
        {
            string message = _errorLocalizer.GetDisplayMessage(exception);
            ErrorText = message;
            ActionMessage?.Invoke(message);
        }

        private GitRepository RequireRepository(string repositoryRoot)
        {
            if (_repository == null)
            {
                throw new GitException("HistoryRepositoryRequired", null, Array.Empty<object>());
            }
            if (_repository.RootPath != repositoryRoot)
            {
                throw new GitException("HistoryMenuRepositoryChanged", null, Array.Empty<object>());
            }
            return _repository;
        }

        private async Task<bool> RunActionAsync(string repositoryRoot, string successMessage,
            Func<GitRepository, Func<GitRepository, Task<bool>>, Task<bool>> execute)
        {
            GitRepository repository = null;
            try
            {
                repository = RequireRepository(repositoryRoot);
                return await execute(repository, updated => ApplyRepositoryActionAsync(repository, updated, successMessage));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (repository == null)
                {
                    ReportActionError(exception);
                    return false;
                }
                if (_repository?.RootPath == repository.RootPath)
                {
                    ReportActionError(exception);
                }
                return false;
            }
        }

        private async Task<bool> ApplyRepositoryActionAsync(GitRepository repository, GitRepository updated, string successMessage)
        {
            if (_repository?.RootPath != repository.RootPath)
            {
                return true;
            }
            _repository = updated;
            RepositoryChanged?.Invoke(updated);
            await LoadAsync(updated);
            ActionMessage?.Invoke(successMessage);
            return true;
        }

        public async Task LoadAsync(GitRepository repository)
        {
            _commitPresenter.Invalidate();
            bool repositoryChanged = _repository == null || _repository.RootPath != repository.RootPath;
            bool branchChanged = _loadedBranchName != repository.CurrentBranch;
            _repository = repository;
            _loadedBranchName = repository.CurrentBranch;
            if (repositoryChanged || (_selectedScope == GitHistoryScope.CurrentBranch && branchChanged))
            {
                ResetHistoryList();
            }
            await LoadCoreAsync();
        }

        public async Task SelectCommitAsync(string commitHash)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            if (Commits.Count == 0)
            {
                await LoadCoreAsync();
            }
            while (Commits.All(item => item.Hash != commitHash) && HasMore == true)
            {
                int previousCount = Commits.Count;
                await LoadMoreAsync();
                if (_repository != repository)
                {
                    return;
                }
                if (HasPageError)
                {
                    break;
                }
                if (Commits.Count == previousCount)
                {
                    break;
                }
            }
            HistoryCommitItem match = Commits.FirstOrDefault(item => item.Hash == commitHash);
            if (match != null)
            {
                SelectedCommit = match;
            }
        }

        private async Task RetryLoadMoreAsync()
        {
            if (CanRetryLoadMore() == false)
            {
                return;
            }
            PageErrorText = string.Empty;
            await LoadMoreAsync();
        }

        private async Task LoadMoreAsync()
        {
            if (CanLoadMore() == false)
            {
                return;
            }
            GitRepository repository = _repository;
            GitHistoryScope scope = _selectedScope;
            int existingCount = Commits.Count;
            string anchorHash = Commits[existingCount - 1].Hash;
            IsLoadingMore = true;
            IsLoading = true;
            HistoryListResult result = null;
            try
            {
                result = await _listPresenter.LoadNextAsync(repository, scope, existingCount, anchorHash);
                if (result.IsCurrent == false)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (_selectedScope != scope)
                {
                    return;
                }
                if (result.Error != null)
                {
                    SetLocalizedPageErrorText(new LocalizedText(result.Error));
                    return;
                }
                GitHistoryPage page = result.Page;
                GitHistoryCommit[] newCommits = page.Commits.Skip(1).ToArray();
                IReadOnlyList<HistoryGraphRow> rows = _graphCursor.Append(newCommits);
                double graphWidth = Math.Max(_graphWidth, GetGraphWidth(rows));
                if (graphWidth > _graphWidth)
                {
                    _graphWidth = graphWidth;
                    foreach (HistoryCommitItem item in Commits)
                    {
                        item.GraphWidth = graphWidth;
                    }
                }
                for (int index = 0; index < newCommits.Length; index++)
                {
                    Commits.Add(CreateCommitItem(newCommits[index], rows[index], _graphWidth));
                }
                HasMore = page.HasMore;
                OnPropertyChanged(nameof(CountText));
            }
            catch (Exception exception)
            {
                if (result == null)
                {
                    return;
                }
                if (_listPresenter.IsCurrent(result.RequestVersion) == false)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                SetLocalizedPageErrorText(new LocalizedText(exception));
            }
            finally
            {
                if (result != null && _listPresenter.IsCurrent(result.RequestVersion))
                {
                    IsLoading = false;
                    IsLoadingMore = false;
                }
            }
        }

        private async Task LoadCoreAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            GitHistoryScope scope = _selectedScope;
            IsLoadingMore = false;
            IsLoading = true;
            ErrorText = string.Empty;
            PageErrorText = string.Empty;
            HistoryListResult result = null;
            try
            {
                result = await _listPresenter.LoadFirstAsync(repository, scope);
                if (result.IsCurrent == false)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (_selectedScope != scope)
                {
                    return;
                }
                if (result.Error != null)
                {
                    SetLocalizedErrorText(new LocalizedText(result.Error));
                    throw result.Error;
                }
                GitHistoryPage page = result.Page;
                if (MatchesLoadedPrefix(page))
                {
                    HasMore = Commits.Count > page.Commits.Count || page.HasMore;
                    return;
                }

                string selectedHash = SelectedCommit?.Hash;
                SelectedCommit = null;
                Commits.Clear();
                _graphCursor = _graphBuilder.CreateCursor();
                IReadOnlyList<HistoryGraphRow> rows = _graphCursor.Append(page.Commits);
                _graphWidth = GetGraphWidth(rows);
                for (int index = 0; index < page.Commits.Count; index++)
                {
                    Commits.Add(CreateCommitItem(page.Commits[index], rows[index], _graphWidth));
                }

                HasMore = page.HasMore;
                _listVersion++;
                OnPropertyChanged(nameof(ListVersion));
                OnPropertyChanged(nameof(HasHistory));
                OnPropertyChanged(nameof(HasLoadError));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(CountText));
                SelectedCommit = Commits.FirstOrDefault(item => item.Hash == selectedHash) ?? Commits.FirstOrDefault();
            }
            catch (Exception exception)
            {
                if (result == null)
                {
                    return;
                }
                if (_listPresenter.IsCurrent(result.RequestVersion) == false)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                SetLocalizedErrorText(new LocalizedText(exception));
                throw;
            }
            finally
            {
                if (result != null && _listPresenter.IsCurrent(result.RequestVersion))
                {
                    IsLoading = false;
                }
            }
        }

        private bool MatchesLoadedPrefix(GitHistoryPage page)
        {
            if (Commits.Count == 0)
            {
                return false;
            }
            if (page.Commits.Count == 0)
            {
                return false;
            }
            if (page.Commits.Count > Commits.Count)
            {
                return false;
            }
            if (Commits.Count > page.Commits.Count && page.HasMore == false)
            {
                return false;
            }
            for (int index = 0; index < page.Commits.Count; index++)
            {
                GitHistoryCommit loaded = Commits[index].Commit;
                GitHistoryCommit current = page.Commits[index];
                if (loaded.Hash != current.Hash)
                {
                    return false;
                }
                if (loaded.References.SequenceEqual(current.References) == false)
                {
                    return false;
                }
            }
            return true;
        }

        private HistoryCommitItem CreateCommitItem(GitHistoryCommit commit, HistoryGraphRow graph, double graphWidth)
        {
            return new HistoryCommitItem(commit, graph, graphWidth, _stringHelper);
        }

        private static double GetGraphWidth(IReadOnlyList<HistoryGraphRow> rows)
        {
            int maximumLane = 0;
            foreach (HistoryGraphRow row in rows)
            {
                maximumLane = Math.Max(maximumLane, row.NodeLane);
                foreach (HistoryGraphSegment segment in row.Segments)
                {
                    maximumLane = Math.Max(maximumLane, Math.Max(segment.FromLane, segment.ToLane));
                }
            }
            return Math.Min(220, 34 + maximumLane * 18);
        }

        private void ClearInspection()
        {
            IsFileTreeView = false;
            _inspectionRequest++;
            _expandRequest++;
            _treeRequest++;
            _treeFilterRequest++;
            _previewRequest++;
            _inspection = null;
            OnPropertyChanged(nameof(Inspection));
            OnPropertyChanged(nameof(AuthorDescription));
            OnPropertyChanged(nameof(AuthoredAtText));
            OnPropertyChanged(nameof(ReferencesText));
            _selectedChangedFile = null;
            OnPropertyChanged(nameof(SelectedChangedFile));
            _selectedTreeFile = null;
            OnPropertyChanged(nameof(SelectedTreeFile));
            OnPropertyChanged(nameof(HasNoChangedFiles));
            OnPropertyChanged(nameof(HasParents));
            OnPropertyChanged(nameof(HasMultipleParents));
            OnPropertyChanged(nameof(ChangesSummary));
            _selectedParent = string.Empty;
            OnPropertyChanged(nameof(SelectedParent));
            ChangedFiles.Clear();
            VisibleChangedFiles.Clear();
            TreeRoots.Clear();
            VisibleTreeRoots.Clear();
            PreviewPath = string.Empty;
            PreviewText = string.Empty;
            PreviewReason = string.Empty;
            _isLoadingInspection = false;
            _isLoadingChanges = false;
            _hasLoadedChanges = false;
            NotifyDetailState();
        }

        private void NotifyDetailState()
        {
            OnPropertyChanged(nameof(IsLoadingDetails));
            OnPropertyChanged(nameof(HasNoChangedFiles));
        }

        private async Task LoadInspectionAsync(HistoryCommitItem selected)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            _isLoadingInspection = true;
            ErrorText = string.Empty;
            NotifyDetailState();
            HistoryCommitResult result = await _commitPresenter.LoadAsync(repository, selected.Hash);
            if (result.IsCurrent == false)
            {
                return;
            }
            if (SelectedCommit != selected)
            {
                return;
            }
            if (_repository != repository)
            {
                return;
            }
            if (result.Error != null)
            {
                _isLoadingInspection = false;
                SetLocalizedErrorText(new LocalizedText(result.Error));
                NotifyDetailState();
                return;
            }
            try
            {
                GitCommitInspection details = result.Inspection;
                _inspection = details;
                OnPropertyChanged(nameof(Inspection));
                OnPropertyChanged(nameof(AuthorDescription));
                OnPropertyChanged(nameof(AuthoredAtText));
                OnPropertyChanged(nameof(ReferencesText));
                OnPropertyChanged(nameof(HasParents));
                OnPropertyChanged(nameof(HasMultipleParents));
                OnPropertyChanged(nameof(ChangesSummary));
                SelectedMessage = result.Message;
                if (details.Parents.Count > 0)
                {
                    SelectedParent = details.Parents[0];
                }
                else
                {
                    await LoadChangesAsync();
                }
            }
            catch (Exception exception)
            {
                if (_commitPresenter.IsCurrent(result.RequestVersion) == false)
                {
                    return;
                }
                if (SelectedCommit != selected)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                SetLocalizedErrorText(new LocalizedText(exception));
            }
            finally
            {
                FinishInspectionLoading(result, selected, repository);
            }
        }

        private void FinishInspectionLoading(HistoryCommitResult result, HistoryCommitItem selected, GitRepository repository)
        {
            if (_commitPresenter.IsCurrent(result.RequestVersion) == false)
            {
                return;
            }
            if (SelectedCommit != selected)
            {
                return;
            }
            if (_repository != repository)
            {
                return;
            }
            _isLoadingInspection = false;
            NotifyDetailState();
        }

        private async Task LoadChangesAsync()
        {
            _expandRequest++;
            GitRepository repository = _repository;
            GitCommitInspection inspection = _inspection;
            string parent = SelectedParent;
            int request = ++_inspectionRequest;
            _hasLoadedChanges = false;
            ChangedFiles.Clear();
            VisibleChangedFiles.Clear();
            OnPropertyChanged(nameof(ComparisonText));
            if (repository == null)
            {
                _isLoadingChanges = false;
                NotifyDetailState();
                return;
            }
            if (inspection == null)
            {
                _isLoadingChanges = false;
                NotifyDetailState();
                return;
            }
            _isLoadingChanges = true;
            ErrorText = string.Empty;
            NotifyDetailState();
            try
            {
                GitCommitFileChanges changes = await _inspectionService.GetChangedFilesAsync(repository, inspection.Hash, parent);
                if (request != _inspectionRequest)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (_inspection != inspection)
                {
                    return;
                }
                if (SelectedParent != parent)
                {
                    return;
                }
                foreach (GitCommitChangedFile file in changes.Files)
                {
                    ChangedFiles.Add(new HistoryInspectionFileItem(file, _stringHelper));
                }
                FilterChangedFiles();
                _hasLoadedChanges = true;
            }
            catch (Exception exception)
            {
                if (request != _inspectionRequest)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (_inspection != inspection)
                {
                    return;
                }
                if (SelectedParent != parent)
                {
                    return;
                }
                SetLocalizedErrorText(new LocalizedText(exception));
            }
            finally
            {
                FinishChangesLoading(request, repository, inspection, parent);
            }
        }

        private void FinishChangesLoading(int request, GitRepository repository, GitCommitInspection inspection, string parent)
        {
            if (request != _inspectionRequest)
            {
                return;
            }
            if (_repository != repository)
            {
                return;
            }
            if (_inspection != inspection)
            {
                return;
            }
            if (SelectedParent != parent)
            {
                return;
            }
            _isLoadingChanges = false;
            NotifyDetailState();
        }

        private void FilterChangedFiles()
        {
            VisibleChangedFiles.Clear();
            foreach (HistoryInspectionFileItem file in ChangedFiles)
            {
                if (file.Path.Contains(FileSearch, StringComparison.OrdinalIgnoreCase) == true || file.PreviousPath.Contains(FileSearch, StringComparison.OrdinalIgnoreCase) == true)
                {
                    VisibleChangedFiles.Add(file);
                }
            }
        }

        public async Task ExpandFileAsync(HistoryInspectionFileItem file)
        {
            if (ChangedFiles.Contains(file) == false)
            {
                return;
            }
            if (file.IsLoaded == true)
            {
                file.IsExpanded = true;
                return;
            }
            if (file.IsLoading == true)
            {
                file.IsExpanded = true;
                return;
            }
            if (_repository == null)
            {
                return;
            }
            if (_inspection == null)
            {
                return;
            }
            GitRepository repository = _repository;
            GitCommitInspection inspection = _inspection;
            string parent = SelectedParent;
            file.IsLoading = true;
            file.IsExpanded = true;
            try
            {
                GitCommitFileDiff diff = await _inspectionService.GetFileDiffAsync(repository, inspection.Hash, parent, file.File);
                if (_repository != repository)
                {
                    return;
                }
                if (_inspection != inspection)
                {
                    return;
                }
                if (SelectedParent != parent)
                {
                    return;
                }
                if (ChangedFiles.Contains(file) == false)
                {
                    return;
                }

                file.DiffText = diff.Text;
                file.SetLocalizedDiffReason(new LocalizedText(diff.ReasonCode, diff.ReasonArguments.ToArray()));
                file.DiffLines.Clear();
                foreach (GitUnifiedDiffHunk hunk in diff.Hunks)
                {
                    file.DiffLines.Add(new HistoryDiffLineItem(string.Empty, string.Empty, hunk.Header, 'H'));
                    foreach (GitUnifiedDiffLine line in hunk.Lines)
                    {
                        string oldNumber = string.Empty;
                        string newNumber = string.Empty;
                        if (line.OldLineNumber > 0) oldNumber = line.OldLineNumber.ToString();
                        if (line.NewLineNumber > 0) newNumber = line.NewLineNumber.ToString();
                        file.DiffLines.Add(new HistoryDiffLineItem(oldNumber, newNumber, $"{line.Kind}{line.Text}", line.Kind));
                    }
                }
                file.OnDiffLinesChanged();
                file.IsLoaded = true;
            }
            catch (Exception exception)
            {
                if (ChangedFiles.Contains(file) == true)
                {
                    file.SetLocalizedDiffReason(new LocalizedText(exception));
                    file.IsLoaded = true;
                }
            }
            finally
            {
                file.IsLoading = false;
            }
        }

        public async Task ExpandAllAsync()
        {
            int request = ++_expandRequest;
            HistoryInspectionFileItem[] files = ChangedFiles.ToArray();
            foreach (HistoryInspectionFileItem file in files)
            {
                if (request != _expandRequest)
                {
                    return;
                }
                await ExpandFileAsync(file);
            }
        }

        public void CollapseAll()
        {
            _expandRequest++;
            foreach (HistoryInspectionFileItem file in ChangedFiles) file.IsExpanded = false;
        }

        public async Task BrowseCommitFilesAsync(string repositoryRoot, string commitHash)
        {
            GitRepository repository = RequireRepository(repositoryRoot);
            await SelectCommitAsync(commitHash);
            if (_repository != repository)
            {
                return;
            }
            HistoryCommitItem selected = SelectedCommit;
            if (selected == null)
            {
                return;
            }
            if (selected.Hash != commitHash)
            {
                return;
            }

            await _inspectionLoadTask;
            if (_repository != repository)
            {
                return;
            }
            if (SelectedCommit != selected)
            {
                return;
            }
            if (_inspection == null)
            {
                return;
            }
            if (_inspection.Hash != commitHash)
            {
                return;
            }
            await OpenFileTreeAsync();
        }

        private async Task OpenFileTreeAsync()
        {
            IsFileTreeView = true;
            TreeSearch = string.Empty;
            _previewRequest++;
            PreviewPath = string.Empty;
            PreviewText = string.Empty;
            PreviewReason = string.Empty;
            _selectedTreeFile = null;
            OnPropertyChanged(nameof(SelectedTreeFile));
            await LoadTreeAsync();
        }

        public async Task CloseFileTreeAsync()
        {
            IsFileTreeView = false;
            _previewRequest++;
            PreviewPath = string.Empty;
            PreviewText = string.Empty;
            PreviewReason = string.Empty;
            if (SelectedTab != 1)
            {
                return;
            }
            HistoryInspectionFileItem selected = SelectedChangedFile;
            if (selected == null)
            {
                return;
            }
            await OpenFileAsync(selected.Path, selected.File.StatusCode == 'D');
        }

        public async Task LoadTreeAsync()
        {
            GitRepository repository = _repository;
            GitCommitInspection inspection = _inspection;
            if (repository == null)
            {
                return;
            }
            if (inspection == null)
            {
                return;
            }
            if (TreeRoots.Count > 0)
            {
                return;
            }
            int request = ++_treeRequest;
            try
            {
                GitCommitTreeListing listing = await _fileTreePresenter.LoadAsync(repository, inspection.Hash);
                if (request != _treeRequest)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (_inspection != inspection)
                {
                    return;
                }
                foreach (GitCommitTreeEntry entry in listing.Entries) TreeRoots.Add(new HistoryTreeItem(entry, _stringHelper));
                await FilterTreeAsync();
            }
            catch (Exception exception)
            {
                if (request == _treeRequest) SetLocalizedErrorText(new LocalizedText(exception));
            }
        }

        public async Task ExpandTreeAsync(HistoryTreeItem item)
        {
            item.IsExpanded = true;
            if (item.IsDirectory == false)
            {
                return;
            }
            if (item.IsLoaded == true)
            {
                return;
            }
            if (_repository == null)
            {
                return;
            }
            if (_inspection == null)
            {
                return;
            }
            GitRepository repository = _repository;
            GitCommitInspection inspection = _inspection;
            item.IsLoading = true;
            try
            {
                GitCommitTreeListing listing = await _fileTreePresenter.LoadAsync(repository, inspection.Hash, item.Path);
                if (_repository != repository)
                {
                    return;
                }
                if (_inspection != inspection)
                {
                    return;
                }
                if (item.IsLoaded == true)
                {
                    return;
                }
                item.Children.Clear();
                foreach (GitCommitTreeEntry entry in listing.Entries) item.Children.Add(new HistoryTreeItem(entry, _stringHelper));
                item.IsLoaded = true;
            }
            finally
            {
                item.IsLoading = false;
            }
        }

        private async Task FilterTreeAsync()
        {
            int request = ++_treeFilterRequest;
            VisibleTreeRoots.Clear();
            string search = TreeSearch;
            if (search.Length == 0)
            {
                foreach (HistoryTreeItem item in TreeRoots) VisibleTreeRoots.Add(item);
                return;
            }
            try
            {
                foreach (HistoryTreeItem item in TreeRoots)
                {
                    if (item.Path.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        VisibleTreeRoots.Add(item);
                        continue;
                    }
                    if (item.IsDirectory == true)
                    {
                        await SearchTreeAsync(item, search, request);
                        if (request != _treeFilterRequest)
                        {
                            return;
                        }
                        if (item.Children.Count > 0 && HasTreeMatch(item, search) == true) VisibleTreeRoots.Add(item);
                    }
                }
            }
            catch (Exception exception)
            {
                if (request == _treeFilterRequest) SetLocalizedErrorText(new LocalizedText(exception));
            }
        }

        private async Task SearchTreeAsync(HistoryTreeItem item, string search, int request)
        {
            await ExpandTreeAsync(item);
            if (request != _treeFilterRequest)
            {
                return;
            }
            foreach (HistoryTreeItem child in item.Children)
            {
                if (child.IsDirectory == true) await SearchTreeAsync(child, search, request);
                if (request != _treeFilterRequest)
                {
                    return;
                }
            }
        }

        private bool HasTreeMatch(HistoryTreeItem item, string search)
        {
            if (item.Path.Contains(search, StringComparison.OrdinalIgnoreCase) == true) return true;
            foreach (HistoryTreeItem child in item.Children)
            {
                if (HasTreeMatch(child, search) == true) return true;
            }
            return false;
        }

        private async Task SelectTreeItemAsync(HistoryTreeItem item)
        {
            if (item.IsPlaceholder == true)
            {
                return;
            }
            if (item.IsDirectory == true)
            {
                try { await ExpandTreeAsync(item); }
                catch (Exception exception) { SetLocalizedErrorText(new LocalizedText(exception)); }
                return;
            }
            await OpenFileAsync(item.Path, false);
        }

        public async Task OpenFileAsync(string path, bool deleted)
        {
            GitRepository repository = _repository;
            GitCommitInspection inspection = _inspection;
            if (repository == null)
            {
                return;
            }
            if (inspection == null)
            {
                return;
            }
            string revision = inspection.Hash;
            string parent = SelectedParent;
            if (deleted == true) revision = SelectedParent;
            if (revision.Length == 0)
            {
                SetLocalizedPreviewReason(new LocalizedText("HistoryPreviewFileAbsent"));
                return;
            }
            int request = ++_previewRequest;
            PreviewPath = $"{path} @ {revision.Substring(0, 8)}";
            PreviewText = string.Empty;
            SetLocalizedPreviewReason(new LocalizedText("HistoryPreviewLoading"));
            try
            {
                GitCommitFileContent content = await _inspectionService.GetFileContentAsync(repository, revision, path);
                if (request != _previewRequest)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                if (_inspection != inspection)
                {
                    return;
                }
                if (SelectedParent != parent)
                {
                    return;
                }
                PreviewText = content.Text;
                SetLocalizedPreviewReason(new LocalizedText(content.ReasonCode, content.ReasonArguments.ToArray()));
                if (content.ReasonCode == "HistorySubmoduleGitlink")
                {
                    SetLocalizedPreviewReason(new LocalizedText("HistoryPreviewTarget", new LocalizedText(content.ReasonCode, content.ReasonArguments.ToArray()), content.ObjectHash));
                }
            }
            catch (Exception exception)
            {
                if (request == _previewRequest) SetLocalizedPreviewReason(new LocalizedText(exception));
            }
        }

        public FileHistoryPresenter CreateFileHistoryPresenter(string repositoryRoot, string commitHash, string path)
        {
            GitRepository repository = RequireSelectedRepository(repositoryRoot, commitHash);
            FileHistoryViewModel model = new(repository, commitHash, path);
            return new FileHistoryPresenter(_inspectionService, model);
        }

        public GitRepository RequireSelectedRepository(string root, string hash)
        {
            GitRepository repository = RequireRepository(root);
            if (_inspection == null)
            {
                throw new GitException("HistoryCommitRequired", null, Array.Empty<object>());
            }
            if (_inspection.Hash != hash)
            {
                throw new GitException("HistorySelectedCommitChanged", null, Array.Empty<object>());
            }
            return repository;
        }

        public GitCommitFileActionService FileActions { get { return _fileActionService; } }

        public void NotifyFileRestored(string repositoryRoot, string path)
        {
            if (_repository != null && _repository.RootPath == repositoryRoot)
            {
                ActionMessage?.Invoke(_stringHelper.Format("HistoryWorkingFileRestored", path));
            }
            FileRestored?.Invoke(repositoryRoot, path);
        }

        public event Action<string, string> FileRestored;

        private string LocalizeFileReason(string reasonCode, IReadOnlyList<object> arguments)
        {
            if (string.IsNullOrEmpty(reasonCode))
            {
                return string.Empty;
            }

            StringTemplate template = TemplateContainer<StringTemplate>.Find(reasonCode);
            if (template.Invalid())
            {
                return reasonCode;
            }

            string localized = _stringHelper.GetString(template);
            if (arguments.Count == 0)
            {
                return localized;
            }

            return string.Format(CultureInfo.CurrentCulture, localized, arguments.ToArray());
        }

        private bool CanLoadMore()
        {
            if (_repository == null)
            {
                return false;
            }
            if (Commits.Count == 0)
            {
                return false;
            }
            if (HasMore == false)
            {
                return false;
            }
            if (IsLoading)
            {
                return false;
            }
            if (HasPageError)
            {
                return false;
            }
            return true;
        }

        private bool CanRetryLoadMore()
        {
            if (_repository == null)
            {
                return false;
            }
            if (Commits.Count == 0)
            {
                return false;
            }
            if (HasMore == false)
            {
                return false;
            }
            if (IsLoading)
            {
                return false;
            }
            if (HasPageError == false)
            {
                return false;
            }
            return true;
        }
    }
}
