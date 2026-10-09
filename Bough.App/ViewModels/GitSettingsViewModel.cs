using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Controls;
using Bough.App.Appearance;
using Bough.App.Commands;
using Bough.App.Localization;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;
using Bough.App.Internals;
using Bough.App.Presenters;
using Bough.App.ViewModels.Models;

namespace Bough.App.ViewModels
{
    public class GitSettingsViewModel : ViewModelBase
    {
        private static readonly string _globalConfigQueueRoot = Path.Combine(Path.GetTempPath(), "Bough", "global-git-config-queue");
        private readonly GitSettingsService _settingsService;
        private readonly GitHubAccountService _gitHubAccountService;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly AppearanceThemeService _appearanceTheme;
        private readonly LanguageSelectionPresenter _languagePresenter;
        private readonly GitOperationQueue _operationQueue;
        private readonly GitExecutablePresenter _gitExecutablePresenter;
        private readonly ObservableCollection<GitRemote> _remotes;
        private readonly ObservableCollection<GitHubRemoteAccountItem> _gitHubRemotes;
        private readonly Dictionary<string, string> _displayLabels = new();
        private GitRepository _repository;
        private string _accountRepositoryText = string.Empty;
        private string _gitPathInput;
        private string _gitVersion;
        private string _localName;
        private string _localEmail;
        private string _globalName;
        private string _globalEmail;
        private string _authorStatus;
        private string _statusMessage;
        private GitExecutableActionResult _gitPathResult;
        private GitSettingsDisplayResult _displayResult;
        private GitSettingsDisplayResult _statusResult;
        private string _accountStatusText = string.Empty;
        private string _appearanceStatus = string.Empty;
        private string _languageStatus = string.Empty;
        private bool _remoteOperationBusy;
        private bool _accountLoginRunning;
        private int _pendingAccountSelections;
        private CancellationTokenSource _accountCancellation;
        private bool _isBusy;
        private int _requestVersion;
        private int _localNameEditVersion;
        private int _localEmailEditVersion;
        private int _globalNameEditVersion;
        private int _globalEmailEditVersion;

        public GitSettingsViewModel(GitSettingsService settingsService, GitHubAccountService gitHubAccountService, StringHelper stringHelper, AppearanceThemeService appearanceTheme, GitErrorLocalizer errorLocalizer, GitOperationQueue operationQueue, LanguageSelectionPresenter languagePresenter)
        {
            _settingsService = settingsService;
            _gitHubAccountService = gitHubAccountService;
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            _appearanceTheme = appearanceTheme;
            _languagePresenter = languagePresenter;
            _operationQueue = operationQueue;
            _gitExecutablePresenter = new GitExecutablePresenter(settingsService);
            _appearanceTheme.ThemeChanged += OnAppearanceThemeChanged;
            _settingsService.DefaultPullStrategyChanged += OnDefaultPullStrategyChanged;
            _gitPathInput = settingsService.ConfiguredGitPath;
            _gitVersion = string.Empty;
            _localName = string.Empty;
            _localEmail = string.Empty;
            _globalName = string.Empty;
            _globalEmail = string.Empty;
            _authorStatus = string.Empty;
            _statusMessage = string.Empty;
            _remotes = [];
            _gitHubRemotes = [];
            Remotes = new ReadOnlyObservableCollection<GitRemote>(_remotes);
            GitHubRemotes = new ReadOnlyObservableCollection<GitHubRemoteAccountItem>(_gitHubRemotes);
            TestGitCommand = new AsyncRelayCommand(TestGitAsync, CanRun);
            SaveGitPathCommand = new AsyncRelayCommand(SaveGitPathAsync, CanRun);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanUseRepository);
            SaveLocalAuthorCommand = new QueuedAsyncRelayCommand(SaveLocalAuthorAsync, CanQueueRepositoryMutation, exception => ShowDisplayError(GitSettingsDisplayTarget.Status, exception));
            SaveGlobalAuthorCommand = new QueuedAsyncRelayCommand(SaveGlobalAuthorAsync, CanQueueRepositoryMutation, exception => ShowDisplayError(GitSettingsDisplayTarget.Status, exception));
        }

        public ReadOnlyObservableCollection<GitRemote> Remotes { get; }
        public GitRepository CurrentRepository { get { return _repository; } }
        public string AccountRepositoryText { get { return _accountRepositoryText; } }
        public ReadOnlyObservableCollection<GitHubRemoteAccountItem> GitHubRemotes { get; }
        public string AccountStatusText { get { return _accountStatusText; } private set { SetProperty(ref _accountStatusText, value); } }
        public string AppearanceHeading { get { return GetDisplayLabel(nameof(AppearanceHeading)); } }
        public string LanguageHeading { get { return GetDisplayLabel(nameof(LanguageHeading)); } }
        public string LanguageDescription { get { return GetDisplayLabel(nameof(LanguageDescription)); } }
        public string KoreanLanguageLabel { get { return GetDisplayLabel(nameof(KoreanLanguageLabel)); } }
        public string EnglishLanguageLabel { get { return GetDisplayLabel(nameof(EnglishLanguageLabel)); } }
        public string LanguageStatus { get { return _languageStatus; } private set { SetProperty(ref _languageStatus, value); } }
        public bool IsKoreanLanguage { get { return _stringHelper.Language == StringLanguage.Korean; } }
        public bool IsEnglishLanguage { get { return _stringHelper.Language == StringLanguage.English; } }
        public string AppearanceDescription { get { return GetDisplayLabel(nameof(AppearanceDescription)); } }
        public string AppearanceLightLabel { get { return GetDisplayLabel(nameof(AppearanceLightLabel)); } }
        public string AppearanceDarkLabel { get { return GetDisplayLabel(nameof(AppearanceDarkLabel)); } }
        public string AppearanceSystemLabel { get { return GetDisplayLabel(nameof(AppearanceSystemLabel)); } }
        public string DefaultPullStrategyHeading { get { return GetDisplayLabel(nameof(DefaultPullStrategyHeading)); } }
        public string DefaultPullStrategyDescription { get { return GetDisplayLabel(nameof(DefaultPullStrategyDescription)); } }
        public string PullStrategyFastForwardOnlyLabel { get { return GetDisplayLabel(nameof(PullStrategyFastForwardOnlyLabel)); } }
        public string PullStrategyMergeLabel { get { return GetDisplayLabel(nameof(PullStrategyMergeLabel)); } }
        public string PullStrategyRebaseLabel { get { return GetDisplayLabel(nameof(PullStrategyRebaseLabel)); } }
        public string SettingsTitle { get { return GetDisplayLabel(nameof(SettingsTitle)); } }
        public string GitSettingsHint { get { return GetDisplayLabel(nameof(GitSettingsHint)); } }
        public string RefreshLabel { get { return GetDisplayLabel(nameof(RefreshLabel)); } }
        public string GitExecutableHeading { get { return GetDisplayLabel(nameof(GitExecutableHeading)); } }
        public string GitPathHint { get { return GetDisplayLabel(nameof(GitPathHint)); } }
        public string GitPathPlaceholder { get { return GetDisplayLabel(nameof(GitPathPlaceholder)); } }
        public string BrowseGitLabel { get { return GetDisplayLabel(nameof(BrowseGitLabel)); } }
        public string TestGitLabel { get { return GetDisplayLabel(nameof(TestGitLabel)); } }
        public string SaveGitPathLabel { get { return GetDisplayLabel(nameof(SaveGitPathLabel)); } }
        public string CommitAuthorHeading { get { return GetDisplayLabel(nameof(CommitAuthorHeading)); } }
        public string LocalAuthorHeading { get { return GetDisplayLabel(nameof(LocalAuthorHeading)); } }
        public string SaveLocalAuthorLabel { get { return GetDisplayLabel(nameof(SaveLocalAuthorLabel)); } }
        public string GlobalAuthorHeading { get { return GetDisplayLabel(nameof(GlobalAuthorHeading)); } }
        public string SaveGlobalAuthorLabel { get { return GetDisplayLabel(nameof(SaveGlobalAuthorLabel)); } }
        public string AccountsHeading { get { return GetDisplayLabel(nameof(AccountsHeading)); } }
        public string AccountsDescription { get { return GetDisplayLabel(nameof(AccountsDescription)); } }
        public string AccountsPurpose { get { return GetDisplayLabel(nameof(AccountsPurpose)); } }
        public string AccountsQueueHint { get { return GetDisplayLabel(nameof(AccountsQueueHint)); } }
        public string RemoteUrlsHeading { get { return GetDisplayLabel(nameof(RemoteUrlsHeading)); } }

        private string GetDisplayLabel(string propertyName)
        {
            if (_displayLabels.TryGetValue(propertyName, out string value))
            {
                return value;
            }
            return string.Empty;
        }

        public void SetDisplayLabels(IReadOnlyDictionary<string, string> labels)
        {
            foreach (KeyValuePair<string, string> label in labels)
            {
                _displayLabels[label.Key] = label.Value;
                OnPropertyChanged(label.Key);
            }
        }

        public void SetAccountRepositoryText(string value)
        {
            SetProperty(ref _accountRepositoryText, value, nameof(AccountRepositoryText));
        }
        public string AppearanceStatus { get { return _appearanceStatus; } private set { SetProperty(ref _appearanceStatus, value); } }
        public bool IsLightAppearance { get { return _appearanceTheme.SelectedMode == AppearanceThemeMode.Light; } }
        public bool IsDarkAppearance { get { return _appearanceTheme.SelectedMode == AppearanceThemeMode.Dark; } }
        public bool IsSystemAppearance { get { return _appearanceTheme.SelectedMode == AppearanceThemeMode.System; } }
        public bool IsFastForwardOnlyPull { get { return _settingsService.DefaultPullStrategy == GitPullStrategy.FastForwardOnly; } }
        public bool IsMergePull { get { return _settingsService.DefaultPullStrategy == GitPullStrategy.Merge; } }
        public bool IsRebasePull { get { return _settingsService.DefaultPullStrategy == GitPullStrategy.Rebase; } }
        public bool IsRemoteOperationBusy { get { return _remoteOperationBusy; } }

        public async Task SelectLanguageAsync(StringLanguage language)
        {
            try
            {
                await _languagePresenter.SelectAsync(language);
                ShowDisplayResult(GitSettingsDisplayTarget.Language, "AppLanguageSaved");
            }
            catch (Exception exception)
            {
                ShowDisplayError(GitSettingsDisplayTarget.Language, exception);
            }
            finally
            {
                OnPropertyChanged(nameof(IsKoreanLanguage));
                OnPropertyChanged(nameof(IsEnglishLanguage));
            }
        }

        public async Task SelectAppearanceAsync(AppearanceThemeMode mode)
        {
            try
            {
                await _appearanceTheme.SetThemeAsync(mode);
                ShowDisplayResult(GitSettingsDisplayTarget.Appearance, string.Empty);
            }
            catch (Exception exception)
            {
                ShowDisplayError(GitSettingsDisplayTarget.Appearance, exception);
            }
        }

        private void OnAppearanceThemeChanged(AppearanceThemeMode mode)
        {
            OnPropertyChanged(nameof(IsLightAppearance));
            OnPropertyChanged(nameof(IsDarkAppearance));
            OnPropertyChanged(nameof(IsSystemAppearance));
        }

        public void SelectDefaultPullStrategy(GitPullStrategy strategy)
        {
            try
            {
                _settingsService.SaveDefaultPullStrategy(strategy);
                ShowDisplayResult(GitSettingsDisplayTarget.Status, "DefaultPullStrategySaved", strategy);
            }
            catch (Exception exception)
            {
                ShowDisplayError(GitSettingsDisplayTarget.Status, exception);
            }
        }

        private void OnDefaultPullStrategyChanged(GitPullStrategy strategy)
        {
            OnPropertyChanged(nameof(IsFastForwardOnlyPull));
            OnPropertyChanged(nameof(IsMergePull));
            OnPropertyChanged(nameof(IsRebasePull));
        }

        public AsyncRelayCommand TestGitCommand { get; }
        public AsyncRelayCommand SaveGitPathCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public QueuedAsyncRelayCommand SaveLocalAuthorCommand { get; }
        public QueuedAsyncRelayCommand SaveGlobalAuthorCommand { get; }
        public string GitPathInput
        {
            get { return _gitPathInput; }
            set
            {
                if (SetProperty(ref _gitPathInput, value) == false)
                {
                    return;
                }

                _gitExecutablePresenter.Invalidate();
                GitPathResult = null;
                GitVersion = string.Empty;
                DisplayResult = null;
                StatusMessage = string.Empty;
            }
        }
        public string GitVersion { get { return _gitVersion; } private set { SetProperty(ref _gitVersion, value); } }
        public GitExecutableActionResult GitPathResult
        {
            get { return _gitPathResult; }
            private set
            {
                if (value != null)
                {
                    _statusResult = null;
                }
                SetProperty(ref _gitPathResult, value);
            }
        }
        public GitSettingsDisplayResult DisplayResult
        {
            get { return _displayResult; }
            private set
            {
                if (value == null)
                {
                    _statusResult = null;
                }
                else if (value.Target == GitSettingsDisplayTarget.Status)
                {
                    _statusResult = value;
                }
                SetProperty(ref _displayResult, value);
            }
        }
        public GitSettingsDisplayResult StatusResult { get { return _statusResult; } }
        internal StringHelper Strings { get { return _stringHelper; } }
        internal GitErrorLocalizer Errors { get { return _errorLocalizer; } }

        public void SetGitPathStatusMessage(GitExecutableActionResult result, string message)
        {
            if (ReferenceEquals(GitPathResult, result) == false)
            {
                return;
            }

            StatusMessage = message;
        }

        public void SetDisplayMessage(GitSettingsDisplayResult result, string message)
        {
            if (result.Target == GitSettingsDisplayTarget.Status)
            {
                if (ReferenceEquals(StatusResult, result) == false)
                {
                    return;
                }
                StatusMessage = message;
                return;
            }
            if (ReferenceEquals(DisplayResult, result) == false)
            {
                return;
            }

            if (result.Target == GitSettingsDisplayTarget.Language)
            {
                LanguageStatus = message;
                return;
            }
            if (result.Target == GitSettingsDisplayTarget.Appearance)
            {
                AppearanceStatus = message;
                return;
            }
            if (result.Target == GitSettingsDisplayTarget.Account)
            {
                result.Account.StatusText = message;
                return;
            }
            if (result.Target == GitSettingsDisplayTarget.AccountSummary)
            {
                AccountStatusText = message;
                return;
            }

            StatusMessage = message;
        }

        public void SetAuthorStatusText(string message)
        {
            AuthorStatus = message;
        }

        private void ShowDisplayResult(GitSettingsDisplayTarget target, string code, params object[] arguments)
        {
            DisplayResult = new GitSettingsDisplayResult(target, code, arguments, null, null);
        }

        private void ShowDisplayError(GitSettingsDisplayTarget target, Exception error)
        {
            DisplayResult = new GitSettingsDisplayResult(target, string.Empty, Array.Empty<object>(), error, null);
        }

        private void ShowAccountResult(GitHubRemoteAccountItem account, string code, params object[] arguments)
        {
            DisplayResult = new GitSettingsDisplayResult(GitSettingsDisplayTarget.Account, code, arguments, null, account);
        }

        private void ShowAccountError(GitHubRemoteAccountItem account, Exception error)
        {
            DisplayResult = new GitSettingsDisplayResult(GitSettingsDisplayTarget.Account, string.Empty, Array.Empty<object>(), error, account);
        }
        public string LocalName
        {
            get { return _localName; }
            set
            {
                if (SetProperty(ref _localName, value))
                {
                    _localNameEditVersion++;
                }
            }
        }
        public string LocalEmail
        {
            get { return _localEmail; }
            set
            {
                if (SetProperty(ref _localEmail, value))
                {
                    _localEmailEditVersion++;
                }
            }
        }
        public string GlobalName
        {
            get { return _globalName; }
            set
            {
                if (SetProperty(ref _globalName, value))
                {
                    _globalNameEditVersion++;
                }
            }
        }
        public string GlobalEmail
        {
            get { return _globalEmail; }
            set
            {
                if (SetProperty(ref _globalEmail, value))
                {
                    _globalEmailEditVersion++;
                }
            }
        }
        public string AuthorStatus { get { return _authorStatus; } private set { SetProperty(ref _authorStatus, value); } }
        public string StatusMessage { get { return _statusMessage; } private set { SetProperty(ref _statusMessage, value); } }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    TestGitCommand.NotifyCanExecuteChanged();
                    SaveGitPathCommand.NotifyCanExecuteChanged();
                    RefreshCommand.NotifyCanExecuteChanged();
                    SaveLocalAuthorCommand.NotifyCanExecuteChanged();
                    SaveGlobalAuthorCommand.NotifyCanExecuteChanged();
                    UpdateAccountAvailability();
                }
            }
        }

        public async Task SetRepositoryAsync(GitRepository repository)
        {
            _requestVersion++;
            _accountCancellation?.Cancel();
            _repository = repository;
            OnPropertyChanged(nameof(CurrentRepository));
            DisplayResult = null;
            StatusMessage = string.Empty;
            SaveLocalAuthorCommand.NotifyCanExecuteChanged();
            SaveGlobalAuthorCommand.NotifyCanExecuteChanged();
            ClearRepositoryValues();
            if (repository == null)
            {
                IsBusy = false;
                return;
            }

            await RefreshCoreAsync(false);
        }

        public async Task RefreshAsync()
        {
            await RefreshCoreAsync(true);
        }

        public void SetRemoteOperationBusy(bool busy)
        {
            if (_remoteOperationBusy == busy)
            {
                return;
            }
            _remoteOperationBusy = busy;
            OnPropertyChanged(nameof(IsRemoteOperationBusy));
            UpdateAccountAvailability();
        }

        public async Task SelectAccountAsync(GitHubRemoteAccountItem item, string userName)
        {
            if (item == null)
            {
                return;
            }
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            if (_gitHubRemotes.Contains(item) == false)
            {
                return;
            }
            if (item.IsSupported == false)
            {
                ShowAccountResult(item, item.UnavailableReasonCode);
                return;
            }
            string selectedName = userName?.Trim() ?? string.Empty;
            if (selectedName.Length == 0)
            {
                ShowAccountResult(item, "GitHubUserNameRequired");
                return;
            }
            string remoteName = item.RemoteName;
            string successMessage = _stringHelper.Format("GitHubAccountSelected", remoteName, selectedName);
            ShowAccountResult(item, "GcmOperationInProgress");
            _pendingAccountSelections++;
            UpdateAccountAvailability();
            try
            {
                await _operationQueue.EnqueueAsync(repository.RootPath, successMessage, async token =>
                {
                    int request = 0;
                    if (IsCurrentRepository(repository))
                    {
                        _accountCancellation?.Cancel();
                        request = ++_requestVersion;
                        IsBusy = true;
                    }
                    try
                    {
                        await _gitHubAccountService.SaveSelectedAccountAsync(repository, remoteName, selectedName, token);
                        if (IsCurrentRepository(repository) == false)
                        {
                            return;
                        }
                        await RefreshAccountsAsync(repository, request, remoteName, selectedName, token);
                        if (request != _requestVersion)
                        {
                            return;
                        }
                        if (IsCurrentRepository(repository) == false)
                        {
                            return;
                        }
                        GitHubRemoteAccountItem refreshed = FindAccountRemote(remoteName);
                        if (refreshed != null)
                        {
                            ShowAccountResult(refreshed, "GitHubAccountSelected", remoteName, selectedName);
                        }
                    }
                    finally
                    {
                        if (IsCurrentRepository(repository))
                        {
                            IsBusy = false;
                        }
                    }
                });
            }
            catch (Exception exception)
            {
                ShowAccountError(item, exception);
            }
            finally
            {
                _pendingAccountSelections--;
                UpdateAccountAvailability();
            }
        }

        public async Task LoginAccountAsync(GitHubRemoteAccountItem item)
        {
            if (item == null)
            {
                return;
            }
            string requestedName = item.CandidateName?.Trim() ?? string.Empty;
            await RunAccountActionAsync(item, requestedName,
                (repository, token) => _gitHubAccountService.LoginAsync(repository, requestedName, token),
                "GcmLoginCompleted");
        }

        private async Task RunAccountActionAsync(GitHubRemoteAccountItem item, string candidateName,
            Func<GitRepository, CancellationToken, Task> action, string successCode)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            if (_gitHubRemotes.Contains(item) == false)
            {
                return;
            }
            if (item.CanChange == false)
            {
                return;
            }
            if (IsBusy)
            {
                return;
            }
            if (_remoteOperationBusy)
            {
                return;
            }

            _accountCancellation?.Cancel();
            using CancellationTokenSource cancellation = new();
            _accountCancellation = cancellation;
            int request = ++_requestVersion;
            _accountLoginRunning = true;
            IsBusy = true;
            UpdateAccountAvailability();
            ShowAccountResult(item, "GcmOperationInProgress");
            try
            {
                await action(repository, cancellation.Token);
                if (request != _requestVersion)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                await RefreshAccountsAsync(repository, request, item.RemoteName, candidateName, cancellation.Token);
                if (request != _requestVersion)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                GitHubRemoteAccountItem refreshed = FindAccountRemote(item.RemoteName);
                if (refreshed != null)
                {
                    ShowAccountResult(refreshed, successCode);
                }
            }
            catch (OperationCanceledException)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    ShowAccountResult(item, "GitHubAccountActionCanceled");
                }
            }
            catch (Exception exception)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    ShowAccountError(item, exception);
                }
            }
            finally
            {
                if (_accountCancellation == cancellation)
                {
                    _accountCancellation = null;
                }
                _accountLoginRunning = false;
                UpdateAccountAvailability();
                if (request == _requestVersion)
                {
                    IsBusy = false;
                }
            }
        }

        private async Task<bool> RefreshCoreAsync(bool reportSuccess, string savedCode = null)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return false;
            }

            int request = ++_requestVersion;
            int localNameEditVersion = _localNameEditVersion;
            int localEmailEditVersion = _localEmailEditVersion;
            int globalNameEditVersion = _globalNameEditVersion;
            int globalEmailEditVersion = _globalEmailEditVersion;
            _accountCancellation?.Cancel();
            CancellationTokenSource cancellation = new();
            _accountCancellation = cancellation;
            Task accountRefresh = null;
            IsBusy = true;
            try
            {
                GitSettingsSnapshot snapshot = await _settingsService.GetSnapshotAsync(repository);
                if (request != _requestVersion)
                {
                    return false;
                }

                if (ReferenceEquals(repository, _repository) == false)
                {
                    return false;
                }
                if (savedCode == null)
                {
                    if (localNameEditVersion == _localNameEditVersion)
                    {
                        LocalName = snapshot.LocalName;
                    }
                    if (localEmailEditVersion == _localEmailEditVersion)
                    {
                        LocalEmail = snapshot.LocalEmail;
                    }
                    if (globalNameEditVersion == _globalNameEditVersion)
                    {
                        GlobalName = snapshot.GlobalName;
                    }
                    if (globalEmailEditVersion == _globalEmailEditVersion)
                    {
                        GlobalEmail = snapshot.GlobalEmail;
                    }
                }
                _remotes.Clear();
                foreach (GitRemote remote in snapshot.Remotes) { _remotes.Add(remote); }
                if (reportSuccess)
                {
                    ShowDisplayResult(GitSettingsDisplayTarget.Status, "GitSettingsRefreshed");
                }
                IsBusy = false;
                accountRefresh = RefreshAccountsInBackgroundAsync(repository, request, cancellation);
                return true;
            }
            catch (Exception exception)
            {
                if (request != _requestVersion)
                {
                    return false;
                }
                if (ReferenceEquals(repository, _repository) == false)
                {
                    return false;
                }
                if (savedCode == null)
                {
                    ShowDisplayError(GitSettingsDisplayTarget.Status, exception);
                }
                else
                {
                    DisplayResult = new GitSettingsDisplayResult(GitSettingsDisplayTarget.Status, savedCode, Array.Empty<object>(), null, null, exception);
                }
                return false;
            }
            finally
            {
                if (accountRefresh == null)
                {
                    if (_accountCancellation == cancellation)
                    {
                        _accountCancellation = null;
                    }
                    cancellation.Dispose();
                }
                if (request == _requestVersion)
                {
                    IsBusy = false;
                }
            }
        }

        private async Task RefreshAccountsInBackgroundAsync(GitRepository repository, int request,
            CancellationTokenSource cancellation)
        {
            try
            {
                await RefreshAccountsAsync(repository, request, string.Empty, string.Empty, cancellation.Token);
            }
            catch (Exception exception)
            {
                if (request != _requestVersion)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                ShowDisplayError(GitSettingsDisplayTarget.AccountSummary, exception);
            }
            finally
            {
                if (_accountCancellation == cancellation)
                {
                    _accountCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private async Task RefreshAccountsAsync(GitRepository repository, int request, string preserveRemote,
            string preserveCandidate, CancellationToken cancellationToken)
        {
            _gitHubRemotes.Clear();
            ShowDisplayResult(GitSettingsDisplayTarget.AccountSummary, "GitHubAccountsLoading");
            try
            {
                GitHubAccountSnapshot snapshot = await _gitHubAccountService.GetSnapshotAsync(repository, cancellationToken);
                if (request != _requestVersion)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }

                Dictionary<string, int> urlCounts = new(StringComparer.Ordinal);
                foreach (GitHubRemoteAccount remote in snapshot.Remotes)
                {
                    if (remote.CanSelect == false)
                    {
                        continue;
                    }
                    if (urlCounts.TryGetValue(remote.FetchUrl, out int count))
                    {
                        urlCounts[remote.FetchUrl] = count + 1;
                    }
                    else
                    {
                        urlCounts.Add(remote.FetchUrl, 1);
                    }
                }
                foreach (GitHubRemoteAccount remote in snapshot.Remotes)
                {
                    bool sharedUrl = urlCounts.TryGetValue(remote.FetchUrl, out int count) && count > 1;
                    GitHubRemoteAccountItem item = new(remote, snapshot.KnownAccounts, sharedUrl);
                    if (remote.RemoteName == preserveRemote && preserveCandidate.Length > 0)
                    {
                        item.CandidateName = preserveCandidate;
                    }
                    _gitHubRemotes.Add(item);
                }
                UpdateAccountAvailability();
                if (snapshot.IsGcmAvailable == false)
                {
                    ShowDisplayResult(GitSettingsDisplayTarget.AccountSummary, snapshot.AvailabilityMessageCode);
                }
                else if (snapshot.AccountListErrorCode.Length > 0)
                {
                    ShowDisplayResult(GitSettingsDisplayTarget.AccountSummary, snapshot.AccountListErrorCode);
                }
                else if (snapshot.Remotes.Count == 0)
                {
                    ShowDisplayResult(GitSettingsDisplayTarget.AccountSummary, "GitHubNoRemotes");
                }
                else
                {
                    ShowDisplayResult(GitSettingsDisplayTarget.AccountSummary, "GitHubAccountScopeHint");
                }
            }
            catch (OperationCanceledException)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    ShowDisplayResult(GitSettingsDisplayTarget.AccountSummary, "GitHubAccountLookupCanceled");
                }
            }
            catch (Exception exception)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    ShowDisplayError(GitSettingsDisplayTarget.AccountSummary, exception);
                }
            }
        }

        private GitHubRemoteAccountItem FindAccountRemote(string remoteName)
        {
            foreach (GitHubRemoteAccountItem item in _gitHubRemotes)
            {
                if (item.RemoteName == remoteName)
                {
                    return item;
                }
            }
            return null;
        }

        private void UpdateAccountAvailability()
        {
            foreach (GitHubRemoteAccountItem item in _gitHubRemotes)
            {
                item.CanChange = item.IsSupported && _accountLoginRunning == false;
                item.CanLogin = item.IsSupported && IsBusy == false && _remoteOperationBusy == false && _pendingAccountSelections == 0;
            }
        }

        private async Task TestGitAsync()
        {
            await _gitExecutablePresenter.TestAsync(this);
        }

        private async Task SaveGitPathAsync()
        {
            await _gitExecutablePresenter.SaveAsync(this);
        }

        internal void BeginGitPathAction()
        {
            IsBusy = true;
        }

        internal void ApplyGitPathAction(GitExecutableActionResult result)
        {
            if (result.Error != null)
            {
                if (result.Kind == GitExecutableActionKind.Verified)
                {
                    GitVersion = string.Empty;
                }

                GitPathResult = result;
                return;
            }

            if (result.Kind == GitExecutableActionKind.Saved)
            {
                GitPathInput = result.ConfiguredPath;
            }
            GitVersion = result.Version;
            GitPathResult = result;
        }

        internal void EndGitPathAction()
        {
            IsBusy = false;
        }

        private async Task SaveLocalAuthorAsync()
        {
            await SaveAuthorAsync(false, LocalName, LocalEmail);
        }

        private async Task SaveGlobalAuthorAsync()
        {
            await SaveAuthorAsync(true, GlobalName, GlobalEmail);
        }

        private async Task SaveAuthorAsync(bool global, string name, string email)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            string savedCode = "LocalAuthorSaved";
            if (global)
            {
                savedCode = "GlobalAuthorSaved";
            }
            string successMessage = _stringHelper.GetString(savedCode);
            string queueRoot = repository.RootPath;
            if (global)
            {
                queueRoot = _globalConfigQueueRoot;
            }
            try
            {
                await _operationQueue.EnqueueAsync(queueRoot, successMessage, async token =>
                {
                    if (IsCurrentRepository(repository))
                    {
                        IsBusy = true;
                    }
                    try
                    {
                        await _settingsService.SaveAuthorAsync(repository, global, name, email, token);
                        if (IsCurrentRepository(repository) == false)
                        {
                            return;
                        }
                        GitRepository binding = _repository;
                        int refreshVersion = _requestVersion + 1;
                        bool refreshed = await RefreshCoreAsync(false, savedCode);
                        if (ReferenceEquals(binding, _repository) == false)
                        {
                            return;
                        }
                        if (refreshVersion != _requestVersion)
                        {
                            return;
                        }
                        if (refreshed)
                        {
                            ShowDisplayResult(GitSettingsDisplayTarget.Status, savedCode);
                        }
                    }
                    finally
                    {
                        if (IsCurrentRepository(repository))
                        {
                            IsBusy = false;
                        }
                    }
                });
            }
            catch (Exception exception)
            {
                if (IsCurrentRepository(repository))
                {
                    ShowDisplayError(GitSettingsDisplayTarget.Status, exception);
                }
            }
        }

        private void ClearRepositoryValues()
        {
            LocalName = string.Empty;
            LocalEmail = string.Empty;
            GlobalName = string.Empty;
            GlobalEmail = string.Empty;
            _remotes.Clear();
            _gitHubRemotes.Clear();
            ShowDisplayResult(GitSettingsDisplayTarget.AccountSummary, "SelectRepositoryPrompt");
        }

        private bool CanRun()
        {
            return IsBusy == false;
        }

        private bool CanUseRepository()
        {
            return _repository != null && IsBusy == false;
        }

        private bool CanQueueRepositoryMutation()
        {
            return _repository != null;
        }

        private bool IsCurrentRepository(GitRepository repository)
        {
            if (_repository == null)
            {
                return false;
            }
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(_repository.RootPath, repository.RootPath, comparison);
        }
    }

}
