using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bough.App.Appearance;
using Bough.App.ViewModels.Models;
using Bough.App.ViewModels;
using Bough.App.Internals;
using Bough.Core.Internals;

namespace Bough.App.Views
{
    public partial class GitSettingsView : UserControl
    {
        private static readonly (string PropertyName, string Key)[] _labelKeys =
        {
            (nameof(GitSettingsViewModel.AppearanceHeading), "AppearanceHeading"),
            (nameof(GitSettingsViewModel.AppearanceDescription), "AppearanceDescription"),
            (nameof(GitSettingsViewModel.AppearanceLightLabel), "AppearanceLight"),
            (nameof(GitSettingsViewModel.AppearanceDarkLabel), "AppearanceDark"),
            (nameof(GitSettingsViewModel.AppearanceSystemLabel), "AppearanceSystem"),
            (nameof(GitSettingsViewModel.DefaultPullStrategyHeading), "DefaultPullStrategyHeading"),
            (nameof(GitSettingsViewModel.DefaultPullStrategyDescription), "DefaultPullStrategyDescription"),
            (nameof(GitSettingsViewModel.PullStrategyFastForwardOnlyLabel), "PullStrategyFastForwardOnly"),
            (nameof(GitSettingsViewModel.PullStrategyMergeLabel), "PullStrategyMerge"),
            (nameof(GitSettingsViewModel.PullStrategyRebaseLabel), "PullStrategyRebase"),
            (nameof(GitSettingsViewModel.SettingsTitle), "SettingsTitle"),
            (nameof(GitSettingsViewModel.GitSettingsHint), "GitSettingsHint"),
            (nameof(GitSettingsViewModel.RefreshLabel), "RefreshButton"),
            (nameof(GitSettingsViewModel.GitExecutableHeading), "GitExecutableHeading"),
            (nameof(GitSettingsViewModel.GitPathHint), "GitPathHint"),
            (nameof(GitSettingsViewModel.GitPathPlaceholder), "GitPathPlaceholder"),
            (nameof(GitSettingsViewModel.BrowseGitLabel), "BrowseGitLabel"),
            (nameof(GitSettingsViewModel.TestGitLabel), "TestGitLabel"),
            (nameof(GitSettingsViewModel.SaveGitPathLabel), "SaveGitPathLabel"),
            (nameof(GitSettingsViewModel.CommitAuthorHeading), "CommitAuthorHeading"),
            (nameof(GitSettingsViewModel.LocalAuthorHeading), "LocalAuthorHeading"),
            (nameof(GitSettingsViewModel.SaveLocalAuthorLabel), "SaveLocalAuthorLabel"),
            (nameof(GitSettingsViewModel.GlobalAuthorHeading), "GlobalAuthorHeading"),
            (nameof(GitSettingsViewModel.SaveGlobalAuthorLabel), "SaveGlobalAuthorLabel"),
            (nameof(GitSettingsViewModel.AccountsHeading), "GitHubAccountsHeading"),
            (nameof(GitSettingsViewModel.AccountsDescription), "GitHubAccountsDescription"),
            (nameof(GitSettingsViewModel.AccountsPurpose), "GitHubAccountsPurpose"),
            (nameof(GitSettingsViewModel.AccountsQueueHint), "GitHubAccountsQueueHint"),
            (nameof(GitSettingsViewModel.RemoteUrlsHeading), "RemoteUrlsHeading")
        };
        private GitSettingsViewModel _boundViewModel;
        private readonly HashSet<GitHubRemoteAccountItem> _boundAccounts = new();
        private bool _attached;

        public event Action InternalDialogOpening;
        public event Action InternalDialogClosed;

        public GitSettingsView()
        {
            InitializeComponent();
            DataContextChanged += GitSettingsDataContextChanged;
            AttachedToVisualTree += GitSettingsAttached;
            DetachedFromVisualTree += GitSettingsDetached;
        }

        private void GitSettingsDataContextChanged(object sender, EventArgs eventArgs)
        {
            if (_attached == false)
            {
                return;
            }

            BindGitPathResult();
        }

        private void GitSettingsAttached(object sender, VisualTreeAttachmentEventArgs eventArgs)
        {
            _attached = true;
            BindGitPathResult();
        }

        private void GitSettingsDetached(object sender, VisualTreeAttachmentEventArgs eventArgs)
        {
            _attached = false;
            UnbindGitPathResult();
        }

        private void BindGitPathResult()
        {
            GitSettingsViewModel viewModel = DataContext as GitSettingsViewModel;
            if (ReferenceEquals(_boundViewModel, viewModel))
            {
                return;
            }

            UnbindGitPathResult();
            _boundViewModel = viewModel;
            if (_boundViewModel == null)
            {
                return;
            }

            _boundViewModel.PropertyChanged += GitSettingsPropertyChanged;
            INotifyCollectionChanged accountChanges = _boundViewModel.GitHubRemotes;
            accountChanges.CollectionChanged += GitHubRemotesChanged;
            ShowFixedLabels(_boundViewModel);
            ShowAccountRepository(_boundViewModel);
            foreach (GitHubRemoteAccountItem item in _boundViewModel.GitHubRemotes)
            {
                BindAccountItem(_boundViewModel, item);
            }
            ShowGitPathResult(_boundViewModel);
            ShowDisplayResult(_boundViewModel);
            ShowAuthorStatus(_boundViewModel);
        }

        private void UnbindGitPathResult()
        {
            if (_boundViewModel == null)
            {
                return;
            }

            _boundViewModel.PropertyChanged -= GitSettingsPropertyChanged;
            INotifyCollectionChanged accountChanges = _boundViewModel.GitHubRemotes;
            accountChanges.CollectionChanged -= GitHubRemotesChanged;
            UnbindAccountItems();
            _boundViewModel = null;
        }

        private void GitHubRemotesChanged(object sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            if (_boundViewModel == null)
            {
                return;
            }

            if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
            {
                UnbindAccountItems();
                return;
            }
            if (eventArgs.OldItems != null)
            {
                foreach (GitHubRemoteAccountItem item in eventArgs.OldItems)
                {
                    item.PropertyChanged -= AccountItemPropertyChanged;
                    _boundAccounts.Remove(item);
                }
            }
            if (eventArgs.NewItems != null)
            {
                foreach (GitHubRemoteAccountItem item in eventArgs.NewItems)
                {
                    BindAccountItem(_boundViewModel, item);
                }
            }
        }

        private void BindAccountItem(GitSettingsViewModel viewModel, GitHubRemoteAccountItem item)
        {
            if (_boundAccounts.Add(item) == false)
            {
                return;
            }
            item.PropertyChanged += AccountItemPropertyChanged;
            ShowAccountItem(viewModel, item);
        }

        private void UnbindAccountItems()
        {
            foreach (GitHubRemoteAccountItem item in _boundAccounts)
            {
                item.PropertyChanged -= AccountItemPropertyChanged;
            }
            _boundAccounts.Clear();
        }

        private void AccountItemPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(GitHubRemoteAccountItem.CandidateName))
            {
                return;
            }
            if (_boundViewModel == null)
            {
                return;
            }
            if (sender is not GitHubRemoteAccountItem item)
            {
                return;
            }

            ShowAccountSelectionPreview(_boundViewModel, item);
        }

        private static void ShowAccountItem(GitSettingsViewModel viewModel, GitHubRemoteAccountItem item)
        {
            string fetchUrl = item.FetchUrl;
            if (fetchUrl.Length == 0)
            {
                fetchUrl = viewModel.Strings.GetString("GitHubRemoteUrlNeedsReview");
            }
            string pushUrl = item.PushUrl;
            if (pushUrl.Length == 0)
            {
                pushUrl = viewModel.Strings.GetString("GitHubRemoteUrlNeedsReview");
            }
            item.FetchUrlText = viewModel.Strings.Format("GitHubFetchUrlLabel", fetchUrl);
            item.PushUrlText = viewModel.Strings.Format("GitHubPushUrlLabel", pushUrl);
            item.UnavailableReason = string.Empty;
            if (item.UnavailableReasonCode.Length > 0)
            {
                item.UnavailableReason = viewModel.Strings.GetString(item.UnavailableReasonCode);
            }
            if (item.SelectedUserName.Length == 0)
            {
                item.SelectedAccountText = viewModel.Strings.GetString("GitHubNoSelectedAccount");
            }
            else if (item.IsRepositorySelection)
            {
                item.SelectedAccountText = viewModel.Strings.Format("GitHubSelectedAccount", item.SelectedUserName);
            }
            else
            {
                item.SelectedAccountText = viewModel.Strings.Format("GitHubInheritedAccount", item.SelectedUserName);
            }
            item.ExistingAccountPlaceholder = viewModel.Strings.GetString("GitHubExistingAccountPlaceholder");
            item.UserNamePlaceholder = viewModel.Strings.GetString("GitHubPhotoUserName");
            item.RemoteUserNameAutomationName = viewModel.Strings.GetString("GitHubRemoteUserNameAutomationName");
            item.SelectAccountLabel = viewModel.Strings.GetString("GitHubSelectAccountLabel");
            item.AddAccountLabel = viewModel.Strings.GetString("GitHubAddAccountLabel");
            ShowAccountSelectionPreview(viewModel, item);
        }

        private static void ShowAccountSelectionPreview(GitSettingsViewModel viewModel, GitHubRemoteAccountItem item)
        {
            if (string.IsNullOrWhiteSpace(item.CandidateName))
            {
                item.SelectionPreviewText = viewModel.Strings.GetString("GitHubUserNameRequired");
                return;
            }

            item.SelectionPreviewText = viewModel.Strings.Format("GitHubAccountSelectionPreview", item.RemoteName, item.CandidateName.Trim());
        }

        private void GitSettingsPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(GitSettingsViewModel.CurrentRepository))
            {
                if (_boundViewModel != null)
                {
                    ShowAccountRepository(_boundViewModel);
                }
            }
            switch (eventArgs.PropertyName)
            {
                case nameof(GitSettingsViewModel.CurrentRepository):
                case nameof(GitSettingsViewModel.LocalName):
                case nameof(GitSettingsViewModel.LocalEmail):
                case nameof(GitSettingsViewModel.GlobalName):
                case nameof(GitSettingsViewModel.GlobalEmail):
                    if (_boundViewModel != null)
                    {
                        ShowAuthorStatus(_boundViewModel);
                    }
                    return;
            }
            if (eventArgs.PropertyName == nameof(GitSettingsViewModel.DisplayResult))
            {
                if (_boundViewModel != null)
                {
                    ShowDisplayResult(_boundViewModel);
                }
                return;
            }
            if (eventArgs.PropertyName != nameof(GitSettingsViewModel.GitPathResult))
            {
                return;
            }
            if (_boundViewModel == null)
            {
                return;
            }

            ShowGitPathResult(_boundViewModel);
        }

        private static void ShowFixedLabels(GitSettingsViewModel viewModel)
        {
            Dictionary<string, string> labels = new(_labelKeys.Length);
            foreach ((string propertyName, string key) in _labelKeys)
            {
                labels.Add(propertyName, viewModel.Strings.GetString(key));
            }
            viewModel.SetDisplayLabels(labels);
        }

        private static void ShowAccountRepository(GitSettingsViewModel viewModel)
        {
            if (viewModel.CurrentRepository == null)
            {
                viewModel.SetAccountRepositoryText(viewModel.Strings.GetString("SelectRepositoryPrompt"));
                return;
            }
            viewModel.SetAccountRepositoryText(viewModel.Strings.Format("CurrentRepositoryDetails", viewModel.CurrentRepository.DisplayName, viewModel.CurrentRepository.RootPath));
        }

        private static void ShowGitPathResult(GitSettingsViewModel viewModel)
        {
            GitExecutableActionResult result = viewModel.GitPathResult;
            if (result == null)
            {
                return;
            }

            string message;
            if (result.Error != null)
            {
                message = viewModel.Errors.GetDisplayMessage(result.Error);
            }
            else if (result.Kind == GitExecutableActionKind.Saved)
            {
                message = viewModel.Strings.Format("GitPathSaved", result.Version);
            }
            else
            {
                message = viewModel.Strings.Format("GitPathVerified", result.Version);
            }

            viewModel.SetGitPathStatusMessage(result, message);
        }

        private static void ShowDisplayResult(GitSettingsViewModel viewModel)
        {
            GitSettingsDisplayResult result = viewModel.DisplayResult;
            if (result == null)
            {
                return;
            }

            string message = string.Empty;
            if (result.Error != null)
            {
                message = viewModel.Errors.GetDisplayMessage(result.Error);
            }
            else if (string.IsNullOrEmpty(result.Code) == false)
            {
                if (result.Code == "DefaultPullStrategySaved")
                {
                    GitPullStrategy strategy = (GitPullStrategy)result.Arguments[0];
                    string labelCode = "PullStrategyFastForwardOnly";
                    if (strategy == GitPullStrategy.Merge)
                    {
                        labelCode = "PullStrategyMerge";
                    }
                    else if (strategy == GitPullStrategy.Rebase)
                    {
                        labelCode = "PullStrategyRebase";
                    }
                    message = viewModel.Strings.Format(result.Code, viewModel.Strings.GetString(labelCode));
                }
                else
                {
                    message = viewModel.Strings.Format(result.Code, result.Arguments);
                }
            }

            viewModel.SetDisplayMessage(result, message);
        }

        private static void ShowAuthorStatus(GitSettingsViewModel viewModel)
        {
            if (viewModel.CurrentRepository == null)
            {
                viewModel.SetAuthorStatusText(viewModel.Strings.GetString("SelectRepositoryPrompt"));
                return;
            }

            string name = viewModel.LocalName;
            if (string.IsNullOrWhiteSpace(name) == true)
            {
                name = viewModel.GlobalName;
            }
            string email = viewModel.LocalEmail;
            if (string.IsNullOrWhiteSpace(email) == true)
            {
                email = viewModel.GlobalEmail;
            }
            if (string.IsNullOrWhiteSpace(name) == true)
            {
                viewModel.SetAuthorStatusText(viewModel.Strings.GetString("GitAuthorNameMissing"));
                return;
            }
            if (string.IsNullOrWhiteSpace(email) == true)
            {
                viewModel.SetAuthorStatusText(viewModel.Strings.GetString("GitAuthorEmailMissing"));
                return;
            }

            viewModel.SetAuthorStatusText(viewModel.Strings.Format("GitAuthorSummary", name, email));
        }

        private async void LightAppearanceClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }
            await viewModel.SelectAppearanceAsync(AppearanceThemeMode.Light);
        }

        private async void DarkAppearanceClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }
            await viewModel.SelectAppearanceAsync(AppearanceThemeMode.Dark);
        }

        private async void SystemAppearanceClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }
            await viewModel.SelectAppearanceAsync(AppearanceThemeMode.System);
        }

        private void FastForwardOnlyPullClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }

            viewModel.SelectDefaultPullStrategy(GitPullStrategy.FastForwardOnly);
        }

        private void MergePullClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }

            viewModel.SelectDefaultPullStrategy(GitPullStrategy.Merge);
        }

        private void RebasePullClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }

            viewModel.SelectDefaultPullStrategy(GitPullStrategy.Rebase);
        }

        private async void ApplyGitHubAccountClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (button.DataContext is not GitHubRemoteAccountItem remote)
            {
                return;
            }
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }
            string userName = remote.CandidateName?.Trim() ?? string.Empty;
            if (userName.Length == 0)
            {
                return;
            }
            await viewModel.SelectAccountAsync(remote, userName);
        }

        private async void LoginGitHubAccountClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (button.DataContext is not GitHubRemoteAccountItem remote)
            {
                return;
            }
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }
            InternalDialogOpening?.Invoke();
            try
            {
                await viewModel.LoginAccountAsync(remote);
            }
            finally
            {
                InternalDialogClosed?.Invoke();
            }
        }

        private async void BrowseGitClicked(object sender, RoutedEventArgs eventArgs)
        {
            TopLevel topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }
            if (DataContext is not GitSettingsViewModel viewModel)
            {
                return;
            }

            FilePickerOpenOptions options = new() { Title = viewModel.Strings.GetString("GitExecutablePickerTitle"), AllowMultiple = false };
            InternalDialogOpening?.Invoke();
            IReadOnlyList<IStorageFile> files;
            try
            {
                files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            }
            finally
            {
                InternalDialogClosed?.Invoke();
            }
            if (files.Count == 0)
            {
                return;
            }

            string path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) == false)
            {
                viewModel.GitPathInput = path;
            }
        }
    }
}
