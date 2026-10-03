using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Views
{
    public partial class LocalChangesView : UserControl
    {
        private const double _minimumFileListWidth = 260;
        private const double _minimumPreviewWidth = 220;
        private const double _splitterWidth = 6;
        private const double _localHeaderHeight = 64;
        private const double _minimumWorkAreaHeight = 180;
        private const double _fileSectionHeaderHeight = 48;
        private const double _minimumPopulatedSectionHeight = 84;
        private const double _fileSectionsSplitterHeight = 6;
        private const double _collapsedCommitHeight = 48;
        private const double _minimumCommitHeight = 190;
        private const double _commitSplitterHeight = 6;

        private readonly LocalChangesLayoutSettings _layoutSettings;
        private readonly ColumnDefinition _fileListColumn;
        private readonly RowDefinition _stagedRow;
        private readonly RowDefinition _fileSectionsSplitterRow;
        private readonly RowDefinition _unstagedRow;
        private readonly RowDefinition _commitRow;
        private readonly RowDefinition _commitSplitterRow;
        private double _preferredFileListWidth;
        private double _preferredStagedHeightRatio;
        private double _expandedCommitHeight = _minimumCommitHeight;
        private bool _commitExpanded = true;
        private StashSaveWindow _stashWindow;
        private LocalChangesViewModel _confirmationSource;
        private LocalChangesViewModel _fileSectionsSource;
        private GitRepository _contextStagedRepository;
        private GitWorktreeFile _contextStagedFile;
        private IReadOnlyList<string> _contextDiscardPaths = Array.Empty<string>();
        private bool _stagedPointerSelection;
        private bool _contextPointerSelection;

        public event Action StashDialogOpening;

        public event Action<bool> StashDialogClosed;

        public LocalChangesView()
        {
            InitializeComponent();
            _layoutSettings = new LocalChangesLayoutSettings();
            _fileListColumn = WorkAreaGrid.ColumnDefinitions[0];
            _stagedRow = FileSectionsGrid.RowDefinitions[0];
            _fileSectionsSplitterRow = FileSectionsGrid.RowDefinitions[1];
            _unstagedRow = FileSectionsGrid.RowDefinitions[2];
            _commitSplitterRow = LayoutGrid.RowDefinitions[2];
            _commitRow = LayoutGrid.RowDefinitions[3];
            _preferredFileListWidth = _layoutSettings.LoadFileListWidth();
            _preferredStagedHeightRatio = _layoutSettings.LoadStagedHeightRatio();
            _fileListColumn.Width = new GridLength(_preferredFileListWidth);
            StagedList.AddHandler(InputElement.PointerPressedEvent, StagedPointerPressed, RoutingStrategies.Tunnel, true);
            StagedList.AddHandler(InputElement.ContextRequestedEvent, StagedContextRequested, RoutingStrategies.Tunnel, true);
            UnstagedList.AddHandler(InputElement.PointerPressedEvent, UnstagedPointerPressed, RoutingStrategies.Tunnel, true);
            UnstagedList.AddHandler(InputElement.ContextRequestedEvent, UnstagedContextRequested, RoutingStrategies.Tunnel, true);
            SizeChanged += LocalChangesSizeChanged;
            DataContextChanged += delegate { BindConfirmations(); BindFileSections(); BindContextMenuLabels(); };
            Bough.App.Localization.LanguageChangeBinding.Bind(this, () => (DataContext as LocalChangesViewModel)?.Strings, RefreshLocalizedMenuLabels);
            AttachedToVisualTree += delegate { BindConfirmations(); BindFileSections(); BindContextMenuLabels(); };
            DetachedFromVisualTree += delegate { UnbindConfirmations(); UnbindFileSections(); };
        }

        private void PreviewNumberGutterPressed(object sender, PointerPressedEventArgs eventArgs)
        {
            if (eventArgs.GetCurrentPoint(PreviewNumberGutter).Properties.IsLeftButtonPressed == false)
            {
                return;
            }

            bool extendSelection = (eventArgs.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
            PreviewCode.SelectLineAt(eventArgs.GetPosition(PreviewNumberGutter).Y, extendSelection);
        }

        private void RefreshLocalizedMenuLabels()
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }
            StagedUnstageItem.Header = viewModel.UnstageSelectedText;
            ToolTip.SetTip(StagedUnstageItem, viewModel.UnstageSelectedText);
            string discardLabel = viewModel.GetDiscardContextMenuText(viewModel.UnstagedFiles.Where(file => _contextDiscardPaths.Contains(file.Path)).ToList());
            DiscardContextItem.Header = discardLabel;
            ToolTip.SetTip(DiscardContextItem, discardLabel);
            StopTrackingContextItem.Header = viewModel.StopTrackingContextMenuText;
            IgnoreContextItem.Header = viewModel.IgnoreMenuText;
            IgnoreRepositoryItem.Header = viewModel.IgnoreRepositoryText;
            IgnoreLocalItem.Header = viewModel.IgnoreLocalText;
            ToolTip.SetTip(IgnoreRepositoryItem, viewModel.IgnoreRepositoryText);
            ToolTip.SetTip(IgnoreLocalItem, viewModel.IgnoreLocalText);
        }

        private void BindContextMenuLabels()
        {
            UnstagedDangerSeparator.IsVisible = false;
            _contextStagedRepository = null;
            _contextStagedFile = null;
            _contextDiscardPaths = Array.Empty<string>();
            _stagedPointerSelection = false;
            _contextPointerSelection = false;
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                StagedUnstageItem.IsEnabled = false;
                DiscardContextItem.IsEnabled = false;
                StopTrackingContextItem.IsVisible = false;
                IgnoreContextItem.IsEnabled = false;
                return;
            }

            StagedUnstageItem.Header = viewModel.UnstageSelectedText;
            ToolTip.SetTip(StagedUnstageItem, viewModel.UnstageSelectedText);
            StagedUnstageItem.IsEnabled = false;
            string discardLabel = viewModel.GetDiscardContextMenuText(Array.Empty<GitWorktreeFile>());
            DiscardContextItem.Header = discardLabel;
            ToolTip.SetTip(DiscardContextItem, discardLabel);
            DiscardContextItem.IsEnabled = false;
            StopTrackingContextItem.Header = viewModel.StopTrackingContextMenuText;
            StopTrackingContextItem.IsVisible = false;
            IgnoreContextItem.Header = viewModel.IgnoreMenuText;
            IgnoreContextItem.IsVisible = false;
            IgnoreRepositoryItem.Header = viewModel.IgnoreRepositoryText;
            IgnoreLocalItem.Header = viewModel.IgnoreLocalText;
            ToolTip.SetTip(IgnoreRepositoryItem, viewModel.IgnoreRepositoryText);
            ToolTip.SetTip(IgnoreLocalItem, viewModel.IgnoreLocalText);
        }

        private void BindFileSections()
        {
            UnbindFileSections();
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                UpdateFileSections(false, false);
                return;
            }

            _fileSectionsSource = viewModel;
            viewModel.PropertyChanged += FileSectionsPropertyChanged;
            UpdateFileSections(viewModel.HasStagedFiles, viewModel.HasUnstagedFiles);
        }

        private void UnbindFileSections()
        {
            if (_fileSectionsSource == null)
            {
                return;
            }

            _fileSectionsSource.PropertyChanged -= FileSectionsPropertyChanged;
            _fileSectionsSource = null;
        }

        private void FileSectionsPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            switch (eventArgs.PropertyName)
            {
                case nameof(LocalChangesViewModel.HasStagedFiles):
                case nameof(LocalChangesViewModel.HasUnstagedFiles):
                    LocalChangesViewModel viewModel = _fileSectionsSource;
                    if (viewModel == null)
                    {
                        return;
                    }

                    UpdateFileSections(viewModel.HasStagedFiles, viewModel.HasUnstagedFiles);
                    break;
            }
        }

        private void UpdateFileSections(bool hasStaged, bool hasUnstaged)
        {
            if (hasStaged == true)
            {
                _stagedRow.MinHeight = _minimumPopulatedSectionHeight;
            }
            else
            {
                _stagedRow.MinHeight = _fileSectionHeaderHeight;
            }

            if (hasUnstaged == true)
            {
                _unstagedRow.MinHeight = _minimumPopulatedSectionHeight;
            }
            else
            {
                _unstagedRow.MinHeight = _fileSectionHeaderHeight;
            }

            if (hasStaged == true)
            {
                if (hasUnstaged == true)
                {
                    _stagedRow.Height = new GridLength(_preferredStagedHeightRatio, GridUnitType.Star);
                    _unstagedRow.Height = new GridLength(1 - _preferredStagedHeightRatio, GridUnitType.Star);
                    _fileSectionsSplitterRow.Height = new GridLength(_fileSectionsSplitterHeight);
                    FileSectionsSplitter.IsVisible = true;
                    return;
                }

                _stagedRow.Height = new GridLength(1, GridUnitType.Star);
                _unstagedRow.Height = new GridLength(_fileSectionHeaderHeight);
            }
            else if (hasUnstaged == true)
            {
                _stagedRow.Height = new GridLength(_fileSectionHeaderHeight);
                _unstagedRow.Height = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                _stagedRow.Height = new GridLength(_fileSectionHeaderHeight);
                _unstagedRow.Height = new GridLength(_fileSectionHeaderHeight);
            }

            _fileSectionsSplitterRow.Height = new GridLength(0);
            FileSectionsSplitter.IsVisible = false;
        }

        private void FileSectionsSplitterDragCompleted(object sender, VectorEventArgs eventArgs)
        {
            SaveStagedHeightRatio();
        }

        private void FileSectionsSplitterKeyUp(object sender, KeyEventArgs eventArgs)
        {
            switch (eventArgs.Key)
            {
                case Key.Up:
                case Key.Down:
                    SaveStagedHeightRatio();
                    break;
            }
        }

        private void SaveStagedHeightRatio()
        {
            double totalHeight = _stagedRow.ActualHeight + _unstagedRow.ActualHeight;
            if (totalHeight <= 0)
            {
                return;
            }

            _preferredStagedHeightRatio = _stagedRow.ActualHeight / totalHeight;
            _layoutSettings.SaveStagedHeightRatio(_preferredStagedHeightRatio);
        }

        private void BindConfirmations()
        {
            UnbindConfirmations();
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }

            _confirmationSource = viewModel;
            viewModel.ConfirmLargeFilesRequested += ConfirmLargeFilesAsync;
            viewModel.ConfirmDiscardRequested += ConfirmDiscardAsync;
            viewModel.ConfirmStopTrackingRequested += ConfirmStopTrackingAsync;
            viewModel.ConfirmIgnoreRequested += ConfirmIgnoreAsync;
        }

        private void UnbindConfirmations()
        {
            if (_confirmationSource == null)
            {
                return;
            }

            _confirmationSource.ConfirmLargeFilesRequested -= ConfirmLargeFilesAsync;
            _confirmationSource.ConfirmDiscardRequested -= ConfirmDiscardAsync;
            _confirmationSource.ConfirmStopTrackingRequested -= ConfirmStopTrackingAsync;
            _confirmationSource.ConfirmIgnoreRequested -= ConfirmIgnoreAsync;
            _confirmationSource = null;
        }

        private async Task<bool> ConfirmLargeFilesAsync(IReadOnlyList<GitLargeFileCandidate> files, CancellationToken cancellationToken)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return false;
            }

            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return false;
            }

            LargeFileStageWindow window = new(files, viewModel.LargeStageTitleText, viewModel.GetLargeStageDescription(files.Count), viewModel.LargeStageContinueText, viewModel.LargeStageCancelText);
            return await window.ShowForAsync(owner, cancellationToken);
        }

        private async Task<bool> ConfirmDiscardAsync(IReadOnlyList<GitDiscardPlan> plans, CancellationToken cancellationToken)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return false;
            }

            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return false;
            }

            DiscardChangesWindow window = new(plans, viewModel.GetDiscardTitleText(plans), viewModel.DiscardPathLabelText, viewModel.GetDiscardImpactText(plans), viewModel.GetDiscardConfirmText(plans), viewModel.DiscardCancelText);
            StashDialogOpening?.Invoke();
            try
            {
                return await window.ShowForAsync(owner, cancellationToken);
            }
            finally
            {
                StashDialogClosed?.Invoke(false);
            }
        }

        private async Task<bool> ConfirmStopTrackingAsync(IReadOnlyList<GitDiscardPlan> plans, CancellationToken cancellationToken)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return false;
            }

            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return false;
            }

            DiscardChangesWindow window = new(plans, viewModel.StopTrackingTitleText, viewModel.DiscardPathLabelText, viewModel.GetStopTrackingDescription(plans.Count), viewModel.StopTrackingConfirmText, viewModel.DiscardCancelText);
            StashDialogOpening?.Invoke();
            try
            {
                return await window.ShowForAsync(owner, cancellationToken);
            }
            finally
            {
                StashDialogClosed?.Invoke(false);
            }
        }

        private async Task<bool> ConfirmIgnoreAsync(GitIgnorePlan plan, CancellationToken cancellationToken)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return false;
            }

            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return false;
            }

            IgnoreFilesWindow window = new(plan, viewModel.IgnoreConfirmTitleText, viewModel.GetIgnoreDescription(plan), viewModel.IgnoreConfirmButtonText, viewModel.DiscardCancelText);
            StashDialogOpening?.Invoke();
            try
            {
                return await window.ShowForAsync(owner, cancellationToken);
            }
            finally
            {
                StashDialogClosed?.Invoke(false);
            }
        }

        private void UnstagedSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }

            viewModel.SetUnstagedSelection(UnstagedList.SelectedItems.OfType<GitWorktreeFile>());
        }

        private void UnstagedKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key != Key.A)
            {
                return;
            }

            if ((eventArgs.KeyModifiers & KeyModifiers.Control) == 0)
            {
                return;
            }

            foreach (GitWorktreeFile file in UnstagedList.ItemsSource.OfType<GitWorktreeFile>())
            {
                if (UnstagedList.SelectedItems.Contains(file) == false)
                {
                    UnstagedList.SelectedItems.Add(file);
                }
            }

            eventArgs.Handled = true;
        }

        private void StagedPointerPressed(object sender, PointerPressedEventArgs eventArgs)
        {
            if (eventArgs.GetCurrentPoint(this).Properties.IsRightButtonPressed == false)
            {
                _stagedPointerSelection = false;
                return;
            }

            _stagedPointerSelection = true;
            _contextStagedFile = null;
            if (eventArgs.Source is not Control source)
            {
                return;
            }

            ListBoxItem item = source as ListBoxItem;
            if (item == null)
            {
                item = source.FindAncestorOfType<ListBoxItem>();
            }

            if (item?.DataContext is not GitWorktreeFile file)
            {
                return;
            }

            _contextStagedFile = file;
            StagedList.SelectedItem = file;
        }

        private void StagedContextOpened(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                StagedUnstageItem.IsEnabled = false;
                return;
            }

            if (_stagedPointerSelection == false)
            {
                _contextStagedFile = viewModel.SelectedStagedFile;
            }

            _stagedPointerSelection = false;
            _contextStagedRepository = viewModel.CurrentRepository;
            StagedUnstageItem.Header = viewModel.UnstageSelectedText;
            ToolTip.SetTip(StagedUnstageItem, viewModel.UnstageSelectedText);
            if (_contextStagedFile == null)
            {
                StagedUnstageItem.IsEnabled = false;
                return;
            }

            StagedUnstageItem.IsEnabled = viewModel.StagedFiles.Contains(_contextStagedFile);
        }

        private void StagedContextRequested(object sender, ContextRequestedEventArgs eventArgs)
        {
            StagedContextOpened(sender, eventArgs);
        }

        private async void UnstageContextClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }

            GitRepository repository = _contextStagedRepository;
            GitWorktreeFile file = _contextStagedFile;
            if (repository == null)
            {
                return;
            }

            if (file == null)
            {
                return;
            }

            await viewModel.UnstageFileAsync(repository, file);
        }

        private void UnstagedPointerPressed(object sender, PointerPressedEventArgs eventArgs)
        {
            if (eventArgs.GetCurrentPoint(this).Properties.IsRightButtonPressed == false)
            {
                _contextPointerSelection = false;
                return;
            }

            _contextPointerSelection = true;
            _contextDiscardPaths = Array.Empty<string>();
            if (eventArgs.Source is not Control source)
            {
                return;
            }

            ListBoxItem item = source as ListBoxItem;
            if (item == null)
            {
                item = source.FindAncestorOfType<ListBoxItem>();
            }

            if (item?.DataContext is not GitWorktreeFile file)
            {
                return;
            }

            if (UnstagedList.SelectedItems.Contains(file) == true)
            {
                _contextDiscardPaths = UnstagedList.SelectedItems.OfType<GitWorktreeFile>().Select(selected => selected.Path).ToArray();
                return;
            }

            UnstagedList.SelectedItems.Clear();
            UnstagedList.SelectedItems.Add(file);
            _contextDiscardPaths = new string[] { file.Path };
        }

        private void UnstagedContextOpened(object sender, RoutedEventArgs eventArgs)
        {
            UnstagedDangerSeparator.IsVisible = false;
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                DiscardContextItem.IsEnabled = false;
                StopTrackingContextItem.IsVisible = false;
                IgnoreContextItem.IsVisible = false;
                return;
            }

            bool preservePointerSelection = _contextPointerSelection;
            if (preservePointerSelection == false)
            {
                _contextDiscardPaths = UnstagedList.SelectedItems.OfType<GitWorktreeFile>().Select(file => file.Path).ToArray();
            }

            _contextPointerSelection = false;
            HashSet<string> paths = new(_contextDiscardPaths, StringComparer.Ordinal);
            GitWorktreeFile[] files = viewModel.UnstagedFiles.Where(file => paths.Contains(file.Path)).ToArray();
            _contextDiscardPaths = files.Select(file => file.Path).ToArray();
            if (preservePointerSelection == true)
            {
                if (files.Length > 0)
                {
                    UnstagedList.SelectedItems.Clear();
                    foreach (GitWorktreeFile file in files)
                    {
                        UnstagedList.SelectedItems.Add(file);
                    }
                }
            }

            string label = viewModel.GetDiscardContextMenuText(files);
            DiscardContextItem.Header = label;
            DiscardContextItem.IsEnabled = _contextDiscardPaths.Count > 0;
            ToolTip.SetTip(DiscardContextItem, label);
            bool canStopTracking = viewModel.CanStopTrackingPaths(_contextDiscardPaths);
            string stopTrackingLabel = viewModel.StopTrackingContextMenuText;
            StopTrackingContextItem.Header = stopTrackingLabel;
            StopTrackingContextItem.IsVisible = canStopTracking;
            StopTrackingContextItem.IsEnabled = canStopTracking;
            ToolTip.SetTip(StopTrackingContextItem, stopTrackingLabel);
            IgnoreContextItem.Header = viewModel.IgnoreMenuText;
            bool canIgnore = viewModel.CanIgnorePaths(_contextDiscardPaths);
            IgnoreContextItem.IsVisible = canIgnore;
            IgnoreContextItem.IsEnabled = canIgnore;
            UnstagedDangerSeparator.IsVisible = canIgnore;
            IgnoreRepositoryItem.Header = viewModel.IgnoreRepositoryText;
            IgnoreLocalItem.Header = viewModel.IgnoreLocalText;
            ToolTip.SetTip(IgnoreRepositoryItem, viewModel.IgnoreRepositoryText);
            ToolTip.SetTip(IgnoreLocalItem, viewModel.IgnoreLocalText);
        }

        private void UnstagedContextRequested(object sender, ContextRequestedEventArgs eventArgs)
        {
            UnstagedContextOpened(sender, eventArgs);
        }

        private async void DiscardContextClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }

            if (_contextDiscardPaths.Count == 0)
            {
                return;
            }

            await viewModel.DiscardPathsAsync(_contextDiscardPaths);
        }

        private async void StopTrackingContextClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }

            if (viewModel.CanStopTrackingPaths(_contextDiscardPaths) == false)
            {
                return;
            }

            await viewModel.StopTrackingPathsAsync(_contextDiscardPaths);
        }

        private async void IgnoreRepositoryClicked(object sender, RoutedEventArgs eventArgs)
        {
            await IgnoreContextAsync(GitIgnoreLocation.Repository);
        }

        private async void IgnoreLocalClicked(object sender, RoutedEventArgs eventArgs)
        {
            await IgnoreContextAsync(GitIgnoreLocation.Local);
        }

        private async Task IgnoreContextAsync(GitIgnoreLocation location)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }

            if (viewModel.CanIgnorePaths(_contextDiscardPaths) == false)
            {
                return;
            }

            await viewModel.IgnorePathsAsync(_contextDiscardPaths, location);
        }

        private void WorkAreaSizeChanged(object sender, SizeChangedEventArgs eventArgs)
        {
            double maximumWidth = WorkAreaGrid.Bounds.Width - _splitterWidth - _minimumPreviewWidth;
            double width = Math.Max(_minimumFileListWidth, Math.Min(_preferredFileListWidth, maximumWidth));
            if (Math.Abs(_fileListColumn.ActualWidth - width) > 0.5)
            {
                _fileListColumn.Width = new GridLength(width);
            }
        }

        private void FileListSplitterDragCompleted(object sender, VectorEventArgs eventArgs)
        {
            SaveFileListWidth();
        }

        private void FileListSplitterKeyUp(object sender, KeyEventArgs eventArgs)
        {
            switch (eventArgs.Key)
            {
                case Key.Left:
                case Key.Right:
                    SaveFileListWidth();
                    break;
            }
        }

        private void SaveFileListWidth()
        {
            _preferredFileListWidth = _fileListColumn.ActualWidth;
            _layoutSettings.SaveFileListWidth(_preferredFileListWidth);
        }

        private void CommitSplitterDragCompleted(object sender, VectorEventArgs eventArgs)
        {
            SaveCommitHeight();
        }

        private void CommitSplitterKeyUp(object sender, KeyEventArgs eventArgs)
        {
            switch (eventArgs.Key)
            {
                case Key.Up:
                case Key.Down:
                    SaveCommitHeight();
                    break;
            }
        }

        private void SaveCommitHeight()
        {
            if (_commitExpanded == false)
            {
                return;
            }

            _expandedCommitHeight = Math.Max(_minimumCommitHeight, _commitRow.ActualHeight);
            UpdateLayoutMinimumHeight(Bounds.Height);
        }

        private void LocalChangesSizeChanged(object sender, SizeChangedEventArgs eventArgs)
        {
            UpdateLayoutMinimumHeight(eventArgs.NewSize.Height);
        }

        private void UpdateLayoutMinimumHeight(double availableHeight)
        {
            double commitHeight = _collapsedCommitHeight;
            double splitterHeight = 0;
            if (_commitExpanded == true)
            {
                commitHeight = Math.Max(_minimumCommitHeight, _expandedCommitHeight);
                splitterHeight = _commitSplitterHeight;
            }

            double minimumHeight = _localHeaderHeight + _minimumWorkAreaHeight + splitterHeight + commitHeight;
            LayoutGrid.Height = Math.Max(minimumHeight, availableHeight);
        }

        private void CommitToggleClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_commitExpanded == true)
            {
                SaveCommitHeight();
                _commitExpanded = false;
                CommitBody.IsVisible = false;
                CommitSplitter.IsVisible = false;
                _commitSplitterRow.Height = new GridLength(0);
                _commitRow.MinHeight = _collapsedCommitHeight;
                _commitRow.Height = new GridLength(_collapsedCommitHeight);
                CommitToggle.Content = "⌃";
                UpdateLayoutMinimumHeight(Bounds.Height);
                return;
            }

            _commitExpanded = true;
            _commitRow.MinHeight = _minimumCommitHeight;
            _commitRow.Height = new GridLength(Math.Max(_minimumCommitHeight, _expandedCommitHeight));
            _commitSplitterRow.Height = new GridLength(_commitSplitterHeight);
            CommitSplitter.IsVisible = true;
            CommitBody.IsVisible = true;
            CommitToggle.Content = "⌄";
            UpdateLayoutMinimumHeight(Bounds.Height);
        }

        private async void OpenStashClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not LocalChangesViewModel viewModel)
            {
                return;
            }

            if (viewModel.HasRepository == false)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            if (_stashWindow != null)
            {
                _stashWindow.Activate();
                return;
            }

            if (await viewModel.PrepareStashSaveAsync() == false)
            {
                return;
            }

            StashSaveWindow window = new(viewModel.Stashes);
            _stashWindow = window;
            bool saved = false;
            try
            {
                StashDialogOpening?.Invoke();
                saved = await window.ShowDialog<bool>(owner);
            }
            finally
            {
                _stashWindow = null;
                StashDialogClosed?.Invoke(saved);
            }
        }
    }
}
