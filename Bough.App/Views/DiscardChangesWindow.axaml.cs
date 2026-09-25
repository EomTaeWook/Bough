using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Views
{
    public partial class DiscardChangesWindow : Window
    {
        private CancellationToken _cancellationToken;

        public DiscardChangesWindow()
        {
            InitializeComponent();
        }

        public DiscardChangesWindow(IReadOnlyList<GitDiscardPlan> plans, string title, string pathLabel, string impact, string confirmText, string cancelText)
            : this()
        {
            ArgumentNullException.ThrowIfNull(plans);
            Title = title;
            Heading.Text = title;
            PathLabel.Text = $"{pathLabel} ({plans.Count})";
            FilesList.ItemsSource = plans.Select(plan => plan.Path).ToArray();
            Impact.Text = impact;
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
