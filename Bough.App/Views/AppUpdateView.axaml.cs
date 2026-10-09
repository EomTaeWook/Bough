using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.Core.Updates;

namespace Bough.App.Views
{
    public partial class AppUpdateView : UserControl
    {
        private AppUpdateViewModel _boundModel;
        private bool _attached;
        private int _bindingVersion;

        public event EventHandler CheckRequested;
        public event EventHandler UpdateRequested;
        public event EventHandler CancelRequested;
        public event EventHandler ReleaseRequested;

        public AppUpdateView()
        {
            InitializeComponent();
            DataContextChanged += DataContextUpdated;
            AttachedToVisualTree += ViewAttached;
            DetachedFromVisualTree += ViewDetached;
            LanguageChangeBinding.Bind(this, () => (DataContext as AppUpdateViewModel)?.Strings, RefreshDisplay);
        }

        private void DataContextUpdated(object sender, EventArgs eventArgs)
        {
            if (_attached == false)
            {
                return;
            }
            BindModel();
        }

        private void ViewAttached(object sender, VisualTreeAttachmentEventArgs eventArgs)
        {
            _attached = true;
            BindModel();
        }

        private void ViewDetached(object sender, VisualTreeAttachmentEventArgs eventArgs)
        {
            _attached = false;
            UnbindModel();
        }

        private void BindModel()
        {
            AppUpdateViewModel model = DataContext as AppUpdateViewModel;
            if (ReferenceEquals(_boundModel, model))
            {
                return;
            }
            UnbindModel();
            _boundModel = model;
            if (_boundModel == null)
            {
                return;
            }
            _boundModel.PropertyChanged += ModelChanged;
            _boundModel.Activate();
            RefreshDisplay();
        }

        private void UnbindModel()
        {
            if (_boundModel == null)
            {
                return;
            }
            _boundModel.PropertyChanged -= ModelChanged;
            _boundModel.Deactivate();
            _boundModel = null;
            _bindingVersion++;
        }

        private void ModelChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (_attached == false)
            {
                return;
            }
            if (ReferenceEquals(sender, _boundModel) == false)
            {
                return;
            }
            if (eventArgs.PropertyName == nameof(AppUpdateViewModel.DownloadedBytes))
            {
                RefreshProgress(_boundModel);
                return;
            }
            if (eventArgs.PropertyName == nameof(AppUpdateViewModel.TotalBytes))
            {
                RefreshProgress(_boundModel);
                return;
            }
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_attached == false)
            {
                return;
            }
            if (DataContext is not AppUpdateViewModel model)
            {
                return;
            }
            StringHelper strings = model.Strings;
            Heading.Text = strings.GetString("AppUpdateHeading");
            Description.Text = strings.GetString("AppUpdateDescription");
            CurrentVersionLabel.Text = strings.GetString("AppUpdateCurrentVersionLabel");
            LatestVersionLabel.Text = strings.GetString("AppUpdateLatestVersionLabel");
            CurrentVersionValue.Text = model.CurrentVersion;
            if (string.IsNullOrEmpty(model.CurrentVersion))
            {
                CurrentVersionValue.Text = strings.GetString("AppUpdateVersionUnknown");
            }
            LatestVersionValue.Text = model.LatestVersion;
            if (string.IsNullOrEmpty(model.LatestVersion))
            {
                LatestVersionValue.Text = strings.GetString("AppUpdateVersionUnknown");
            }
            StatusText.Text = model.Status?.GetText(strings) ?? string.Empty;
            StatusText.IsVisible = string.IsNullOrEmpty(StatusText.Text) == false;
            ErrorText.Text = model.Error?.GetText(strings) ?? string.Empty;
            ErrorText.IsVisible = string.IsNullOrEmpty(ErrorText.Text) == false;
            CheckButton.Content = strings.GetString("AppUpdateCheckAction");
            AutomationProperties.SetName(CheckButton, strings.GetString("AppUpdateCheckAction"));
            UpdateButton.Content = strings.GetString("AppUpdateRestartAction");
            AutomationProperties.SetName(UpdateButton, strings.GetString("AppUpdateRestartAction"));
            CancelButton.Content = strings.GetString("AppUpdateCancelAction");
            AutomationProperties.SetName(CancelButton, strings.GetString("AppUpdateCancelAction"));
            ReleaseButton.Content = strings.GetString("AppUpdateReleaseAction");
            AutomationProperties.SetName(ReleaseButton, strings.GetString("AppUpdateReleaseAction"));
            ReleaseButton.IsVisible = string.IsNullOrEmpty(model.ReleaseUrl) == false;
            RefreshProgress(model);
        }

        private void RefreshProgress(AppUpdateViewModel model)
        {
            StringHelper strings = model.Strings;
            DownloadProgress.IsIndeterminate = model.TotalBytes <= 0;
            DownloadProgress.Value = 0;
            if (model.TotalBytes > 0)
            {
                DownloadProgress.Value = Math.Clamp((double)model.DownloadedBytes / model.TotalBytes * 100, 0, 100);
            }
            double downloaded = model.DownloadedBytes / 1048576d;
            if (model.TotalBytes > 0)
            {
                DownloadText.Text = strings.Format("AppUpdateDownloadingKnown", downloaded.ToString("F1"), (model.TotalBytes / 1048576d).ToString("F1"));
            }
            else
            {
                DownloadText.Text = strings.Format("AppUpdateDownloadingUnknown", downloaded.ToString("F1"));
            }
        }

        private async void CheckClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not AppUpdateViewModel model)
            {
                return;
            }
            if (model.CanCheck == false)
            {
                return;
            }
            int bindingVersion = _bindingVersion;
            try
            {
                CheckRequested?.Invoke(this, EventArgs.Empty);
                if (IsCurrentBinding(model, bindingVersion) == false)
                {
                    return;
                }
                await model.CheckAsync();
            }
            catch (Exception exception)
            {
                ShowRequestError(model, bindingVersion, "AppUpdateCheckFailed", "AppUpdateCoreQueryUnavailable", exception);
            }
        }

        private async void UpdateClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not AppUpdateViewModel model)
            {
                return;
            }
            if (model.CanUpdate == false)
            {
                return;
            }
            int bindingVersion = _bindingVersion;
            try
            {
                UpdateRequested?.Invoke(this, EventArgs.Empty);
                if (IsCurrentBinding(model, bindingVersion) == false)
                {
                    return;
                }
                await model.UpdateAsync();
            }
            catch (Exception exception)
            {
                ShowRequestError(model, bindingVersion, "AppUpdateRestartFailed", "AppUpdateCorePrepareFailed", exception);
            }
        }

        private void CancelClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not AppUpdateViewModel model)
            {
                return;
            }
            if (model.CanCancel == false)
            {
                return;
            }
            int bindingVersion = _bindingVersion;
            try
            {
                CancelRequested?.Invoke(this, EventArgs.Empty);
                if (IsCurrentBinding(model, bindingVersion) == false)
                {
                    return;
                }
                model.Cancel();
            }
            catch (Exception exception)
            {
                ShowRequestError(model, bindingVersion, "AppUpdateRestartFailed", "AppUpdateCorePrepareFailed", exception);
            }
        }

        private async void ReleaseClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not AppUpdateViewModel model)
            {
                return;
            }
            if (string.IsNullOrEmpty(model.ReleaseUrl))
            {
                return;
            }
            int bindingVersion = _bindingVersion;
            try
            {
                ReleaseRequested?.Invoke(this, EventArgs.Empty);
                if (IsCurrentBinding(model, bindingVersion) == false)
                {
                    return;
                }
                if (Uri.TryCreate(model.ReleaseUrl, UriKind.Absolute, out Uri address) == false)
                {
                    ShowReleaseError(model, bindingVersion, null);
                    return;
                }
                if (address.Scheme != Uri.UriSchemeHttps)
                {
                    ShowReleaseError(model, bindingVersion, null);
                    return;
                }
                TopLevel owner = TopLevel.GetTopLevel(this);
                if (owner == null)
                {
                    ShowReleaseError(model, bindingVersion, null);
                    return;
                }
                bool opened = await owner.Launcher.LaunchUriAsync(address);
                if (opened == false)
                {
                    ShowReleaseError(model, bindingVersion, null);
                }
            }
            catch (Exception exception)
            {
                ShowReleaseError(model, bindingVersion, exception);
            }
        }

        private bool IsCurrentBinding(AppUpdateViewModel model, int bindingVersion)
        {
            if (_attached == false)
            {
                return false;
            }
            if (bindingVersion != _bindingVersion)
            {
                return false;
            }
            if (ReferenceEquals(_boundModel, model) == false)
            {
                return false;
            }
            if (ReferenceEquals(DataContext, model) == false)
            {
                return false;
            }
            return true;
        }

        private void ShowRequestError(AppUpdateViewModel model, int bindingVersion, string key, string code, Exception exception)
        {
            if (IsCurrentBinding(model, bindingVersion) == false)
            {
                return;
            }
            ApplicationUpdateException error = exception as ApplicationUpdateException;
            if (error == null)
            {
                error = new ApplicationUpdateException(code, exception, Array.Empty<object>());
            }
            model.Error = new LocalizedText(key, new LocalizedText(error));
        }

        private void ShowReleaseError(AppUpdateViewModel model, int bindingVersion, Exception exception)
        {
            if (IsCurrentBinding(model, bindingVersion) == false)
            {
                return;
            }
            model.Error = new LocalizedText(new ApplicationUpdateException("AppUpdateReleaseOpenFailed", exception, Array.Empty<object>()));
        }
    }
}
