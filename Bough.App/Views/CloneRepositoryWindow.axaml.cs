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
            ParentLabel.Text = strings.GetString("CloneParentLabel");
            FolderLabel.Text = strings.GetString("CloneFolderLabel");
            BrowseButton.Content = strings.GetString("CloneBrowseParent");
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
                Title = _strings.GetString("ClonePickParentTitle"),
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
            ParentInput.Text = path;
        }

        private void DestinationInputChanged(object sender, TextChangedEventArgs eventArgs)
        {
            if (ParentInput == null)
            {
                return;
            }
            if (FolderInput == null)
            {
                return;
            }
            if (DestinationText == null)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(ParentInput.Text))
            {
                DestinationText.Text = string.Empty;
                return;
            }
            if (string.IsNullOrWhiteSpace(FolderInput.Text))
            {
                DestinationText.Text = string.Empty;
                return;
            }
            string folder = FolderInput.Text.Trim();
            if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                DestinationText.Text = string.Empty;
                return;
            }
            try
            {
                string destination = Path.GetFullPath(Path.Combine(ParentInput.Text.Trim(), folder));
                DestinationText.Text = _strings.Format("CloneDestinationPreview", destination);
            }
            catch (ArgumentException)
            {
                DestinationText.Text = string.Empty;
            }
            catch (NotSupportedException)
            {
                DestinationText.Text = string.Empty;
            }
            catch (PathTooLongException)
            {
                DestinationText.Text = string.Empty;
            }
        }

        private async void StartClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_running == true)
            {
                return;
            }
            string remote = RemoteInput.Text;
            string parent = ParentInput.Text;
            string folder = FolderInput.Text;
            try
            {
                _destination = _presenter.ValidateDestination(remote, parent, folder);
            }
            catch (Exception exception)
            {
                StatusText.Text = _errorLocalizer.GetDisplayMessage(exception);
                return;
            }

            DestinationText.Text = _strings.Format("CloneDestinationPreview", _destination);
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
                string destination = await _presenter.CloneAsync(remote, parent, folder,
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
            ParentInput.IsEnabled = running == false;
            FolderInput.IsEnabled = running == false;
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
