using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Bough.App.Localization;

namespace Bough.App.Views
{
    public partial class SidebarStatusDetailsWindow : Window
    {
        private readonly StringHelper _strings;

        public SidebarStatusDetailsWindow(string statusText, StringHelper strings)
        {
            InitializeComponent();
            _strings = strings;
            StatusMessageTextBox.Text = statusText;
            LanguageChangeBinding.Bind(this, () => _strings, RefreshLocalizedLabels);
            RefreshLocalizedLabels();
            Opened += delegate { StatusMessageTextBox.Focus(); };
        }

        private void RefreshLocalizedLabels()
        {
            Title = _strings.GetString("MainStatusDetailsTitle");
            CloseButton.Content = _strings.GetString("RemoteCloseAction");
            AutomationProperties.SetName(StatusMessageTextBox, Title);
            AutomationProperties.SetName(CloseButton, _strings.GetString("RemoteCloseAction"));
        }

        private void CloseClicked(object sender, RoutedEventArgs eventArgs)
        {
            Close();
        }
    }
}
