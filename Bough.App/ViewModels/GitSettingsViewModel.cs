using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Controls;
using Bough.App.Appearance;
using Bough.App.Localization;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.App.Internals;

namespace Bough.App.ViewModels
{
    public class GitSettingsViewModel : ViewModelBase
    {
        private static readonly string _globalConfigQueueRoot = Path.Combine(Path.GetTempPath(), "Bough", "global-git-config-queue");
        private readonly GitSettingsService _settingsService;
        private readonly GitHubAccountService _gitHubAccountService;
        private readonly GitHubAuthorLinkSettings _gitHubAuthorLinkSettings;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly AppearanceThemeService _appearanceTheme;
        private readonly GitOperationQueue _operationQueue;
        private readonly ObservableCollection<GitRemote> _remotes;
        private readonly ObservableCollection<GitHubRemoteAccountItem> _gitHubRemotes;
        private GitRepository _repository;
        private string _gitPathInput;
        private string _gitVersion;
        private string _localName;
        private string _localEmail;
        private string _globalName;
        private string _globalEmail;
        private string _credentialHelper;
        private string _authorStatus;
        private string _statusMessage;
        private string _photoGitHubUserName;
        private string _photoAuthorEmail;
        private string _photoLinkStatus;
        private string _accountStatusText = string.Empty;
        private string _appearanceStatus = string.Empty;
        private bool _remoteOperationBusy;
        private bool _accountLoginRunning;
        private int _pendingAccountSelections;
        private CancellationTokenSource _accountCancellation;
        private bool _isBusy;
        private int _requestVersion;

        public GitSettingsViewModel(GitSettingsService settingsService, GitHubAccountService gitHubAccountService, StringHelper stringHelper, AppearanceThemeService appearanceTheme, GitErrorLocalizer errorLocalizer, GitOperationQueue operationQueue)
        {
            _settingsService = settingsService;
            _gitHubAccountService = gitHubAccountService;
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            _appearanceTheme = appearanceTheme;
            _operationQueue = operationQueue;
            _appearanceTheme.ThemeChanged += OnAppearanceThemeChanged;
            _gitHubAuthorLinkSettings = new GitHubAuthorLinkSettings();
            GitHubAuthorLink photoLink = _gitHubAuthorLinkSettings.Load();
            _photoGitHubUserName = photoLink.UserName;
            _photoAuthorEmail = photoLink.AuthorEmail;
            _photoLinkStatus = string.Empty;
            _gitPathInput = settingsService.ConfiguredGitPath;
            _gitVersion = string.Empty;
            _localName = string.Empty;
            _localEmail = string.Empty;
            _globalName = string.Empty;
            _globalEmail = string.Empty;
            _credentialHelper = _stringHelper.GetString("NotConfigured");
            _authorStatus = _stringHelper.GetString("SelectRepositoryPrompt");
            _statusMessage = _stringHelper.GetString("GitSettingsHint");
            _remotes = [];
            _gitHubRemotes = [];
            Remotes = new ReadOnlyObservableCollection<GitRemote>(_remotes);
            GitHubRemotes = new ReadOnlyObservableCollection<GitHubRemoteAccountItem>(_gitHubRemotes);
            TestGitCommand = new AsyncRelayCommand(TestGitAsync, CanRun);
            SaveGitPathCommand = new AsyncRelayCommand(SaveGitPathAsync, CanRun);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanUseRepository);
            SaveLocalAuthorCommand = new QueuedAsyncRelayCommand(SaveLocalAuthorAsync, CanQueueRepositoryMutation, exception => StatusMessage = _errorLocalizer.GetDisplayMessage(exception));
            SaveGlobalAuthorCommand = new QueuedAsyncRelayCommand(SaveGlobalAuthorAsync, CanQueueRepositoryMutation, exception => StatusMessage = _errorLocalizer.GetDisplayMessage(exception));
            SavePhotoLinkCommand = new RelayCommand(SavePhotoLink, CanRun);
        }

        public ReadOnlyObservableCollection<GitRemote> Remotes { get; }
        public GitRepository CurrentRepository { get { return _repository; } }
        public string AccountRepositoryText
        {
            get
            {
                if (_repository == null)
                {
                    return _stringHelper.GetString("SelectRepositoryPrompt");
                }
                return _stringHelper.Format("CurrentRepositoryDetails", _repository.DisplayName, _repository.RootPath);
            }
        }
        public ReadOnlyObservableCollection<GitHubRemoteAccountItem> GitHubRemotes { get; }
        public string AccountStatusText { get { return _accountStatusText; } private set { SetProperty(ref _accountStatusText, value); } }
        public string AppearanceHeading { get { return _stringHelper.GetString("AppearanceHeading"); } }
        public string AppearanceDescription { get { return _stringHelper.GetString("AppearanceDescription"); } }
        public string AppearanceLightLabel { get { return _stringHelper.GetString("AppearanceLight"); } }
        public string AppearanceDarkLabel { get { return _stringHelper.GetString("AppearanceDark"); } }
        public string AppearanceSystemLabel { get { return _stringHelper.GetString("AppearanceSystem"); } }
        public string GitExecutablePickerTitle { get { return _stringHelper.GetString("GitExecutablePickerTitle"); } }
        public string SettingsTitle { get { return _stringHelper.GetString("SettingsTitle"); } }
        public string RefreshLabel { get { return _stringHelper.GetString("RefreshButton"); } }
        public string GitExecutableHeading { get { return _stringHelper.GetString("GitExecutableHeading"); } }
        public string GitPathHint { get { return _stringHelper.GetString("GitPathHint"); } }
        public string GitPathPlaceholder { get { return _stringHelper.GetString("GitPathPlaceholder"); } }
        public string BrowseGitLabel { get { return _stringHelper.GetString("BrowseGitLabel"); } }
        public string TestGitLabel { get { return _stringHelper.GetString("TestGitLabel"); } }
        public string SaveGitPathLabel { get { return _stringHelper.GetString("SaveGitPathLabel"); } }
        public string CommitAuthorHeading { get { return _stringHelper.GetString("CommitAuthorHeading"); } }
        public string LocalAuthorHeading { get { return _stringHelper.GetString("LocalAuthorHeading"); } }
        public string SaveLocalAuthorLabel { get { return _stringHelper.GetString("SaveLocalAuthorLabel"); } }
        public string GlobalAuthorHeading { get { return _stringHelper.GetString("GlobalAuthorHeading"); } }
        public string SaveGlobalAuthorLabel { get { return _stringHelper.GetString("SaveGlobalAuthorLabel"); } }
        public string AccountsHeading { get { return _stringHelper.GetString("GitHubAccountsHeading"); } }
        public string AccountsDescription { get { return _stringHelper.GetString("GitHubAccountsDescription"); } }
        public string AccountsPurpose { get { return _stringHelper.GetString("GitHubAccountsPurpose"); } }
        public string AccountsQueueHint { get { return _stringHelper.GetString("GitHubAccountsQueueHint"); } }
        public string CredentialHelperHeading { get { return _stringHelper.GetString("CredentialHelperHeading"); } }
        public string RemoteUrlsHeading { get { return _stringHelper.GetString("RemoteUrlsHeading"); } }
        public string AppearanceStatus { get { return _appearanceStatus; } private set { SetProperty(ref _appearanceStatus, value); } }
        public bool IsLightAppearance { get { return _appearanceTheme.SelectedMode == AppearanceThemeMode.Light; } }
        public bool IsDarkAppearance { get { return _appearanceTheme.SelectedMode == AppearanceThemeMode.Dark; } }
        public bool IsSystemAppearance { get { return _appearanceTheme.SelectedMode == AppearanceThemeMode.System; } }
        public bool IsRemoteOperationBusy { get { return _remoteOperationBusy; } }

        public async Task SelectAppearanceAsync(AppearanceThemeMode mode)
        {
            try
            {
                await _appearanceTheme.SetThemeAsync(mode);
                AppearanceStatus = string.Empty;
            }
            catch (Exception exception)
            {
                AppearanceStatus = _errorLocalizer.GetDisplayMessage(exception);
            }
        }

        private void OnAppearanceThemeChanged(AppearanceThemeMode mode)
        {
            OnPropertyChanged(nameof(IsLightAppearance));
            OnPropertyChanged(nameof(IsDarkAppearance));
            OnPropertyChanged(nameof(IsSystemAppearance));
        }
        public AsyncRelayCommand TestGitCommand { get; }
        public AsyncRelayCommand SaveGitPathCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public QueuedAsyncRelayCommand SaveLocalAuthorCommand { get; }
        public QueuedAsyncRelayCommand SaveGlobalAuthorCommand { get; }
        public RelayCommand SavePhotoLinkCommand { get; }

        public string PhotoLinkHeading { get { return _stringHelper.GetString("GitHubPhotoLinkHeading"); } }
        public string PhotoLinkDescription { get { return _stringHelper.GetString("GitHubPhotoLinkDescription"); } }
        public string PhotoGitHubUserNameLabel { get { return _stringHelper.GetString("GitHubPhotoUserName"); } }
        public string PhotoAuthorEmailLabel { get { return _stringHelper.GetString("GitHubPhotoAuthorEmail"); } }
        public string PhotoLinkSaveLabel { get { return _stringHelper.GetString("GitHubPhotoSave"); } }
        public string PhotoLinkClearHelp { get { return _stringHelper.GetString("GitHubPhotoClearHelp"); } }
        public string PhotoGitHubUserName { get { return _photoGitHubUserName; } set { SetProperty(ref _photoGitHubUserName, value); } }
        public string PhotoAuthorEmail { get { return _photoAuthorEmail; } set { SetProperty(ref _photoAuthorEmail, value); } }
        public string PhotoLinkStatus { get { return _photoLinkStatus; } private set { SetProperty(ref _photoLinkStatus, value); } }

        public string GitPathInput
        {
            get { return _gitPathInput; }
            set
            {
                if (SetProperty(ref _gitPathInput, value) == false)
                {
                    return;
                }

                GitVersion = string.Empty;
                StatusMessage = _stringHelper.GetString("GitSettingsHint");
            }
        }
        public string GitVersion { get { return _gitVersion; } private set { SetProperty(ref _gitVersion, value); } }
        public string LocalName { get { return _localName; } set { SetProperty(ref _localName, value); } }
        public string LocalEmail { get { return _localEmail; } set { SetProperty(ref _localEmail, value); } }
        public string GlobalName { get { return _globalName; } set { SetProperty(ref _globalName, value); } }
        public string GlobalEmail { get { return _globalEmail; } set { SetProperty(ref _globalEmail, value); } }
        public string CredentialHelper { get { return _credentialHelper; } private set { SetProperty(ref _credentialHelper, value); } }
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
                    SavePhotoLinkCommand.NotifyCanExecuteChanged();
                    UpdateAccountAvailability();
                }
            }
        }

        public async Task SetRepositoryAsync(GitRepository repository)
        {
            _requestVersion++;
            _accountCancellation?.Cancel();
            _repository = repository;
            SaveLocalAuthorCommand.NotifyCanExecuteChanged();
            SaveGlobalAuthorCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AccountRepositoryText));
            ClearRepositoryValues();
            if (repository == null)
            {
                IsBusy = false;
                StatusMessage = _stringHelper.GetString("SelectRepositoryPrompt");
                return;
            }

            await RefreshCoreAsync();
        }

        public async Task RefreshAsync()
        {
            await RefreshCoreAsync();
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
                item.StatusText = item.UnavailableReason;
                return;
            }
            string selectedName = userName?.Trim() ?? string.Empty;
            if (selectedName.Length == 0)
            {
                item.StatusText = _stringHelper.GetString("GitHubUserNameRequired");
                return;
            }
            string remoteName = item.RemoteName;
            string successMessage = _stringHelper.Format("GitHubAccountSelected", remoteName, selectedName);
            item.StatusText = _stringHelper.GetString("GcmOperationInProgress");
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
                            refreshed.StatusText = successMessage;
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
                item.StatusText = _errorLocalizer.GetDisplayMessage(exception);
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
                _stringHelper.GetString("GcmLoginCompleted"));
        }

        private async Task RunAccountActionAsync(GitHubRemoteAccountItem item, string candidateName,
            Func<GitRepository, CancellationToken, Task> action, string successMessage)
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
            item.StatusText = _stringHelper.GetString("GcmOperationInProgress");
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
                    refreshed.StatusText = successMessage;
                }
            }
            catch (OperationCanceledException)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    item.StatusText = _stringHelper.GetString("GitHubAccountActionCanceled");
                }
            }
            catch (Exception exception)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    item.StatusText = _errorLocalizer.GetDisplayMessage(exception);
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

        private async Task<bool> RefreshCoreAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return false;
            }

            int request = ++_requestVersion;
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

                LocalName = snapshot.LocalName;
                LocalEmail = snapshot.LocalEmail;
                GlobalName = snapshot.GlobalName;
                GlobalEmail = snapshot.GlobalEmail;
                string credentialHelper = snapshot.CredentialHelper;
                if (credentialHelper.Length == 0)
                {
                    credentialHelper = _stringHelper.GetString("NotConfigured");
                }
                CredentialHelper = credentialHelper;
                _remotes.Clear();
                foreach (GitRemote remote in snapshot.Remotes) { _remotes.Add(remote); }
                AuthorStatus = GetAuthorStatus(snapshot);
                StatusMessage = _stringHelper.GetString("GitSettingsRefreshed");
                IsBusy = false;
                accountRefresh = RefreshAccountsInBackgroundAsync(repository, request, cancellation);
                return true;
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
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
                AccountStatusText = _errorLocalizer.GetDisplayMessage(exception);
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
            AccountStatusText = _stringHelper.GetString("GitHubAccountsLoading");
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
                    GitHubRemoteAccountItem item = new(remote, snapshot.KnownAccounts, sharedUrl, _stringHelper);
                    if (remote.RemoteName == preserveRemote && preserveCandidate.Length > 0)
                    {
                        item.CandidateName = preserveCandidate;
                    }
                    _gitHubRemotes.Add(item);
                }
                UpdateAccountAvailability();
                if (snapshot.IsGcmAvailable == false)
                {
                    AccountStatusText = _stringHelper.GetString(snapshot.AvailabilityMessageCode);
                }
                else if (snapshot.AccountListErrorCode.Length > 0)
                {
                    AccountStatusText = _stringHelper.GetString(snapshot.AccountListErrorCode);
                }
                else if (snapshot.Remotes.Count == 0)
                {
                    AccountStatusText = _stringHelper.GetString("GitHubNoRemotes");
                }
                else
                {
                    AccountStatusText = _stringHelper.GetString("GitHubAccountScopeHint");
                }
            }
            catch (OperationCanceledException)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    AccountStatusText = _stringHelper.GetString("GitHubAccountLookupCanceled");
                }
            }
            catch (Exception exception)
            {
                if (request == _requestVersion && _repository == repository)
                {
                    AccountStatusText = _errorLocalizer.GetDisplayMessage(exception);
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
            string candidatePath = GitPathInput;
            IsBusy = true;
            try
            {
                string version = await _settingsService.TestGitAsync(candidatePath);
                if (GitPathInput != candidatePath)
                {
                    return;
                }

                GitVersion = version;
                StatusMessage = _stringHelper.Format("GitPathVerified", version);
            }
            catch (Exception exception)
            {
                if (GitPathInput != candidatePath)
                {
                    return;
                }

                GitVersion = string.Empty;
                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveGitPathAsync()
        {
            string candidatePath = GitPathInput;
            IsBusy = true;
            try
            {
                string version = await _settingsService.SaveGitPathAsync(candidatePath);
                if (GitPathInput != candidatePath)
                {
                    return;
                }

                GitPathInput = _settingsService.ConfiguredGitPath;
                GitVersion = version;
                StatusMessage = _stringHelper.Format("GitPathSaved", version);
            }
            catch (Exception exception)
            {
                if (GitPathInput != candidatePath)
                {
                    return;
                }

                StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveLocalAuthorAsync()
        {
            await SaveAuthorAsync(false, LocalName, LocalEmail);
        }

        private void SavePhotoLink()
        {
            if (IsBusy)
            {
                return;
            }
            try
            {
                _gitHubAuthorLinkSettings.Save(PhotoAuthorEmail, PhotoGitHubUserName);
                if (string.IsNullOrWhiteSpace(PhotoAuthorEmail))
                {
                    PhotoLinkStatus = _stringHelper.GetString("GitHubPhotoCleared");
                }
                else
                {
                    PhotoLinkStatus = _stringHelper.GetString("GitHubPhotoSaved");
                }
            }
            catch (Exception exception)
            {
                PhotoLinkStatus = _errorLocalizer.GetDisplayMessage(exception);
            }
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
            string successMessage = _stringHelper.GetString(global ? "GlobalAuthorSaved" : "LocalAuthorSaved");
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
                        bool refreshed = await RefreshCoreAsync();
                        if (refreshed)
                        {
                            StatusMessage = successMessage;
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
                    StatusMessage = _errorLocalizer.GetDisplayMessage(exception);
                }
            }
        }

        private void ClearRepositoryValues()
        {
            LocalName = string.Empty;
            LocalEmail = string.Empty;
            GlobalName = string.Empty;
            GlobalEmail = string.Empty;
            CredentialHelper = _stringHelper.GetString("NotConfigured");
            AuthorStatus = _stringHelper.GetString("SelectRepositoryPrompt");
            _remotes.Clear();
            _gitHubRemotes.Clear();
            AccountStatusText = _stringHelper.GetString("SelectRepositoryPrompt");
        }

        private string GetAuthorStatus(GitSettingsSnapshot snapshot)
        {
            string name = snapshot.LocalName;
            if (string.IsNullOrWhiteSpace(name) == true)
            {
                name = snapshot.GlobalName;
            }
            string email = snapshot.LocalEmail;
            if (string.IsNullOrWhiteSpace(email) == true)
            {
                email = snapshot.GlobalEmail;
            }
            if (string.IsNullOrWhiteSpace(name) == true)
            {
                return _stringHelper.GetString("GitAuthorNameMissing");
            }
            if (string.IsNullOrWhiteSpace(email) == true)
            {
                return _stringHelper.GetString("GitAuthorEmailMissing");
            }

            return _stringHelper.Format("GitAuthorSummary", name, email);
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

    public class GitHubRemoteAccountItem : ViewModelBase
    {
        private readonly StringHelper _stringHelper;
        private string _candidateName;
        private string _statusText = string.Empty;
        private bool _canChange;
        private bool _canLogin;

        public GitHubRemoteAccountItem(GitHubRemoteAccount remote, IReadOnlyList<string> knownAccounts, bool sharedUrl, StringHelper stringHelper)
        {
            _stringHelper = stringHelper;
            RemoteName = remote.RemoteName;
            string fetchUrl = remote.FetchUrl;
            if (fetchUrl.Length == 0)
            {
                fetchUrl = _stringHelper.GetString("GitHubRemoteUrlNeedsReview");
            }
            string pushUrl = remote.PushUrl;
            if (pushUrl.Length == 0)
            {
                pushUrl = _stringHelper.GetString("GitHubRemoteUrlNeedsReview");
            }
            FetchUrlText = _stringHelper.Format("GitHubFetchUrlLabel", fetchUrl);
            PushUrlText = _stringHelper.Format("GitHubPushUrlLabel", pushUrl);
            SelectedUserName = remote.SelectedUserName;
            IsSupported = remote.CanSelect;
            UnavailableReason = string.Empty;
            if (remote.UnavailableReasonCode.Length > 0)
            {
                UnavailableReason = _stringHelper.GetString(remote.UnavailableReasonCode);
            }
            if (sharedUrl)
            {
                IsSupported = false;
                UnavailableReason = _stringHelper.GetString("GitHubSharedRemoteUrlUnsupported");
            }
            KnownAccounts = knownAccounts;
            _candidateName = remote.SelectedUserName;
            if (remote.SelectedUserName.Length == 0)
            {
                SelectedAccountText = _stringHelper.GetString("GitHubNoSelectedAccount");
            }
            else if (remote.IsRepositorySelection)
            {
                SelectedAccountText = _stringHelper.Format("GitHubSelectedAccount", remote.SelectedUserName);
            }
            else
            {
                SelectedAccountText = _stringHelper.Format("GitHubInheritedAccount", remote.SelectedUserName);
            }
        }

        public string RemoteName { get; }
        public string FetchUrlText { get; }
        public string PushUrlText { get; }
        public string SelectedUserName { get; }
        public string SelectedAccountText { get; }
        public string ExistingAccountPlaceholder { get { return _stringHelper.GetString("GitHubExistingAccountPlaceholder"); } }
        public string UserNamePlaceholder { get { return _stringHelper.GetString("GitHubPhotoUserName"); } }
        public string RemoteUserNameAutomationName { get { return _stringHelper.GetString("GitHubRemoteUserNameAutomationName"); } }
        public string SelectAccountLabel { get { return _stringHelper.GetString("GitHubSelectAccountLabel"); } }
        public string AddAccountLabel { get { return _stringHelper.GetString("GitHubAddAccountLabel"); } }
        public bool IsSupported { get; }
        public string UnavailableReason { get; }
        public bool HasUnavailableReason { get { return UnavailableReason.Length > 0; } }
        public IReadOnlyList<string> KnownAccounts { get; }
        public string CandidateName
        {
            get { return _candidateName; }
            set
            {
                if (SetProperty(ref _candidateName, value))
                {
                    OnPropertyChanged(nameof(CanApply));
                    OnPropertyChanged(nameof(SelectionPreviewText));
                }
            }
        }
        public string SelectionPreviewText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CandidateName))
                {
                    return _stringHelper.GetString("GitHubUserNameRequired");
                }
                return _stringHelper.Format("GitHubAccountSelectionPreview", RemoteName, CandidateName.Trim());
            }
        }
        public string StatusText { get { return _statusText; } set { SetProperty(ref _statusText, value); } }
        public bool CanChange
        {
            get { return _canChange; }
            set
            {
                if (SetProperty(ref _canChange, value))
                {
                    OnPropertyChanged(nameof(CanApply));
                }
            }
        }
        public bool CanLogin
        {
            get { return _canLogin; }
            set { SetProperty(ref _canLogin, value); }
        }
        public bool CanApply { get { return CanChange && string.IsNullOrWhiteSpace(CandidateName) == false; } }
    }
}
