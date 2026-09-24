using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bough.Core.Git;

namespace Bough.App.Views
{
    public partial class LargeFileStageWindow : Window
    {
        private CancellationToken _cancellationToken;

        public LargeFileStageWindow()
        {
            InitializeComponent();
        }

        public LargeFileStageWindow(IReadOnlyList<GitLargeFileCandidate> files, string title, string description, string continueText, string cancelText)
            : this()
        {
            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            Title = title;
            Heading.Text = title;
            Description.Text = description;
            ContinueButton.Content = continueText;
            CancelButton.Content = cancelText;
            FilesList.ItemsSource = files;
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

        private void ContinueClicked(object sender, RoutedEventArgs eventArgs)
        {
            Close(true);
        }

        private void CancelClicked(object sender, RoutedEventArgs eventArgs)
        {
            Close(false);
        }
    }
}
