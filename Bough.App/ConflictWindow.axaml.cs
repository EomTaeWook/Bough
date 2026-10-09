using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.App.Localization;
using Bough.App.Internals;
using Bough.App.Views;
using Bough.Core.Internals;

namespace Bough.App
{
    public partial class ConflictWindow : Window
    {
        private readonly ConflictResolutionViewModel _viewModel;
        private readonly Func<Task> _refreshRepository;
        private readonly StringHelper _strings;
        private readonly GitErrorLocalizer _errors;
        private bool _closeConfirmed;
        private bool _confirmationPending;

        public ConflictWindow()
        {
            InitializeComponent();
            LanguageChangeBinding.Bind(this, () => _strings, UpdateStageStatus);
            Closing += async (sender, eventArgs) =>
            {
                if (_viewModel == null)
                {
                    return;
                }
                if (_closeConfirmed == true)
                {
                    return;
                }
                if (_viewModel.HasUnsavedConflictEdits == false)
                {
                    return;
                }

                eventArgs.Cancel = true;
                if (_confirmationPending == true)
                {
                    return;
                }

                string draftText = _viewModel.ResultText;
                bool draftDeleted = _viewModel.IsResultFileDeleted;
                _confirmationPending = true;
                bool discard;
                try
                {
                    discard = await GitActionDialogs.ConfirmAsync(this,
                        _viewModel.DiscardResolutionTitle,
                        _viewModel.DiscardResolutionCloseMessage,
                        _viewModel.DiscardResolutionConfirmText, _viewModel.Strings);
                }
                finally
                {
                    _confirmationPending = false;
                }
                if (draftText != _viewModel.ResultText)
                {
                    return;
                }
                if (draftDeleted != _viewModel.IsResultFileDeleted)
                {
                    return;
                }
                if (discard == true)
                {
                    CloseAfterConfirmation();
                }
            };
            KeyDown += async (sender, eventArgs) =>
            {
                if (eventArgs.Key != Key.F5)
                {
                    return;
                }
                eventArgs.Handled = true;
                if (_refreshRepository != null)
                {
                    await _refreshRepository();
                }
            };
        }

        public ConflictWindow(ConflictResolutionViewModel viewModel, Func<Task> refreshRepository,
            StringHelper strings, GitErrorLocalizer errors)
            : this()
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }
            if (refreshRepository == null)
            {
                throw new ArgumentNullException(nameof(refreshRepository));
            }
            if (strings == null)
            {
                throw new ArgumentNullException(nameof(strings));
            }
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }
            _viewModel = viewModel;
            _refreshRepository = refreshRepository;
            _strings = strings;
            _errors = errors;
            DataContext = viewModel;
            _viewModel.ConfirmFileChangeAsync = ConfirmFileChangeAsync;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Closed += delegate
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.ConfirmFileChangeAsync = null;
            };
            UpdateStageStatus();
        }

        private Task<bool> ConfirmFileChangeAsync(ConflictFileItem file)
        {
            return GitActionDialogs.ConfirmAsync(this, _viewModel.DiscardResolutionTitle,
                _strings.Format("DiscardResolutionSwitchFileMessage", file.RelativePath),
                _viewModel.DiscardResolutionConfirmText, _strings);
        }

        private async void SaveAndStageClicked(object sender, RoutedEventArgs eventArgs)
        {
            await _viewModel.SaveAndStageAsync(_strings.GetString("SaveAndStage"));
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(ConflictResolutionViewModel.BatchStageResult))
            {
                UpdateBatchStageStatus();
                return;
            }
            if (eventArgs.PropertyName == nameof(ConflictResolutionViewModel.StageResult))
            {
                UpdateStageStatus();
                return;
            }
        }

        private void UpdateStageStatus()
        {
            UpdateBatchStageStatus();
            if (_viewModel == null)
            {
                return;
            }
            ConflictStageResult result = _viewModel.StageResult;
            if (result == null)
            {
                StageStatusBlock.Text = string.Empty;
                return;
            }
            string message = GetStageMessage(result);
            if (result.RefreshException != null)
            {
                message = $"{message} {_errors.GetDisplayMessage(result.RefreshException)}";
            }
            StageStatusBlock.Text = message;
        }

        private void UpdateBatchStageStatus()
        {
            if (_viewModel == null)
            {
                return;
            }
            ConflictBatchStageResult result = _viewModel.BatchStageResult;
            if (result == null)
            {
                BatchStageStatusBlock.Text = string.Empty;
                return;
            }
            List<string> lines = new()
            {
                _strings.Format("ConflictBatchStageSummary", result.StagedCount, result.FailedCount, result.Excluded.Count)
            };
            foreach (ConflictStageResult file in result.Files)
            {
                lines.Add(_strings.Format("ConflictBatchStageFileResult", file.Path, GetStageMessage(file)));
            }
            foreach (KeyValuePair<string, LocalizedText> file in result.Excluded)
            {
                lines.Add(_strings.Format("ConflictBatchStageFileResult", file.Key, file.Value.GetText(_strings)));
            }
            if (result.RefreshException != null)
            {
                lines.Add(_strings.Format("ConflictBatchStageRefreshFailed", _errors.GetDisplayMessage(result.RefreshException)));
            }
            BatchStageStatusBlock.Text = string.Join(Environment.NewLine, lines);
        }

        private string GetStageMessage(ConflictStageResult result)
        {
            switch (result.Outcome)
            {
                case ConflictStageOutcome.Succeeded:
                    return _strings.Format("FileStaged", result.Path);
                case ConflictStageOutcome.NoLongerConflicted:
                    return _strings.Format("ConflictStageNoLongerConflicted", result.Path);
                case ConflictStageOutcome.FileChanged:
                    return _strings.Format("ConflictStageFileChanged", result.Path);
                case ConflictStageOutcome.IncompleteResolution:
                    return _strings.GetString("IncompleteConflictMarkers");
                case ConflictStageOutcome.UnresolvedResolution:
                    return _strings.GetString("UnresolvedConflictMarkers");
                case ConflictStageOutcome.Canceled:
                    return _strings.GetString("ConflictStageCanceled");
                case ConflictStageOutcome.RefreshFailed:
                    return _strings.Format("ConflictStageRefreshFailed", result.Path,
                        _errors.GetDisplayMessage(result.Exception));
                case ConflictStageOutcome.Failed:
                    if (result.MessageCode != null)
                    {
                        return _strings.GetString(result.MessageCode);
                    }
                    if (result.Exception != null)
                    {
                        return _errors.GetDisplayMessage(result.Exception);
                    }
                    return _strings.GetString("ConflictStageUnavailable");
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }
        }

        public void CloseAfterConfirmation()
        {
            _closeConfirmed = true;
            Close();
        }

        private async void ApplyRemainingOurs(object sender, RoutedEventArgs eventArgs)
        {
            await ApplyRemainingAsync(ResolutionChoiceType.Ours);
        }

        private async void ApplyRemainingTheirs(object sender, RoutedEventArgs eventArgs)
        {
            await ApplyRemainingAsync(ResolutionChoiceType.Theirs);
        }

        private async void ApplyRemainingBoth(object sender, RoutedEventArgs eventArgs)
        {
            await ApplyRemainingAsync(ResolutionChoiceType.Both);
        }

        private async void ApplyRemainingRemove(object sender, RoutedEventArgs eventArgs)
        {
            await ApplyRemainingAsync(ResolutionChoiceType.Remove);
        }

        private async Task ApplyRemainingAsync(ResolutionChoiceType choice)
        {
            if (_viewModel == null)
            {
                return;
            }

            await _viewModel.ApplyRemainingAndStageAsync(choice, _viewModel.ConflictBatchApplyButtonText);
        }
    }
}
