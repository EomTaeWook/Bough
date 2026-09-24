using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bough.App.Appearance;
using Bough.App.ViewModels;

namespace Bough.App.Views
{
    public partial class GitSettingsView : UserControl
    {
        public event Action InternalDialogOpening;
        public event Action InternalDialogClosed;

        public GitSettingsView()
        {
            InitializeComponent();
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

            FilePickerOpenOptions options = new() { Title = viewModel.GitExecutablePickerTitle, AllowMultiple = false };
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
