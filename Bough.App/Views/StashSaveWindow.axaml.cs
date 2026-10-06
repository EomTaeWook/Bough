using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Bough.App.Interfaces;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;

namespace Bough.App.Views
{
    public partial class StashSaveWindow : Window
    {
        private readonly StashSaveDialogState _state = new();
        private readonly StashSavePresenter _presenter;
        private readonly StashViewModel _viewModel;

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

            _viewModel = viewModel;
            _presenter = new StashSavePresenter(viewModel, _state);
            DataContext = viewModel;
            _state.PropertyChanged += DisplayStateChanged;
            viewModel.PropertyChanged += DisplayStateChanged;
            Opened += OnOpened;
            Closed += OnClosed;
            LanguageChangeBinding.Bind(this, () => viewModel.Strings, RefreshDisplay);
            RefreshDisplay();
        }

        public async Task<bool> ShowForAsync(Window owner)
        {
            await ShowDialog<bool>(owner);
            return _state.Saved;
        }

        private void OnOpened(object sender, EventArgs eventArgs)
        {
            MessageInput.Focus();
        }

        private void OnClosed(object sender, EventArgs eventArgs)
        {
            _state.PropertyChanged -= DisplayStateChanged;
            _viewModel.PropertyChanged -= DisplayStateChanged;
        }

        private void DisplayStateChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_viewModel == null)
            {
                return;
            }

            List<string> errors = [];
            if (_state.SaveException != null)
            {
                errors.Add(new LocalizedText(_state.SaveException).GetText(_viewModel.Strings));
            }
            else if (string.IsNullOrEmpty(_state.SaveErrorText) == false)
            {
                errors.Add(_state.SaveErrorText);
            }
            foreach (StashSaveRefreshFailure failure in _state.RefreshFailures)
            {
                if (failure.Exception != null)
                {
                    errors.Add(new LocalizedText(failure.Exception).GetText(_viewModel.Strings));
                }
                else
                {
                    errors.Add(failure.Result.ErrorText);
                }
            }
            if (errors.Count == 0)
            {
                if (string.IsNullOrEmpty(_viewModel.ErrorText) == false)
                {
                    errors.Add(_viewModel.ErrorText);
                }
            }

            SaveError.Text = string.Join(Environment.NewLine, errors);
            SavedRefreshNotice.Text = _viewModel.Strings.GetString("StashSavedRefreshFailed");
            SavedRefreshNotice.IsVisible = _state.HasRefreshFailures;
            RefreshProgress.Text = _viewModel.Strings.GetString("StashSaveRefreshInProgress");
            RefreshProgress.IsVisible = _state.IsRefreshing;
            ResultArea.IsVisible = errors.Count > 0;
            if (_state.IsRefreshing == true)
            {
                ResultArea.IsVisible = true;
            }
            if (_state.HasRefreshFailures == true)
            {
                ResultArea.IsVisible = true;
            }
            RetryRefreshButton.Content = _viewModel.Strings.GetString("StashSaveRefreshRetry");
            RetryRefreshButton.IsVisible = _state.HasRefreshFailures;
            RetryRefreshButton.IsEnabled = _state.CanRetry;
            CancelButton.IsEnabled = _state.CanClose;
            SaveButton.IsEnabled = _state.CanSave;
            if (_viewModel.CanSaveStash == false)
            {
                SaveButton.IsEnabled = false;
            }
        }

        private async void SaveClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_presenter == null)
            {
                return;
            }
            await _presenter.SaveAsync(Owner?.DataContext as IStashMutationCompletion);
            CloseIfCompleted();
        }

        private async void RetryRefreshClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_presenter == null)
            {
                return;
            }
            await _presenter.RetryRefreshAsync(Owner?.DataContext as IStashMutationCompletion);
            CloseIfCompleted();
        }

        private void CloseIfCompleted()
        {
            if (_state.ShouldClose == false)
            {
                return;
            }
            Close(_state.Saved);
        }

        protected override void OnClosing(WindowClosingEventArgs eventArgs)
        {
            if (_state.CanClose == false)
            {
                eventArgs.Cancel = true;
                return;
            }
            if (_viewModel?.IsBusy == true)
            {
                eventArgs.Cancel = true;
                return;
            }
            base.OnClosing(eventArgs);
        }

        private void CancelClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_state.CanClose == false)
            {
                return;
            }

            if (_viewModel?.IsBusy == true)
            {
                return;
            }

            Close(_state.Saved);
        }
    }
}
