using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.App.Views;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;
using Bough.App.Infrastructure.Updates;

namespace Bough.App
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly StringHelper _stringHelper;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly CloneRepositoryPresenter _clonePresenter;
        private readonly GitOperationQueue _updateOperationQueue;
        private TaskCompletionSource<bool> _updateCloseRequest;
        private CancellationToken _updateCloseCancellation;
        internal bool UpdateCloseApproved { get; private set; }
        private ConflictWindow _conflictWindow;
        private bool _closeConfirmed;
        private bool _autoFetchCloseReady;
        private bool _autoFetchClosePending;
        private bool _mainWindowClosed;
        private bool _revertAbortConfirmationPending;
        private bool _wasDeactivated;
        private bool _conflictWasDeactivated;
        private bool _stashDialogOpen;
        private bool _remoteDialogOpen;
        private int _activationSuppressionDepth;
        private int _activationVersion;
        private int _submoduleOpenRequestVersion;
        private CancellationTokenSource _submoduleOpenCancellation;
        private DateTime _lastActivationRefreshUtc;

        private void RefreshLocalizedLabels()
        {
            SidebarStatusDetailsButton.Content = _stringHelper.GetString("RemoteOperationDetails");
            Avalonia.Automation.AutomationProperties.SetName(SidebarStatusDetailsButton, _stringHelper.GetString("RemoteOperationDetails"));
            Avalonia.Automation.AutomationProperties.SetName(SidebarStatusPreview, _stringHelper.GetString("MainStatusDetailsTitle"));
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
            _updateOperationQueue = operationQueue;
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
            RemoteOperationsPanel.TagPushRequestedAsync = RequestTagPushAsync;
            RemoteOperationsPanel.PopupProgressChanged = _viewModel.UpdateRemotePopupProgress;
            _viewModel.References.SubmoduleOpenRequested += OnSubmoduleOpenRequested;
            _viewModel.ConflictWindowRequested += OpenConflictWindow;
            _viewModel.ConflictResolutionCompleted += CloseCompletedConflictWindow;
            LocalChangesPanel.StashDialogOpening += OnStashDialogOpening;
            LocalChangesPanel.StashDialogClosed += OnStashDialogClosed;
            RemoteOperationsPanel.InternalDialogOpening += OnRemoteDialogOpening;
            RemoteOperationsPanel.InternalDialogClosed += OnRemoteDialogClosed;
            _viewModel.RemoteOperations.PropertyChanged += OnRemoteOperationsPropertyChanged;
            _viewModel.GitSettings.PropertyChanged += OnGitSettingsPropertyChanged;
            _viewModel.GitSettings.AppUpdate.PropertyChanged += OnApplicationUpdatePropertyChanged;
            RefreshApplicationUpdateProgress();
            UpdateSettingsAndRemoteAvailability();
            Closing += OnMainWindowClosing;
            Closed += delegate
            {
                _mainWindowClosed = true;
                _viewModel.StopTagPushCandidates();
                RemoteOperationsPanel.TagPushRequestedAsync = null;
                _viewModel.GitSettings.AppUpdate.PropertyChanged -= OnApplicationUpdatePropertyChanged;
                _viewModel.References.SubmoduleOpenRequested -= OnSubmoduleOpenRequested;
                _submoduleOpenRequestVersion++;
                _submoduleOpenCancellation?.Cancel();
                _viewModel.StopAutoFetch();
                _updateCloseRequest?.TrySetResult(true);
            };
            Opened += async delegate
            {
                _viewModel.StartAutoFetch();
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

        private async void SidebarStatusDetailsClicked(object sender, RoutedEventArgs eventArgs)
        {
            string statusText = SidebarStatusPreview.Text;
            await ShowSidebarStatusDetailsAsync(statusText);
        }

        private async Task ShowSidebarStatusDetailsAsync(string statusText)
        {
            if (_mainWindowClosed == true)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(statusText) == true)
            {
                return;
            }
            _activationSuppressionDepth++;
            try
            {
                SidebarStatusDetailsWindow dialog = new(statusText, _stringHelper);
                await dialog.ShowDialog(this);
            }
            finally
            {
                _activationSuppressionDepth--;
                if (_mainWindowClosed == false)
                {
                    CompleteInternalDialog();
                }
            }
        }

        internal async Task PrepareUpdateCloseAsync(CancellationToken cancellationToken)
        {
            await _viewModel.PauseAutoFetchAsync();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureUpdateCanClose();
            }
            catch
            {
                ResumeAutoFetchAfterUpdate();
                throw;
            }
        }

        internal void ResumeAutoFetchAfterUpdate()
        {
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(ResumeAutoFetchAfterUpdate);
                return;
            }
            if (_mainWindowClosed == false)
            {
                _viewModel.ResumeAutoFetch();
            }
        }

        internal void EnsureUpdateCanClose()
        {
            if (_updateOperationQueue.HasActiveOperations)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.IsBusy)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.Conflicts.IsBusy)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.Conflicts.IsBatchStaging)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.LocalChanges.IsBusy)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.LocalChanges.Stashes.IsBusy)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.GitSettings.IsBusy)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.GitSettings.CanChangeAutoFetchSettings == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.References.IsBusy)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.RemoteOperations.IsBusy)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_viewModel.History.IsLoading)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_stashDialogOpen)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_remoteDialogOpen)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_activationSuppressionDepth > 0)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
            if (_revertAbortConfirmationPending)
            {
                throw new ApplicationUpdateException("AppUpdateCoreGitBusy", null, Array.Empty<object>());
            }
        }

        internal async Task<bool> RequestUpdateCloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureUpdateCanClose();
            if (_updateCloseRequest != null)
            {
                throw new ApplicationUpdateException("AppUpdateCoreCloseUnavailable", null, Array.Empty<object>());
            }
            TaskCompletionSource<bool> request = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _updateCloseRequest = request;
            _updateCloseCancellation = cancellationToken;
            UpdateCloseApproved = false;
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                // View teardown after accepted normal close must not revoke an installation handoff.
                if (UpdateCloseApproved == false)
                {
                    request.TrySetCanceled(cancellationToken);
                }
            });
            try
            {
                Close();
                return await request.Task.ConfigureAwait(false);
            }
            finally
            {
                if (ReferenceEquals(_updateCloseRequest, request))
                {
                    _updateCloseRequest = null;
                    _updateCloseCancellation = default;
                }
            }
        }

        private async void OnMainWindowClosing(object sender, WindowClosingEventArgs eventArgs)
        {
            if (_autoFetchCloseReady == false)
            {
                eventArgs.Cancel = true;
                if (_autoFetchClosePending == true)
                {
                    return;
                }
                _autoFetchClosePending = true;
                try
                {
                    await _viewModel.GitSettings.WaitForAutoFetchSettingsSaveAsync();
                    await _viewModel.PauseAutoFetchAsync();
                    await _viewModel.PauseTagPushCandidatesAsync();
                    _autoFetchCloseReady = true;
                    Dispatcher.UIThread.Post(Close);
                }
                finally
                {
                    _autoFetchClosePending = false;
                }
                return;
            }
            try
            {
                await CompleteMainWindowCloseAsync(eventArgs);
            }
            finally
            {
                if (eventArgs.Cancel == true)
                {
                    if (_mainWindowClosed == false)
                    {
                        _autoFetchCloseReady = false;
                        _viewModel.ResumeTagPushCandidates();
                        if (_updateCloseRequest == null)
                        {
                            _viewModel.ResumeAutoFetch();
                        }
                    }
                }
            }
        }

        private async Task CompleteMainWindowCloseAsync(WindowClosingEventArgs eventArgs)
        {
            TaskCompletionSource<bool> updateRequest = _updateCloseRequest;
            if (updateRequest != null)
            {
                try
                {
                    _updateCloseCancellation.ThrowIfCancellationRequested();
                    EnsureUpdateCanClose();
                }
                catch (Exception exception)
                {
                    eventArgs.Cancel = true;
                    updateRequest.TrySetException(exception);
                    return;
                }
            }
            if (_viewModel.GitSettings.CanChangeAutoFetchSettings == false)
            {
                eventArgs.Cancel = true;
                return;
            }
            if (_closeConfirmed)
            {
                UpdateCloseApproved = updateRequest != null;
                return;
            }
            if (_conflictWindow == null)
            {
                UpdateCloseApproved = updateRequest != null;
                return;
            }
            if (_viewModel.Conflicts.HasUnsavedConflictEdits == false)
            {
                UpdateCloseApproved = updateRequest != null;
                return;
            }
            eventArgs.Cancel = true;
            ConflictWindow window = _conflictWindow;
            string draftText = _viewModel.Conflicts.ResultText;
            bool draftDeleted = _viewModel.Conflicts.IsResultFileDeleted;
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
                updateRequest?.TrySetResult(false);
                return;
            }
            if (draftText != _viewModel.Conflicts.ResultText)
            {
                updateRequest?.TrySetResult(false);
                return;
            }
            if (draftDeleted != _viewModel.Conflicts.IsResultFileDeleted)
            {
                updateRequest?.TrySetResult(false);
                return;
            }
            if (discard == false)
            {
                updateRequest?.TrySetResult(false);
                return;
            }
            if (updateRequest != null)
            {
                if (ReferenceEquals(_updateCloseRequest, updateRequest) == false)
                {
                    return;
                }
                try
                {
                    _updateCloseCancellation.ThrowIfCancellationRequested();
                    EnsureUpdateCanClose();
                }
                catch (Exception exception)
                {
                    updateRequest.TrySetException(exception);
                    return;
                }
            }
            _closeConfirmed = true;
            _conflictWindow.CloseAfterConfirmation();
            Close();
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

        private void OnApplicationUpdatePropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
        {
            if (_mainWindowClosed == true)
            {
                return;
            }
            if (ReferenceEquals(sender, _viewModel.GitSettings.AppUpdate) == false)
            {
                return;
            }
            switch (propertyChangedEventArguments.PropertyName)
            {
                case nameof(AppUpdateViewModel.IsDownloading):
                case nameof(AppUpdateViewModel.DownloadedBytes):
                case nameof(AppUpdateViewModel.TotalBytes):
                    break;
                default:
                    return;
            }
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(() => OnApplicationUpdatePropertyChanged(sender, propertyChangedEventArguments));
                return;
            }
            RefreshApplicationUpdateProgress();
            if (propertyChangedEventArguments.PropertyName == nameof(AppUpdateViewModel.IsDownloading))
            {
                _viewModel.NotifySidebarStatusChanged();
            }
        }

        private void RefreshApplicationUpdateProgress()
        {
            AppUpdateViewModel applicationUpdate = _viewModel.GitSettings.AppUpdate;
            bool isIndeterminate = true;
            double progressValue = 0;
            if (applicationUpdate.IsDownloading == true)
            {
                if (applicationUpdate.TotalBytes > 0)
                {
                    isIndeterminate = false;
                    progressValue = Math.Clamp((double)applicationUpdate.DownloadedBytes / applicationUpdate.TotalBytes * 100, 0, 100);
                }
            }
            SidebarProgressBar.IsIndeterminate = isIndeterminate;
            SidebarProgressBar.Value = progressValue;
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
            bool draftDeleted = _viewModel.Conflicts.IsResultFileDeleted;
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
            if (_viewModel.Conflicts.IsResultFileDeleted != draftDeleted)
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
            if (_viewModel.Conflicts.IsBatchStaging)
            {
                return;
            }
            if (_viewModel.Conflicts.HasBatchStageIssues)
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
                bool draftDeleted = _viewModel.Conflicts.IsResultFileDeleted;
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
                if (draftDeleted != _viewModel.Conflicts.IsResultFileDeleted)
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

        private async Task RequestTagPushAsync(GitRepository repository, int repositoryRequestVersion)
        {
            if (_mainWindowClosed == true)
            {
                return;
            }
            if (repository == null)
            {
                return;
            }
            if (_viewModel.IsCurrentRepositoryRequest(repository, repositoryRequestVersion) == false)
            {
                return;
            }
            ReferenceExplorerViewModel references = _viewModel.References;
            GitRepository referenceRepository = references.CurrentRepository;
            if (referenceRepository == null)
            {
                return;
            }
            StringComparison pathComparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                pathComparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(referenceRepository.RootPath, repository.RootPath, pathComparison) == false)
            {
                return;
            }
            int referenceBindingVersion = references.ReferenceBindingVersion;
            OnRemoteDialogOpening();
            try
            {
                await _viewModel.PauseTagPushCandidatesAsync();
                if (_mainWindowClosed == true)
                {
                    return;
                }
                if (_viewModel.IsCurrentRepositoryRequest(repository, repositoryRequestVersion) == false)
                {
                    return;
                }
                if (referenceBindingVersion != references.ReferenceBindingVersion)
                {
                    return;
                }
                if (references.CurrentRepository == null)
                {
                    return;
                }
                if (string.Equals(references.CurrentRepository.RootPath, repository.RootPath, pathComparison) == false)
                {
                    return;
                }
                await references.RefreshAsync(updateStatus: false);
                if (_mainWindowClosed == true)
                {
                    return;
                }
                if (_viewModel.IsCurrentRepositoryRequest(repository, repositoryRequestVersion) == false)
                {
                    return;
                }
                if (references.CurrentRepository == null)
                {
                    return;
                }
                if (referenceBindingVersion != references.ReferenceBindingVersion)
                {
                    return;
                }
                if (string.Equals(references.CurrentRepository.RootPath, repository.RootPath, pathComparison) == false)
                {
                    return;
                }
                await GitActionDialogs.RequestTagPushAsync(this, references, repository);
            }
            catch (Exception exception)
            {
                if (_mainWindowClosed == true)
                {
                    return;
                }
                if (_viewModel.IsCurrentRepositoryRequest(repository, repositoryRequestVersion) == false)
                {
                    return;
                }
                if (references.CurrentRepository == null)
                {
                    return;
                }
                if (referenceBindingVersion != references.ReferenceBindingVersion)
                {
                    return;
                }
                if (string.Equals(references.CurrentRepository.RootPath, repository.RootPath, pathComparison) == false)
                {
                    return;
                }
                _viewModel.ReportRemoteCompletionFailure(exception, repository, repositoryRequestVersion);
            }
            finally
            {
                if (_mainWindowClosed == false)
                {
                    _viewModel.ResumeTagPushCandidates();
                    OnRemoteDialogClosed();
                }
            }
        }

        private async void OnSubmoduleOpenRequested(GitRepository parentRepository, GitSubmodule submodule)
        {
            if (_mainWindowClosed == true)
            {
                return;
            }
            if (parentRepository == null)
            {
                return;
            }
            if (submodule == null)
            {
                return;
            }
            GitRepository repository = _viewModel.CurrentRepository;
            if (repository == null)
            {
                return;
            }
            StringComparison pathComparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                pathComparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(repository.RootPath, parentRepository.RootPath, pathComparison) == false)
            {
                return;
            }
            if (_viewModel.IsRepositoryMutationInProgress == true)
            {
                return;
            }

            int repositoryRequest = _viewModel.RepositoryRequestVersion;
            int openRequest = ++_submoduleOpenRequestVersion;
            _submoduleOpenCancellation?.Cancel();
            CancellationTokenSource cancellation = new();
            _submoduleOpenCancellation = cancellation;
            try
            {
                if (IsCurrentSubmoduleOpenRequest(repository, repositoryRequest, openRequest) == false)
                {
                    return;
                }
                string path = await _viewModel.References.PrepareSubmoduleOpenAsync(parentRepository, submodule, cancellation.Token);
                if (IsCurrentSubmoduleOpenRequest(repository, repositoryRequest, openRequest) == false)
                {
                    return;
                }
                if (path == null)
                {
                    return;
                }
                if (_viewModel.IsRepositoryMutationInProgress == true)
                {
                    return;
                }

                bool canChange = await CloseConflictWindowForRepositoryChangeAsync();
                if (IsCurrentSubmoduleOpenRequest(repository, repositoryRequest, openRequest) == false)
                {
                    return;
                }
                if (canChange == false)
                {
                    return;
                }
                if (_viewModel.IsRepositoryMutationInProgress == true)
                {
                    return;
                }
                await _viewModel.OpenRepositoryAsync(path);
            }
            catch (OperationCanceledException)
            {
                // Closing or superseding this read never cancels queued user mutations.
            }
            catch (Exception exception)
            {
                if (IsCurrentSubmoduleOpenRequest(repository, repositoryRequest, openRequest) == false)
                {
                    return;
                }
                _viewModel.ReportRemoteCompletionFailure(exception, repository, repositoryRequest);
            }
            finally
            {
                if (ReferenceEquals(_submoduleOpenCancellation, cancellation) == true)
                {
                    _submoduleOpenCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private bool IsCurrentSubmoduleOpenRequest(GitRepository repository, int repositoryRequest, int openRequest)
        {
            if (_mainWindowClosed == true)
            {
                return false;
            }
            if (repositoryRequest != _viewModel.RepositoryRequestVersion)
            {
                return false;
            }
            if (ReferenceEquals(_viewModel.CurrentRepository, repository) == false)
            {
                return false;
            }
            if (openRequest != _submoduleOpenRequestVersion)
            {
                return false;
            }
            return true;
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
