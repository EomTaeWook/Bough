using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Controls;
using Bough.App.Localization;
using Bough.Core.Git;

namespace Bough.App.ViewModels
{
    public class HistoryViewModel : ViewModelBase
    {
        private readonly GitHistoryService _historyService;
        private readonly GitCommitActionService _actionService;
        private readonly GitCommitInspectionService _inspectionService;
        private readonly GitCommitMessageService _commitMessageService;
        private readonly GitCommitFileActionService _fileActionService;
        private readonly GitOperationQueue _operationQueue;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly HistoryGraphBuilder _graphBuilder;
        private HistoryGraphBuilder.Cursor _graphCursor;
        private double _graphWidth;
        private readonly AuthorPhotoSettings _authorPhotoSettings;
        private GitRepository _repository;
        private string _loadedBranchName = string.Empty;
        private GitHistoryScope _selectedScope = GitHistoryScope.All;
        private HistoryCommitItem _selectedCommit;
        private bool _hasMore;
        private bool _isLoading;
        private bool _isLoadingMore;
        private string _pageErrorText = string.Empty;
        private int _listVersion;
        private string _selectedMessage;
        private string _errorText;
        private int _detailsRequest;
        private int _loadRequest;
        private GitCommitInspection _inspection;
        private string _selectedParent;
        private string _fileSearch;
        private string _treeSearch;
        private string _previewPath;
        private string _previewText;
        private string _previewReason;
        private string _auxiliaryText;
        private string _auxiliaryTitle;
        private int _selectedTab;
        private int _inspectionRequest;
        private int _expandRequest;
        private int _treeRequest;
        private int _treeFilterRequest;
        private int _previewRequest;
        private HistoryInspectionFileItem _selectedChangedFile;
        private HistoryTreeItem _selectedTreeFile;
        private bool _externalAuthorPhotosEnabled;
        private string _gitHubRemoteUrl = string.Empty;
        private string _remoteUrlLoadedRoot = string.Empty;
        private int _remoteUrlRequest;

        public HistoryViewModel(GitHistoryService historyService, GitCommitActionService actionService, GitCommitInspectionService inspectionService, GitCommitMessageService commitMessageService, GitCommitFileActionService fileActionService, GitOperationQueue operationQueue, StringHelper stringHelper)
        {
            _historyService = historyService;
            _actionService = actionService;
            _inspectionService = inspectionService;
            _commitMessageService = commitMessageService;
            _fileActionService = fileActionService;
            _operationQueue = operationQueue ?? throw new ArgumentNullException(nameof(operationQueue));
            _stringHelper = stringHelper;
            _errorLocalizer = new GitErrorLocalizer(stringHelper);
            Labels = new HistoryLabels(stringHelper);
            _graphBuilder = new HistoryGraphBuilder();
            _graphCursor = _graphBuilder.CreateCursor();
            _authorPhotoSettings = new AuthorPhotoSettings();
            _externalAuthorPhotosEnabled = _authorPhotoSettings.LoadEnabled();
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
            _auxiliaryText = string.Empty;
            _auxiliaryTitle = string.Empty;
            LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, CanLoadMore);
            RetryLoadMoreCommand = new AsyncRelayCommand(RetryLoadMoreAsync, CanRetryLoadMore);
        }

        public ObservableCollection<HistoryCommitItem> Commits { get; }
        public StringHelper Strings { get { return _stringHelper; } }
        public HistoryLabels Labels { get; }
        public bool ExternalAuthorPhotosEnabled
        {
            get { return _externalAuthorPhotosEnabled; }
            set
            {
                if (SetProperty(ref _externalAuthorPhotosEnabled, value) == false)
                {
                    return;
                }
                foreach (HistoryCommitItem commit in Commits)
                {
                    commit.ExternalPhotosEnabled = value;
                }
                _authorPhotoSettings.SaveEnabled(value);
                if (value == false)
                {
                    _remoteUrlRequest++;
                    _remoteUrlLoadedRoot = string.Empty;
                    return;
                }
                EnsureGitHubRemoteUrl();
            }
        }
        public event Action<GitRepository> RepositoryChanged;
        public event Action<string> ActionMessage;
        public GitRepository CurrentRepository { get { return _repository; } }
        public GitHistoryScope SelectedScope { get { return _selectedScope; } }
        public bool IsAllScope { get { return SelectedScope == GitHistoryScope.All; } }
        public bool IsCurrentBranchScope { get { return SelectedScope == GitHistoryScope.CurrentBranch; } }
        public string GitHubRemoteUrl
        {
            get { return _gitHubRemoteUrl; }
            private set
            {
                if (SetProperty(ref _gitHubRemoteUrl, value) == false)
                {
                    return;
                }
                foreach (HistoryCommitItem commit in Commits)
                {
                    commit.GitHubRemoteUrl = value;
                }
            }
        }
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
        public string PreviewReason { get { return _previewReason; } private set { SetProperty(ref _previewReason, value); } }
        public string AuxiliaryTitle { get { return _auxiliaryTitle; } private set { SetProperty(ref _auxiliaryTitle, value); } }
        public string AuxiliaryText { get { return _auxiliaryText; } private set { SetProperty(ref _auxiliaryText, value); } }
        public bool HasAuxiliary { get { return AuxiliaryTitle.Length > 0; } }
        public int SelectedTab
        {
            get { return _selectedTab; }
            set
            {
                if (SetProperty(ref _selectedTab, value) == false)
                {
                    return;
                }
                if (value != 2)
                {
                    return;
                }
                _ = LoadTreeAsync();
            }
        }
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
            get { return _pageErrorText; }
            private set
            {
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
        public bool HasNoChangedFiles { get { return ChangedFiles.Count == 0; } }
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
            get { return _errorText; }
            private set
            {
                if (SetProperty(ref _errorText, value) == true)
                {
                    OnPropertyChanged(nameof(HasLoadError));
                    OnPropertyChanged(nameof(IsEmpty));
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
                SelectedMessage = string.Empty;
                ClearInspection();
                _detailsRequest++;
                if (value != null)
                {
                    _ = LoadInspectionAsync(value, _detailsRequest);
                }
            }
        }

        public void Clear()
        {
            _remoteUrlRequest++;
            _repository = null;
            _loadedBranchName = string.Empty;
            _remoteUrlLoadedRoot = string.Empty;
            GitHubRemoteUrl = string.Empty;
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
            _loadRequest++;
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
            return await _actionService.GetResetPreviewAsync(repository, commitHash);
        }

        public async Task<bool> ResetAsync(string repositoryRoot, GitResetPreview preview, GitResetMode mode, bool hardConfirmed)
        {
            return await RunActionAsync(repositoryRoot, repository => _actionService.ResetAsync(repository, preview, mode, hardConfirmed), _stringHelper.Format("HistoryResetSucceeded", preview.BranchName, preview.ShortHash));
        }

        public async Task<bool> SwitchDetachedAsync(string repositoryRoot, string commitHash)
        {
            return await RunActionAsync(repositoryRoot, repository => _actionService.SwitchDetachedAsync(repository, commitHash), _stringHelper.Format("HistoryDetachedCheckoutSucceeded", commitHash.Substring(0, 8)));
        }

        public async Task<bool> CreateBranchAsync(string repositoryRoot, string commitHash, string branchName, bool switchToBranch)
        {
            return await RunActionAsync(repositoryRoot, repository => _actionService.CreateBranchAsync(repository, branchName, commitHash, switchToBranch), _stringHelper.Format("HistoryBranchCreated", branchName));
        }

        public async Task<bool> CreateTagAsync(string repositoryRoot, string commitHash, string tagName, string successMessage)
        {
            try
            {
                GitRepository repository = RequireRepository(repositoryRoot);
                return await _operationQueue.EnqueueAsync(repository.RootPath, successMessage, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    return await CreateTagCoreAsync(repository, commitHash, tagName, successMessage);
                });
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                ReportActionError(exception);
                return false;
            }
        }

        private async Task<bool> CreateTagCoreAsync(GitRepository repository, string commitHash, string tagName, string successMessage)
        {
            try
            {
                await _actionService.CreateLightweightTagAsync(repository, tagName, commitHash);
            }
            catch (Exception exception)
            {
                if (_repository?.RootPath == repository.RootPath)
                {
                    ReportActionError(exception);
                }
                return false;
            }

            GitRepository current = _repository;
            if (current == null)
            {
                return true;
            }
            if (current.RootPath != repository.RootPath)
            {
                return true;
            }

            HistoryCommitItem item = Commits.FirstOrDefault(commit => commit.Hash == commitHash);
            item?.AddTagReference(tagName.Trim());
            if (_inspection?.Hash == commitHash)
            {
                OnPropertyChanged(nameof(ReferencesText));
            }
            RepositoryChanged?.Invoke(current);
            ActionMessage?.Invoke(successMessage);
            return true;
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
                throw new GitException(_stringHelper.GetString("HistoryRepositoryRequired"));
            }
            if (_repository.RootPath != repositoryRoot)
            {
                throw new GitException(_stringHelper.GetString("HistoryMenuRepositoryChanged"));
            }
            return _repository;
        }

        private async Task<bool> RunActionAsync(string repositoryRoot, Func<GitRepository, Task<GitRepository>> action, string successMessage)
        {
            try
            {
                GitRepository repository = RequireRepository(repositoryRoot);
                return await _operationQueue.EnqueueAsync(repository.RootPath, successMessage, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    return await RunActionCoreAsync(repository, action, successMessage);
                });
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                ReportActionError(exception);
                return false;
            }
        }

        private async Task<bool> RunActionCoreAsync(GitRepository repository, Func<GitRepository, Task<GitRepository>> action, string successMessage)
        {
            try
            {
                GitRepository updated = await action(repository);
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
            catch (Exception exception)
            {
                if (_repository?.RootPath == repository.RootPath)
                {
                    ReportActionError(exception);
                }
                return false;
            }
        }

        public async Task LoadAsync(GitRepository repository)
        {
            _detailsRequest++;
            bool repositoryChanged = _repository == null || _repository.RootPath != repository.RootPath;
            bool branchChanged = _loadedBranchName != repository.CurrentBranch;
            _repository = repository;
            _loadedBranchName = repository.CurrentBranch;
            if (repositoryChanged)
            {
                GitHubRemoteUrl = string.Empty;
                _remoteUrlLoadedRoot = string.Empty;
            }
            if (repositoryChanged || (_selectedScope == GitHistoryScope.CurrentBranch && branchChanged))
            {
                ResetHistoryList();
            }
            EnsureGitHubRemoteUrl();

            await LoadCoreAsync();
        }

        private void EnsureGitHubRemoteUrl()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            if (ExternalAuthorPhotosEnabled == false)
            {
                return;
            }
            if (_remoteUrlLoadedRoot == repository.RootPath)
            {
                return;
            }
            _remoteUrlLoadedRoot = repository.RootPath;
            int remoteRequest = ++_remoteUrlRequest;
            _ = LoadGitHubRemoteUrlAsync(repository, remoteRequest);
        }

        private async Task LoadGitHubRemoteUrlAsync(GitRepository repository, int request)
        {
            try
            {
                IReadOnlyList<string> urls = await _historyService.GetRemoteUrlsAsync(repository);
                if (request != _remoteUrlRequest)
                {
                    return;
                }
                if (_repository == null)
                {
                    return;
                }
                if (_repository.RootPath != repository.RootPath)
                {
                    return;
                }
                foreach (string url in urls)
                {
                    if (GitHubRepositoryAddress.TryParse(url, out string owner, out string name) == false)
                    {
                        continue;
                    }
                    GitHubRemoteUrl = $"https://github.com/{owner}/{name}";
                    return;
                }
                GitHubRemoteUrl = string.Empty;
            }
            catch (Exception)
            {
                // Photo lookup is optional; local icons remain available.
            }
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
            int request = ++_loadRequest;
            IsLoadingMore = true;
            IsLoading = true;
            try
            {
                GitHistoryPage page = await _historyService.GetHistoryPageAsync(repository, existingCount - 1, 201, scope);
                if (request != _loadRequest)
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
                if (page.Commits.Count <= 1)
                {
                    throw new GitException(_stringHelper.GetString("HistoryCommitListChanged"));
                }
                if (Commits[existingCount - 1].Hash != page.Commits[0].Hash)
                {
                    throw new GitException(_stringHelper.GetString("HistoryCommitListChanged"));
                }

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
                if (request != _loadRequest)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                PageErrorText = _errorLocalizer.GetDisplayMessage(exception);
            }
            finally
            {
                if (request == _loadRequest)
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
            int request = ++_loadRequest;
            IsLoadingMore = false;
            IsLoading = true;
            ErrorText = string.Empty;
            PageErrorText = string.Empty;
            try
            {
                GitHistoryPage page = await _historyService.GetHistoryPageAsync(repository, 0, 200, scope);
                if (request != _loadRequest)
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
                if (request == _loadRequest)
                {
                    ErrorText = _errorLocalizer.GetDisplayMessage(exception);
                    throw;
                }
            }
            finally
            {
                if (request == _loadRequest)
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
            HistoryCommitItem item = new(commit, graph, graphWidth, _stringHelper);
            item.ExternalPhotosEnabled = ExternalAuthorPhotosEnabled;
            item.GitHubRemoteUrl = GitHubRemoteUrl;
            return item;
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
            return Math.Min(180, 32 + maximumLane * 16);
        }

        private void ClearInspection()
        {
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
            AuxiliaryTitle = string.Empty;
            AuxiliaryText = string.Empty;
            OnPropertyChanged(nameof(HasAuxiliary));
        }

        private async Task LoadInspectionAsync(HistoryCommitItem selected, int request)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            try
            {
                GitCommitInspection details = await _inspectionService.GetCommitAsync(repository, selected.Hash);
                string fullMessage = await _commitMessageService.GetMessageAsync(repository, selected.Hash);
                if (request != _detailsRequest)
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
                _inspection = details;
                OnPropertyChanged(nameof(Inspection));
                OnPropertyChanged(nameof(AuthorDescription));
                OnPropertyChanged(nameof(AuthoredAtText));
                OnPropertyChanged(nameof(ReferencesText));
                OnPropertyChanged(nameof(HasParents));
                OnPropertyChanged(nameof(HasMultipleParents));
                OnPropertyChanged(nameof(ChangesSummary));
                SelectedMessage = fullMessage;
                if (details.Parents.Count > 0)
                {
                    SelectedParent = details.Parents[0];
                }
                else
                {
                    await LoadChangesAsync();
                }
                if (SelectedTab == 2)
                {
                    await LoadTreeAsync();
                }
            }
            catch (Exception exception)
            {
                if (request == _detailsRequest)
                {
                    ErrorText = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
        }

        private async Task LoadChangesAsync()
        {
            _expandRequest++;
            GitRepository repository = _repository;
            GitCommitInspection inspection = _inspection;
            string parent = SelectedParent;
            int request = ++_inspectionRequest;
            ChangedFiles.Clear();
            VisibleChangedFiles.Clear();
            OnPropertyChanged(nameof(ComparisonText));
            if (repository == null)
            {
                return;
            }
            if (inspection == null)
            {
                return;
            }
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
                OnPropertyChanged(nameof(HasNoChangedFiles));
            }
            catch (Exception exception)
            {
                if (request == _inspectionRequest) ErrorText = _errorLocalizer.GetDisplayMessage(exception);
            }
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
                file.DiffReason = LocalizeFileReason(diff.ReasonCode, diff.ReasonArguments);
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
                    file.DiffReason = _errorLocalizer.GetDisplayMessage(exception);
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
                GitCommitTreeListing listing = await _inspectionService.GetTreeEntriesAsync(repository, inspection.Hash);
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
                if (request == _treeRequest) ErrorText = _errorLocalizer.GetDisplayMessage(exception);
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
            if (item.IsLoading == true)
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
                GitCommitTreeListing listing = await _inspectionService.GetTreeEntriesAsync(repository, inspection.Hash, item.Path);
                if (_repository != repository)
                {
                    return;
                }
                if (_inspection != inspection)
                {
                    return;
                }
                if (item.IsExpanded == false)
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
                if (request == _treeFilterRequest) ErrorText = _errorLocalizer.GetDisplayMessage(exception);
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
                catch (Exception exception) { ErrorText = _errorLocalizer.GetDisplayMessage(exception); }
                return;
            }
            await OpenFileAsync(item.Path, false);
        }

        public async Task ShowInTreeAsync(string path)
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
            TreeSearch = string.Empty;
            SelectedTab = 2;
            await LoadTreeAsync();
            if (_repository != repository)
            {
                return;
            }
            if (_inspection != inspection)
            {
                return;
            }
            string[] segments = path.Split('/');
            ObservableCollection<HistoryTreeItem> level = TreeRoots;
            HistoryTreeItem found = null;
            string current = string.Empty;
            foreach (string segment in segments)
            {
                if (current.Length > 0) current += "/";
                current += segment;
                found = level.FirstOrDefault(item => item.Path == current);
                if (found == null)
                {
                    return;
                }
                if (found.IsDirectory == true)
                {
                    await ExpandTreeAsync(found);
                    if (_repository != repository)
                    {
                        return;
                    }
                    if (_inspection != inspection)
                    {
                        return;
                    }
                    level = found.Children;
                }
            }
            SelectedTreeFile = found;
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
                PreviewReason = _stringHelper.GetString("HistoryPreviewFileAbsent");
                return;
            }
            int request = ++_previewRequest;
            PreviewPath = $"{path} @ {revision.Substring(0, 8)}";
            PreviewText = string.Empty;
            PreviewReason = _stringHelper.GetString("HistoryPreviewLoading");
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
                PreviewReason = LocalizeFileReason(content.ReasonCode, content.ReasonArguments);
                if (content.ReasonCode == "HistorySubmoduleGitlink")
                {
                    PreviewReason = _stringHelper.Format("HistoryPreviewTarget", PreviewReason, content.ObjectHash);
                }
            }
            catch (Exception exception)
            {
                if (request == _previewRequest) PreviewReason = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        public async Task ShowFileHistoryAsync(string path)
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
            int request = ++_previewRequest;
            try
            {
                GitFileHistoryPage page = await _inspectionService.GetFileHistoryAsync(repository, inspection.Hash, path, 100);
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
                AuxiliaryTitle = _stringHelper.Format("HistoryAuxiliaryFileHistory", path, inspection.Hash.Substring(0, 8));
                AuxiliaryText = string.Join("\n", page.Entries.Select(entry => $"{entry.CommitHash.Substring(0, 8)}  {entry.AuthoredAt:yyyy-MM-dd}  {entry.Author}  {entry.Title}  {entry.PreviousPath}"));
                OnPropertyChanged(nameof(HasAuxiliary));
            }
            catch (Exception exception) { ErrorText = _errorLocalizer.GetDisplayMessage(exception); }
        }

        public GitRepository RequireSelectedRepository(string root, string hash)
        {
            GitRepository repository = RequireRepository(root);
            if (_inspection == null)
            {
                throw new GitException(_stringHelper.GetString("HistoryCommitRequired"));
            }
            if (_inspection.Hash != hash)
            {
                throw new GitException(_stringHelper.GetString("HistorySelectedCommitChanged"));
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

        public void CloseAuxiliary()
        {
            AuxiliaryTitle = string.Empty;
            AuxiliaryText = string.Empty;
            OnPropertyChanged(nameof(HasAuxiliary));
        }

        private string LocalizeFileReason(string reasonCode, IReadOnlyList<object> arguments)
        {
            if (string.IsNullOrEmpty(reasonCode))
            {
                return string.Empty;
            }
            if (arguments.Count == 0)
            {
                return _stringHelper.GetString(reasonCode);
            }
            return _stringHelper.Format(reasonCode, arguments.ToArray());
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

    public class HistoryLabels
    {
        private readonly StringHelper _strings;

        public HistoryLabels(StringHelper strings)
        {
            _strings = strings;
        }

        public string Heading { get { return _strings.GetString("HistoryHeading"); } }
        public string ScopeAll { get { return _strings.GetString("HistoryScopeAll"); } }
        public string ScopeAllTooltip { get { return _strings.GetString("HistoryScopeAllTooltip"); } }
        public string ScopeAllAccessible { get { return _strings.GetString("HistoryScopeAllAccessible"); } }
        public string ScopeCurrent { get { return _strings.GetString("HistoryScopeCurrent"); } }
        public string ScopeCurrentTooltip { get { return _strings.GetString("HistoryScopeCurrentTooltip"); } }
        public string ScopeCurrentAccessible { get { return _strings.GetString("HistoryScopeCurrentAccessible"); } }
        public string ExternalPhotos { get { return _strings.GetString("HistoryExternalPhotos"); } }
        public string ExternalPhotosTooltip { get { return _strings.GetString("HistoryExternalPhotosTooltip"); } }
        public string ExternalPhotosAccessible { get { return _strings.GetString("HistoryExternalPhotosAccessible"); } }
        public string CopySha { get { return _strings.GetString("HistoryCopySha"); } }
        public string CreateBranchHere { get { return _strings.GetString("HistoryCreateBranchHere"); } }
        public string CreateTagHere { get { return _strings.GetString("HistoryCreateTagHere"); } }
        public string CheckoutCommit { get { return _strings.GetString("HistoryCheckoutCommit"); } }
        public string ResetCommit { get { return _strings.GetString("HistoryResetCommit"); } }
        public string MoreReferencesTooltip { get { return _strings.GetString("HistoryMoreReferencesTooltip"); } }
        public string AllReferencesHeading { get { return _strings.GetString("HistoryAllReferencesHeading"); } }
        public string Empty { get { return _strings.GetString("HistoryEmpty"); } }
        public string LoadingCommits { get { return _strings.GetString("HistoryLoadingCommits"); } }
        public string Retry { get { return _strings.GetString("HistoryRetry"); } }
        public string ResizeHistory { get { return _strings.GetString("HistoryResizeHistory"); } }
        public string SelectCommit { get { return _strings.GetString("HistorySelectCommit"); } }
        public string TabCommit { get { return _strings.GetString("HistoryTabCommit"); } }
        public string TabChanges { get { return _strings.GetString("HistoryTabChanges"); } }
        public string TabFileTree { get { return _strings.GetString("HistoryTabFileTree"); } }
        public string ParentTooltip { get { return _strings.GetString("HistoryParentTooltip"); } }
        public string Copy { get { return _strings.GetString("HistoryCopy"); } }
        public string ExpandAll { get { return _strings.GetString("HistoryExpandAll"); } }
        public string CollapseAll { get { return _strings.GetString("HistoryCollapseAll"); } }
        public string NoChanges { get { return _strings.GetString("HistoryNoChanges"); } }
        public string Open { get { return _strings.GetString("HistoryOpen"); } }
        public string ShowExplorer { get { return _strings.GetString("HistoryShowExplorer"); } }
        public string FileHistory { get { return _strings.GetString("HistoryFileHistory"); } }
        public string ShowTree { get { return _strings.GetString("HistoryShowTree"); } }
        public string SaveAs { get { return _strings.GetString("HistorySaveAs"); } }
        public string CopyPath { get { return _strings.GetString("HistoryCopyPath"); } }
        public string LoadingDiff { get { return _strings.GetString("HistoryLoadingDiff"); } }
        public string SearchChangedPaths { get { return _strings.GetString("HistorySearchChangedPaths"); } }
        public string ResizeChanges { get { return _strings.GetString("HistoryResizeChanges"); } }
        public string SearchSnapshotPaths { get { return _strings.GetString("HistorySearchSnapshotPaths"); } }
        public string ResizeTree { get { return _strings.GetString("HistoryResizeTree"); } }
        public string CloseAuxiliary { get { return _strings.GetString("HistoryCloseAuxiliary"); } }
    }
}
