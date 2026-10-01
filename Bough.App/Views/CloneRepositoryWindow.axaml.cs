using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.Presenters;

namespace Bough.App.Views
{
    public partial class CloneRepositoryWindow : Window
    {
        private readonly CloneRepositoryPresenter _presenter;
        private readonly StringHelper _strings;
        private readonly GitErrorLocalizer _errorLocalizer;
        private CancellationTokenSource _cancellation;
        private bool _running;
        private bool _cloneProcessStarted;
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
            try
            {
                _destination = _presenter.ValidateDestination(remote, destinationPath);
            }
            catch (Exception exception)
            {
                StatusText.Text = _errorLocalizer.GetDisplayMessage(exception);
                return;
            }

            DestinationOutcomeText.Text = string.Empty;
            StatusText.Text = _strings.GetString("CloneQueued");
            SetRunning(true);
            _cloneProcessStarted = false;
            _cancellation = new CancellationTokenSource();
            Progress<int> progress = new(percentage =>
            {
                if (_running == false)
                {
                    return;
                }
                StatusText.Text = _strings.Format("CloneProgress", percentage);
            });
            try
            {
                string destination = await _presenter.CloneAsync(remote, _destination,
                    _strings.GetString("CloneAction"), progress, () =>
                    {
                        _cloneProcessStarted = true;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_running == true)
                            {
                                StatusText.Text = _strings.GetString("CloneRunning");
                            }
                        });
                    }, _cancellation.Token);
                StatusText.Text = _strings.GetString("CloneSucceeded");
                SetRunning(false);
                Close(destination);
                return;
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = _strings.GetString("CloneCancelled");
            }
            catch (Exception exception)
            {
                StatusText.Text = _errorLocalizer.GetDisplayMessage(exception);
            }
            finally
            {
                _cancellation.Dispose();
                _cancellation = null;
                SetRunning(false);
            }
            if (_cloneProcessStarted == false)
            {
                return;
            }
            if (Path.Exists(_destination))
            {
                DestinationOutcomeText.Text = _strings.Format("CloneDestinationPreserved", _destination);
            }
            else
            {
                DestinationOutcomeText.Text = _strings.GetString("CloneDestinationAbsent");
            }
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
