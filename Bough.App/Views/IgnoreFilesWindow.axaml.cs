using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Views
{
    public partial class IgnoreFilesWindow : Window
    {
        private CancellationToken _cancellationToken;

        public IgnoreFilesWindow()
        {
            InitializeComponent();
        }

        public IgnoreFilesWindow(GitIgnorePlan plan, string title, string description, string confirmText, string cancelText)
            : this()
        {
            ArgumentNullException.ThrowIfNull(plan);
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
            return await ShowDialog<bool>(owner);
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
            Close(true);
        }

        private void CancelClicked(object sender, RoutedEventArgs eventArgs)
        {
            Close(false);
        }
    }
}
