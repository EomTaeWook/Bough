using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Views
{
    public partial class IgnoreFilesWindow : Window
    {
        private CancellationToken _cancellationToken;
        private readonly LocalChangesViewModel _viewModel;
        private readonly Func<string, CancellationToken, Task<GitIgnorePlan>> _preparePlan;
        private CancellationTokenSource _preparationCancellation;
        private Task _preparationTask = Task.CompletedTask;
        private GitIgnorePlan _currentPlan;
        private string _preparedInput = string.Empty;
        private LocalizedText _preparationError;
        private bool _preparing;
        private bool _closed;

        public GitIgnorePlan ConfirmedPlan { get; private set; }

        public IgnoreFilesWindow()
        {
            InitializeComponent();
            Closed += WindowClosed;
        }

        public IgnoreFilesWindow(GitIgnorePlan plan, string title, string description, string confirmText, string cancelText)
            : this()
        {
            ArgumentNullException.ThrowIfNull(plan);
            _currentPlan = plan;
            Title = title;
            Heading.Text = title;
            Description.Text = description;
            FilesList.ItemsSource = plan.Entries;
            ConfirmButton.Content = confirmText;
            CancelButton.Content = cancelText;
            ConfirmButton.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, confirmText);
            CancelButton.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, cancelText);
            Opened += OnOpened;
        }

        public IgnoreFilesWindow(LocalChangesViewModel viewModel, GitIgnorePlan plan, Func<string, CancellationToken, Task<GitIgnorePlan>> preparePlan)
            : this(plan, viewModel.IgnoreConfirmTitleText, viewModel.GetIgnoreDescription(plan), viewModel.IgnoreConfirmButtonText, viewModel.DiscardCancelText)
        {
            ArgumentNullException.ThrowIfNull(preparePlan);
            if (plan.DirectoryPath == null)
            {
                if (plan.Entries.Count == 1)
                {
                    string selectedFilePath = plan.Entries[0].Path;
                    _preparedInput = selectedFilePath;
                    DirectoryPathInput.Text = selectedFilePath;
                }
            }
            _viewModel = viewModel;
            _preparePlan = preparePlan;
            DirectoryInputPanel.IsVisible = true;
            LanguageChangeBinding.Bind(this, () => _viewModel.Strings, RefreshDisplay);
            RefreshDisplay();
        }

        public async Task<bool> ShowForAsync(Window owner, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            if (cancellationToken.IsCancellationRequested == true)
            {
                return false;
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible == true)
                {
                    Close(false);
                }
            }));
            bool accepted = await ShowDialog<bool>(owner);
            await _preparationTask;
            return accepted;
        }

        private void RefreshDisplay()
        {
            if (_closed == true)
            {
                return;
            }
            if (_viewModel == null)
            {
                return;
            }
            StringHelper strings = _viewModel.Strings;
            Title = _viewModel.IgnoreConfirmTitleText;
            Heading.Text = Title;
            DirectoryPathLabel.Text = strings.GetString("IgnoreDirectoryPathLabel");
            DirectoryPathHint.Text = strings.GetString("IgnoreDirectoryPathHint");
            DirectoryPathInput.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, DirectoryPathLabel.Text);
            ConfirmButton.Content = _viewModel.IgnoreConfirmButtonText;
            CancelButton.Content = _viewModel.DiscardCancelText;
            ConfirmButton.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, _viewModel.IgnoreConfirmButtonText);
            CancelButton.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, _viewModel.DiscardCancelText);
            PreparationText.Text = strings.GetString("IgnoreDirectoryPreparing");
            PreparationText.IsVisible = _preparing;
            ErrorText.Text = _preparationError?.GetText(strings) ?? string.Empty;
            ErrorText.IsVisible = _preparationError != null;
            bool canConfirm = _currentPlan != null;
            if (_preparing == true)
            {
                canConfirm = false;
            }
            ConfirmButton.IsEnabled = canConfirm;
            if (_currentPlan == null)
            {
                Description.Text = string.Empty;
                FilesList.ItemsSource = Array.Empty<GitIgnoreEntry>();
                return;
            }
            Description.Text = _viewModel.GetIgnoreDescription(_currentPlan);
            FilesList.ItemsSource = _currentPlan.Entries;
        }

        private async void DirectoryPathChanged(object sender, TextChangedEventArgs eventArgs)
        {
            if (_preparePlan == null)
            {
                return;
            }
            if (_closed == true)
            {
                return;
            }
            if ((DirectoryPathInput.Text ?? string.Empty) == _preparedInput)
            {
                return;
            }
            _preparationCancellation?.Cancel();
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
            _preparationCancellation = cancellation;
            string directoryPath = DirectoryPathInput.Text ?? string.Empty;
            _preparedInput = null;
            _currentPlan = null;
            ConfirmedPlan = null;
            _preparationError = null;
            _preparing = true;
            RefreshDisplay();
            _preparationTask = PreparePlanAsync(directoryPath, cancellation);
            await _preparationTask;
        }

        private async Task PreparePlanAsync(string directoryPath, CancellationTokenSource cancellation)
        {
            try
            {
                GitIgnorePlan plan = await _preparePlan(directoryPath, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_closed == true)
                {
                    return;
                }
                if (ReferenceEquals(_preparationCancellation, cancellation) == false)
                {
                    return;
                }
                _currentPlan = plan;
                _preparedInput = directoryPath;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (_closed == true)
                {
                    return;
                }
                if (ReferenceEquals(_preparationCancellation, cancellation) == false)
                {
                    return;
                }
                _preparationError = new LocalizedText(exception);
            }
            finally
            {
                if (ReferenceEquals(_preparationCancellation, cancellation) == true)
                {
                    _preparationCancellation = null;
                    _preparing = false;
                    RefreshDisplay();
                }
                cancellation.Dispose();
            }
        }

        private void WindowClosed(object sender, EventArgs eventArgs)
        {
            _closed = true;
            _preparationCancellation?.Cancel();
        }

        private void OnOpened(object sender, EventArgs eventArgs)
        {
            if (_cancellationToken.IsCancellationRequested == true)
            {
                Close(false);
                return;
            }

            CancelButton.Focus();
        }

        private void ConfirmClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_preparing == true)
            {
                return;
            }
            if (_currentPlan == null)
            {
                return;
            }
            if (_preparePlan != null)
            {
                if ((DirectoryPathInput.Text ?? string.Empty) != _preparedInput)
                {
                    return;
                }
            }
            if (_cancellationToken.IsCancellationRequested == true)
            {
                Close(false);
                return;
            }
            ConfirmedPlan = _currentPlan;
            Close(true);
        }

        private void CancelClicked(object sender, RoutedEventArgs eventArgs)
        {
            Close(false);
        }
    }
}
