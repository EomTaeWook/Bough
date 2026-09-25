using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;

namespace Bough.App.Views
{
    public partial class StashSaveWindow : Window
    {
        private int _pendingSaves;
        private bool _saved;
        private bool _saveFailed;

        public StashSaveWindow()
        {
            InitializeComponent();
        }

        public StashSaveWindow(StashViewModel viewModel)
            : this()
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            DataContext = viewModel;
            Opened += OnOpened;
        }

        private void OnOpened(object sender, System.EventArgs eventArgs)
        {
            MessageInput.Focus();
        }

        private async void SaveClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not StashViewModel viewModel)
            {
                return;
            }

            if (viewModel.CanSaveStash == false)
            {
                return;
            }

            IStashMutationCompletion completion = Owner?.DataContext as IStashMutationCompletion;
            _pendingSaves++;
            try
            {
                StashMutationResult result = await viewModel.SaveAsync();
                if (completion != null)
                {
                    await completion.CompleteStashSaveAsync(result);
                }

                if (result.Succeeded == true)
                {
                    _saved = true;
                }
                else
                {
                    _saveFailed = true;
                }
            }
            finally
            {
                _pendingSaves--;
            }

            if (_pendingSaves == 0)
            {
                if (_saved == true)
                {
                    if (_saveFailed == false)
                    {
                        Close(true);
                    }
                }
            }
        }

        protected override void OnClosing(WindowClosingEventArgs eventArgs)
        {
            if (DataContext is not StashViewModel viewModel)
            {
                base.OnClosing(eventArgs);
                return;
            }

            if (_pendingSaves > 0)
            {
                eventArgs.Cancel = true;
                return;
            }

            if (viewModel.IsBusy == true)
            {
                eventArgs.Cancel = true;
                return;
            }

            base.OnClosing(eventArgs);
        }

        private void CancelClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not StashViewModel viewModel)
            {
                return;
            }

            if (_pendingSaves > 0)
            {
                return;
            }

            if (viewModel.IsBusy == true)
            {
                return;
            }

            Close(_saved);
        }
    }
}
