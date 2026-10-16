using System;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Bough.App.Localization;

namespace Bough.App.Views
{
    public partial class SidebarStatusDetailsWindow : Window
    {
        private readonly StringHelper _strings;
        private readonly string _statusText;
        private bool _copyFailed;

        public SidebarStatusDetailsWindow(string statusText, StringHelper strings, bool copyFailed)
        {
            InitializeComponent();
            _statusText = statusText;
            _strings = strings;
            _copyFailed = copyFailed;
            StatusMessageTextBox.Text = _statusText;
            LanguageChangeBinding.Bind(this, () => _strings, RefreshLocalizedLabels);
            RefreshLocalizedLabels();
            Opened += delegate { StatusMessageTextBox.Focus(); };
        }

        private void RefreshLocalizedLabels()
        {
            Title = _strings.GetString("MainStatusDetailsTitle");
            CopyButton.Content = _strings.GetString("HistoryCopy");
            CloseButton.Content = _strings.GetString("RemoteCloseAction");
            CopyFailureText.Text = _strings.GetString("MainStatusCopyFailed");
            CopyFailureText.IsVisible = _copyFailed;
            AutomationProperties.SetName(StatusMessageTextBox, Title);
            AutomationProperties.SetName(CopyButton, _strings.GetString("HistoryCopy"));
            AutomationProperties.SetName(CloseButton, _strings.GetString("RemoteCloseAction"));
        }

        private async void CopyClicked(object sender, RoutedEventArgs eventArgs)
        {
            CopyButton.IsEnabled = false;
            bool copyFailed = false;
            try
            {
                IClipboard clipboard = Clipboard;
                if (clipboard == null)
                {
                    copyFailed = true;
                }
                else
                {
                    await clipboard.SetTextAsync(_statusText);
                }
            }
            catch (Exception)
            {
                copyFailed = true;
            }
            finally
            {
                if (IsVisible == true)
                {
                    CopyButton.IsEnabled = true;
                }
            }
            if (IsVisible == false)
            {
                return;
            }
            _copyFailed = copyFailed;
            RefreshLocalizedLabels();
            if (_copyFailed == true)
            {
                StatusMessageTextBox.Focus();
            }
        }

        private void CloseClicked(object sender, RoutedEventArgs eventArgs)
        {
            Close();
        }
    }
}
