using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Views
{
    public partial class CloneRepositoryWindow : Window
    {
        private readonly CloneRepositoryPresenter _presenter;
        private readonly StringHelper _strings;
        private readonly GitErrorLocalizer _errorLocalizer;
        private CancellationTokenSource _cancellation;
        private bool _running;
        private volatile bool _cloneProcessStarted;
        private string _destination;

        public CloneRepositoryWindow(CloneRepositoryPresenter presenter, StringHelper strings, GitErrorLocalizer errorLocalizer)
        {
            _presenter = presenter;
            _strings = strings;
            _errorLocalizer = errorLocalizer;
            InitializeComponent();
            Title = strings.GetString("CloneTitle");
            HeadingText.Text = Title;
            RemoteLabel.Text = strings.GetString("CloneUrlLabel");
            DestinationLabel.Text = strings.GetString("CloneDestinationLabel");
            DestinationHint.Text = strings.GetString("CloneDestinationHint");
            BrowseButton.Content = strings.GetString("CloneBrowseDestination");
            StartButton.Content = strings.GetString("CloneAction");
            CancelButton.Content = strings.GetString("Cancel");
            RemoteInput.Focus();
        }

        protected override void OnClosing(WindowClosingEventArgs eventArgs)
        {
            if (_running == true)
            {
                _cancellation?.Cancel();
                eventArgs.Cancel = true;
                return;
            }
            base.OnClosing(eventArgs);
        }

        private async void BrowseClicked(object sender, RoutedEventArgs eventArgs)
        {
            FolderPickerOpenOptions options = new()
            {
                Title = _strings.GetString("ClonePickDestinationTitle"),
                AllowMultiple = false
            };
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(options);
            if (folders.Count == 0)
            {
                return;
            }
            string path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            DestinationInput.Text = path;
        }

        private async void StartClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_running == true)
            {
                return;
            }
            string remote = RemoteInput.Text;
            string destinationPath = DestinationInput.Text;
            DestinationOutcomeText.Text = string.Empty;
            _destination = null;
            _cloneProcessStarted = false;
            CancellationTokenSource cancellation = new();
            _cancellation = cancellation;
            SetRunning(true);
            StatusText.Text = _strings.GetString("CloneQueued");
            Progress<int> progress = new(percentage =>
            {
                if (ReferenceEquals(_cancellation, cancellation) == false)
                {
                    return;
                }
                if (_running == false)
                {
                    return;
                }
                StatusText.Text = _strings.Format("CloneProgress", percentage);
            });
            try
            {
                _destination = await _presenter.ValidateDestinationAsync(remote, destinationPath, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                string destination = await _presenter.CloneAsync(remote, _destination,
                    _strings.GetString("CloneAction"), progress, () =>
                    {
                        _cloneProcessStarted = true;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (ReferenceEquals(_cancellation, cancellation) == false)
                            {
                                return;
                            }
                            if (_running == false)
                            {
                                return;
                            }
                            StatusText.Text = _strings.GetString("CloneRunning");
                        });
                    }, cancellation.Token);
                if (ReferenceEquals(_cancellation, cancellation) == false)
                {
                    return;
                }
                StatusText.Text = _strings.GetString("CloneSucceeded");
                SetRunning(false);
                Close(destination);
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(_cancellation, cancellation) == false)
                {
                    return;
                }
                StatusText.Text = _strings.GetString("CloneCancelled");
                await ShowDestinationOutcomeAsync(cancellation);
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(_cancellation, cancellation) == false)
                {
                    return;
                }
                StatusText.Text = GetSafeFailureMessage(exception);
                await ShowDestinationOutcomeAsync(cancellation);
            }
            finally
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                    SetRunning(false);
                }
                cancellation.Dispose();
            }
        }

        private async Task ShowDestinationOutcomeAsync(CancellationTokenSource cancellation)
        {
            if (_cloneProcessStarted == false)
            {
                return;
            }
            string destination = _destination;
            GitCloneDestinationState state = await _presenter.GetDestinationStateAsync(destination);
            if (ReferenceEquals(_cancellation, cancellation) == false)
            {
                return;
            }
            string key = "CloneDestinationInspectionFailed";
            switch (state)
            {
                case GitCloneDestinationState.Absent:
                    key = "CloneDestinationMissingAfterAttempt";
                    break;
                case GitCloneDestinationState.EmptyDirectory:
                    key = "CloneDestinationEmptyAfterAttempt";
                    break;
                case GitCloneDestinationState.ContainsContent:
                    key = "CloneDestinationContentsAfterAttempt";
                    break;
            }
            DestinationOutcomeText.Text = _strings.Format(key, destination);
        }

        private string GetSafeFailureMessage(Exception exception)
        {
            // Structured clone/runner exceptions contain only validated paths and numeric
            // codes. An unstructured message may contain a remote URL or credentials.
            if (exception is GitException gitException)
            {
                if (string.IsNullOrWhiteSpace(gitException.ErrorCode) == false)
                {
                    return _errorLocalizer.GetDisplayMessage(gitException);
                }
            }
            if (exception is UnauthorizedAccessException)
            {
                return _errorLocalizer.GetDisplayMessage(exception);
            }
            if (exception is System.IO.IOException)
            {
                return _errorLocalizer.GetDisplayMessage(exception);
            }
            if (exception is System.Security.SecurityException)
            {
                return _errorLocalizer.GetDisplayMessage(exception);
            }
            return _strings.GetString("CloneFailureUnexpected");
        }

        private void CancelClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_running == true)
            {
                _cancellation.Cancel();
                return;
            }
            Close(null);
        }

        private void SetRunning(bool running)
        {
            _running = running;
            RemoteInput.IsEnabled = running == false;
            DestinationInput.IsEnabled = running == false;
            BrowseButton.IsEnabled = running == false;
            StartButton.IsEnabled = running == false;
            if (running == true)
            {
                CancelButton.Content = _strings.GetString("CloneStop");
            }
            else
            {
                CancelButton.Content = _strings.GetString("CloneClose");
            }
            CloneProgress.IsVisible = running;
        }
    }
}
