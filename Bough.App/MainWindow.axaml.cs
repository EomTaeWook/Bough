using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.App.Views;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly CloneRepositoryPresenter _clonePresenter;
        private ConflictWindow _conflictWindow;
        private bool _closeConfirmed;
        private bool _revertAbortConfirmationPending;
        private bool _wasDeactivated;
        private bool _conflictWasDeactivated;
        private bool _stashDialogOpen;
        private bool _remoteDialogOpen;
        private int _activationSuppressionDepth;
        private int _activationVersion;
        private DateTime _lastActivationRefreshUtc;

        private void RefreshLocalizedLabels()
        {
            ToolTip.SetTip(RepositoryActionsButton, _stringHelper.GetString("RepositoryActionsTooltip"));
            Avalonia.Automation.AutomationProperties.SetName(RepositoryActionsButton, _stringHelper.GetString("RepositoryActionsButtonText"));
            OpenRepositoryActionLabel.Text = _stringHelper.GetString("RepositoryAddExistingAction");
            OpenRepositoryMenuItem.Header = _stringHelper.GetString("RepositoryAddExistingAction");
            ToolTip.SetTip(OpenRepositoryMenuItem, _viewModel.RepositoryList.AddRepositoryTooltipText);
            ToolTip.SetTip(OpenRepositoryButton, _viewModel.RepositoryList.AddRepositoryTooltipText);
            Avalonia.Automation.AutomationProperties.SetName(OpenRepositoryButton, _stringHelper.GetString("RepositoryAddExistingAction"));
            CloneRepositoryActionLabel.Text = _stringHelper.GetString("CloneAction");
            CloneRepositoryMenuItem.Header = _stringHelper.GetString("CloneAction");
            ToolTip.SetTip(CloneRepositoryMenuItem, _stringHelper.GetString("CloneTitle"));
            ToolTip.SetTip(CloneRepositoryButton, _stringHelper.GetString("CloneTitle"));
            Avalonia.Automation.AutomationProperties.SetName(CloneRepositoryButton, _stringHelper.GetString("CloneTitle"));
        }

        public MainWindow(MainWindowViewModel viewModel, StringHelper stringHelper, GitErrorLocalizer errorLocalizer,
            GitOperationQueue operationQueue, CloneRepositoryPresenter clonePresenter, GitSettingsService settingsService,
            Func<RemoteOperationsViewModel> createRemoteOperationSession)
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }
            if (stringHelper == null)
            {
                throw new ArgumentNullException(nameof(stringHelper));
            }
            if (errorLocalizer == null)
            {
                throw new ArgumentNullException(nameof(errorLocalizer));
            }
            if (operationQueue == null)
            {
                throw new ArgumentNullException(nameof(operationQueue));
            }
            if (clonePresenter == null)
            {
                throw new ArgumentNullException(nameof(clonePresenter));
            }
            if (settingsService == null)
            {
                throw new ArgumentNullException(nameof(settingsService));
            }
            if (createRemoteOperationSession == null)
            {
                throw new ArgumentNullException(nameof(createRemoteOperationSession));
            }

            InitializeComponent();
            _stringHelper = stringHelper;
            _errorLocalizer = errorLocalizer;
            _clonePresenter = clonePresenter;
            _viewModel = viewModel;
            DataContext = _viewModel;
            RepositoryPickerPanel.DataContext = _viewModel;
            LanguageChangeBinding.Bind(this, () => _stringHelper, RefreshLocalizedLabels);
            RefreshLocalizedLabels();
            RemoteOperationsPanel.StringHelper = stringHelper;
            RemoteOperationsPanel.SettingsService = settingsService;
            RemoteOperationsPanel.ErrorLocalizer = errorLocalizer;
            RemoteOperationsPanel.OperationQueue = operationQueue;
            RemoteOperationsPanel.CreateOperationSession = createRemoteOperationSession;
            RemoteOperationsPanel.GetRepositoryRequestVersion = () => _viewModel.RepositoryRequestVersion;
            RemoteOperationsPanel.OperationFinishedAsync = _viewModel.HandleRemoteOperationFinishedAsync;
            RemoteOperationsPanel.PopupProgressChanged = _viewModel.UpdateRemotePopupProgress;
            _viewModel.ConflictWindowRequested += OpenConflictWindow;
            _viewModel.ConflictResolutionCompleted += CloseCompletedConflictWindow;
            LocalChangesPanel.StashDialogOpening += OnStashDialogOpening;
            LocalChangesPanel.StashDialogClosed += OnStashDialogClosed;
            RemoteOperationsPanel.InternalDialogOpening += OnRemoteDialogOpening;
            RemoteOperationsPanel.InternalDialogClosed += OnRemoteDialogClosed;
            _viewModel.RemoteOperations.PropertyChanged += OnRemoteOperationsPropertyChanged;
            _viewModel.GitSettings.PropertyChanged += OnGitSettingsPropertyChanged;
            UpdateSettingsAndRemoteAvailability();
            Closing += async (sender, eventArgs) =>
            {
                if (_closeConfirmed == true)
                {
                    return;
                }
                if (_conflictWindow == null)
                {
                    return;
                }
                if (_viewModel.Conflicts.HasUnsavedConflictEdits == false)
                {
                    return;
                }

                eventArgs.Cancel = true;
                ConflictWindow window = _conflictWindow;
                string draftText = _viewModel.Conflicts.ResultText;
                _activationSuppressionDepth++;
                bool discard;
                try
                {
                    discard = await GitActionDialogs.ConfirmAsync(this,
                        _stringHelper.GetString("DiscardResolutionTitle"),
                        _stringHelper.GetString("DiscardResolutionCloseAppMessage"),
                        _stringHelper.GetString("DiscardResolutionConfirm"), _stringHelper);
                }
                finally
                {
                    _activationSuppressionDepth--;
                    CompleteInternalDialog();
                }
                if (ReferenceEquals(_conflictWindow, window) == false)
                {
                    return;
                }
                if (draftText != _viewModel.Conflicts.ResultText)
                {
                    return;
                }
                if (discard == true)
                {
                    _closeConfirmed = true;
                    _conflictWindow.CloseAfterConfirmation();
                    Close();
                }
            };
            Opened += async delegate
            {
                if (string.IsNullOrWhiteSpace(_viewModel.RepositoryList.LastActivePath) == false)
                {
                    await _viewModel.OpenRepositoryAsync(_viewModel.RepositoryList.LastActivePath);
                }
            };
            Deactivated += delegate
            {
                _wasDeactivated = true;
                _activationVersion++;
            };
            Activated += async delegate
            {
                if (_wasDeactivated == false)
                {
                    return;
                }
                _wasDeactivated = false;
                await RefreshAfterActivationAsync();
            };
            KeyDown += async (sender, eventArgs) =>
            {
                if (eventArgs.Key != Key.F5)
                {
                    return;
                }
                eventArgs.Handled = true;
                await _viewModel.RefreshAsync();
            };
        }

        private async Task RefreshAfterActivationAsync()
        {
            int version = ++_activationVersion;
            await Task.Delay(250);
            if (version != _activationVersion)
            {
                return;
            }
            if (_activationSuppressionDepth > 0)
            {
                return;
            }
            if (_stashDialogOpen == true)
            {
                return;
            }
            if (_remoteDialogOpen == true)
            {
                return;
            }
            if (_viewModel.IsRepositoryMutationInProgress == true)
            {
                return;
            }
            if (_viewModel.IsGitOperationRunning == true)
            {
                return;
            }
            if (DateTime.UtcNow - _lastActivationRefreshUtc < TimeSpan.FromMilliseconds(800))
            {
                return;
            }
            while (_viewModel.RefreshCommand.CanExecute(null) == false)
            {
                if (_viewModel.IsBusy == true)
                {
                    return;
                }
                if (_viewModel.IsRepositoryMutationInProgress == true)
                {
                    return;
                }
                if (_viewModel.IsGitOperationRunning == true)
                {
                    return;
                }
                if (IsRepositoryAreaLoading() == false)
                {
                    return;
                }
                await Task.Delay(250);
                if (version != _activationVersion)
                {
                    return;
                }
                if (_activationSuppressionDepth > 0)
                {
                    return;
                }
                if (_stashDialogOpen == true)
                {
                    return;
                }
                if (_remoteDialogOpen == true)
                {
                    return;
                }
                if (_viewModel.IsRepositoryMutationInProgress == true)
                {
                    return;
                }
                if (_viewModel.IsGitOperationRunning == true)
                {
                    return;
                }
            }
            if (DateTime.UtcNow - _lastActivationRefreshUtc < TimeSpan.FromMilliseconds(800))
            {
                return;
            }
            _lastActivationRefreshUtc = DateTime.UtcNow;
            await _viewModel.RefreshAsync();
        }

        private bool IsRepositoryAreaLoading()
        {
            if (_viewModel.IsLocalChangesLoading == true)
            {
                return true;
            }
            if (_viewModel.IsReferencesLoading == true)
            {
                return true;
            }
            if (_viewModel.IsRemoteLoading == true)
            {
                return true;
            }
            if (_viewModel.History.IsLoading == true)
            {
                return true;
            }
            return false;
        }

        private void OnStashDialogOpening()
        {
            _stashDialogOpen = true;
            _activationVersion++;
        }

        private void OnStashDialogClosed(bool saved)
        {
            _stashDialogOpen = false;
            CompleteInternalDialog();
        }

        private void OnRemoteDialogOpening()
        {
            _remoteDialogOpen = true;
            _activationVersion++;
        }

        private void OnRemoteDialogClosed()
        {
            _remoteDialogOpen = false;
            CompleteInternalDialog();
        }

        private void CompleteInternalDialog()
        {
            _wasDeactivated = false;
            _activationVersion++;
        }

        private void OnRemoteOperationsPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(RemoteOperationsViewModel.IsBusy))
            {
                return;
            }
            UpdateSettingsAndRemoteAvailability();
        }

        private void OnGitSettingsPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(GitSettingsViewModel.IsBusy))
            {
                return;
            }
            UpdateSettingsAndRemoteAvailability();
        }

        private void UpdateSettingsAndRemoteAvailability()
        {
            GitSettingsPanel.IsEnabled = _viewModel.RemoteOperations.IsBusy == false;
            RemoteOperationsPanel.IsEnabled = _viewModel.GitSettings.IsBusy == false;
        }

        private void OpenConflictWindow(string path)
        {
            if (_conflictWindow != null)
            {
                _conflictWindow.Activate();
                return;
            }

            ConflictWindow window = new(_viewModel.Conflicts, _viewModel.RefreshAsync, _stringHelper, _errorLocalizer);
            window.Title = $"{_stringHelper.GetString("OpenResolve")} · {_viewModel.RepositoryName}";
            _conflictWindow = window;
            window.Closed += delegate
            {
                _viewModel.Conflicts.DiscardClosedWindowEdits();
                if (ReferenceEquals(_conflictWindow, window) == true)
                {
                    _conflictWindow = null;
                }
                _wasDeactivated = false;
            };
            window.Deactivated += delegate
            {
                _conflictWasDeactivated = true;
                _activationVersion++;
            };
            window.Activated += async delegate
            {
                if (_conflictWasDeactivated == false)
                {
                    return;
                }
                _conflictWasDeactivated = false;
                await RefreshAfterActivationAsync();
            };
            window.Show(this);
        }

        private async void ContinueRevertClicked(object sender, RoutedEventArgs eventArgs)
        {
            await _viewModel.ContinueRevertAsync();
        }

        private async void AbortRevertClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_revertAbortConfirmationPending)
            {
                return;
            }
            if (_viewModel.CanAbortRevert == false)
            {
                return;
            }
            GitRepository repository = _viewModel.CurrentRepository;
            GitRevertState state = _viewModel.RevertState;
            int request = _viewModel.RepositoryRequestVersion;
            ConflictWindow window = _conflictWindow;
            string path = _viewModel.Conflicts.CurrentFilePath;
            string draft = _viewModel.Conflicts.ResultText;
            bool confirmed;
            _revertAbortConfirmationPending = true;
            _activationSuppressionDepth++;
            try
            {
                confirmed = await GitActionDialogs.ConfirmAsync(this,
                    _stringHelper.GetString("MainRevertAbortTitle"),
                    _stringHelper.Format("MainRevertAbortMessage", repository.RootPath),
                    _stringHelper.GetString("MainRevertAbortAction"), _stringHelper);
            }
            finally
            {
                _activationSuppressionDepth--;
                _revertAbortConfirmationPending = false;
                CompleteInternalDialog();
            }
            if (confirmed == false)
            {
                return;
            }
            if (request != _viewModel.RepositoryRequestVersion)
            {
                return;
            }
            if (ReferenceEquals(_viewModel.CurrentRepository, repository) == false)
            {
                return;
            }
            if (ReferenceEquals(_viewModel.RevertState, state) == false)
            {
                return;
            }
            if (ReferenceEquals(_conflictWindow, window) == false)
            {
                return;
            }
            if (_viewModel.Conflicts.CurrentFilePath != path)
            {
                return;
            }
            if (_viewModel.Conflicts.ResultText != draft)
            {
                return;
            }
            GitRevertResult result = await _viewModel.AbortRevertAsync(repository, state, request, path, draft);
            if (request != _viewModel.RepositoryRequestVersion)
            {
                return;
            }
            if (result == null)
            {
                return;
            }
            GitRepository applied = repository;
            if (result.Repository != null)
            {
                applied = result.Repository;
            }
            if (ReferenceEquals(_viewModel.CurrentRepository, applied) == false)
            {
                return;
            }
            if (result.Outcome != GitRevertOutcome.Aborted)
            {
                return;
            }
            if (result.State == null)
            {
                return;
            }
            if (result.State.IsInProgress)
            {
                return;
            }
            if (ReferenceEquals(_conflictWindow, window) == false)
            {
                return;
            }
            CloseCompletedConflictWindow();
        }

        private void CloseCompletedConflictWindow()
        {
            if (_conflictWindow == null)
            {
                return;
            }

            if (_viewModel.Conflicts.HasUnsavedConflictEdits)
            {
                return;
            }
            _conflictWindow.CloseAfterConfirmation();
            _conflictWindow = null;
            _wasDeactivated = false;
            Activate();
        }

        private async Task<bool> CloseConflictWindowForRepositoryChangeAsync()
        {
            if (_conflictWindow == null)
            {
                return true;
            }

            ConflictWindow window = _conflictWindow;
            if (_viewModel.Conflicts.HasUnsavedConflictEdits == true)
            {
                string draftText = _viewModel.Conflicts.ResultText;
                _activationSuppressionDepth++;
                bool discard;
                try
                {
                    discard = await GitActionDialogs.ConfirmAsync(this,
                        _stringHelper.GetString("DiscardResolutionTitle"),
                        _stringHelper.GetString("DiscardResolutionChangeRepositoryMessage"),
                        _stringHelper.GetString("DiscardResolutionConfirm"), _stringHelper);
                }
                finally
                {
                    _activationSuppressionDepth--;
                    CompleteInternalDialog();
                }
                if (ReferenceEquals(_conflictWindow, window) == false)
                {
                    return false;
                }
                if (draftText != _viewModel.Conflicts.ResultText)
                {
                    return false;
                }
                if (discard == false)
                {
                    return false;
                }
            }

            _conflictWindow.CloseAfterConfirmation();
            _conflictWindow = null;
            _wasDeactivated = false;
            return true;
        }

        private void RepositoryActionsClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }

            CloseRepositoryMenus();
            ContextMenu menu = button.ContextMenu;
            OpenRepositoryMenuItem.IsEnabled = _viewModel.IsRepositoryMutationInProgress == false;
            menu.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX |
                PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.FlipY;
            menu.Open(button);
        }

        private void CloseRepositoryMenus()
        {
            RepositoryPickerButton.Flyout.Hide();
            RepositoryActionsButton.ContextMenu?.Close();
        }

        private async void RepositoryClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (button.DataContext is not RepositoryItem item)
            {
                return;
            }
            if (_viewModel.IsRepositoryMutationInProgress == true)
            {
                return;
            }
            CloseRepositoryMenus();
            if (item.RootPath != _viewModel.CurrentRepositoryRoot)
            {
                if (await CloseConflictWindowForRepositoryChangeAsync() == false)
                {
                    return;
                }
            }
            await _viewModel.OpenRepositoryAsync(item.RootPath);
        }

        private async void RemoveRepositoryClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (button.DataContext is not RepositoryItem item)
            {
                return;
            }
            if (_viewModel.IsRepositoryMutationInProgress == true)
            {
                return;
            }
            if (item.RootPath == _viewModel.CurrentRepositoryRoot)
            {
                CloseRepositoryMenus();
                if (await CloseConflictWindowForRepositoryChangeAsync() == false)
                {
                    return;
                }
            }
            _viewModel.RemoveRepository(item);
        }

        private async void OpenRepositoryClicked(object sender, RoutedEventArgs eventArgs)
        {
            CloseRepositoryMenus();
            if (_viewModel.IsRepositoryMutationInProgress == true)
            {
                return;
            }
            _activationSuppressionDepth++;
            try
            {
                FolderPickerOpenOptions options = new();
                options.Title = _stringHelper.GetString("OpenGitRepository");
                options.AllowMultiple = false;
                IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(options);
                if (folders.Count == 0)
                {
                    return;
                }

                string path = folders[0].TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path) == true)
                {
                    return;
                }

                if (await CloseConflictWindowForRepositoryChangeAsync() == false)
                {
                    return;
                }
                await _viewModel.OpenRepositoryAsync(path);
            }
            finally
            {
                _activationSuppressionDepth--;
                CompleteInternalDialog();
            }
        }

        private async void CloneRepositoryClicked(object sender, RoutedEventArgs eventArgs)
        {
            CloseRepositoryMenus();
            _activationSuppressionDepth++;
            try
            {
                CloneRepositoryWindow dialog = new(_clonePresenter, _stringHelper, _errorLocalizer);
                string destination = await dialog.ShowDialog<string>(this);
                if (string.IsNullOrWhiteSpace(destination))
                {
                    return;
                }
                _viewModel.RegisterClonedRepository(destination);
                if (await CloseConflictWindowForRepositoryChangeAsync() == false)
                {
                    return;
                }
                await _viewModel.OpenClonedRepositoryAsync(destination);
            }
            finally
            {
                _activationSuppressionDepth--;
                CompleteInternalDialog();
            }
        }

        private void OpenFolderClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_viewModel.References.OpenFolderCommand.CanExecute(null) == true)
            {
                _viewModel.References.OpenFolderCommand.Execute(null);
            }
        }
    }
}
