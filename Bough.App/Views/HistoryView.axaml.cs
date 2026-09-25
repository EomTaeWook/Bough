using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Views
{
    public partial class HistoryView : UserControl
    {
        private string _menuCommitHash;
        private string _menuRepositoryRoot;
        private string _menuFilePath;
        private bool _menuFileDeleted;
        private ScrollViewer _commitScrollViewer;
        private HistoryViewModel _lastAutoViewModel;
        private int _lastAutoListVersion = -1;
        private int _lastAutoCount = -1;
        private HistoryViewModel _layoutViewModel;
        private bool _commitListHeightUserSet;
        private bool _commitListHeightUpdateQueued;

        public HistoryView()
        {
            InitializeComponent();
            AddHandler(TreeViewItem.ExpandedEvent, TreeExpanded);
            HistoryCommitList.TemplateApplied += (_, _) => AttachCommitScrollViewer();
            DataContextChanged += (_, _) => AttachLayoutViewModel();
            AttachedToVisualTree += (_, _) =>
            {
                AttachCommitScrollViewer();
                AttachLayoutViewModel();
                ScheduleCommitListHeightUpdate();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                DetachCommitScrollViewer();
                DetachLayoutViewModel();
            };
        }

        private void AttachLayoutViewModel()
        {
            if (_layoutViewModel == DataContext)
            {
                return;
            }

            DetachLayoutViewModel();
            _layoutViewModel = DataContext as HistoryViewModel;
            if (_layoutViewModel == null)
            {
                return;
            }

            _layoutViewModel.PropertyChanged += LayoutViewModelPropertyChanged;
            ScheduleCommitListHeightUpdate();
        }

        private void DetachLayoutViewModel()
        {
            if (_layoutViewModel == null)
            {
                return;
            }

            _layoutViewModel.PropertyChanged -= LayoutViewModelPropertyChanged;
            _layoutViewModel = null;
        }

        private void LayoutViewModelPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(HistoryViewModel.ListVersion))
            {
                return;
            }

            ScheduleCommitListHeightUpdate();
        }

        private void ScheduleCommitListHeightUpdate()
        {
            if (_commitListHeightUserSet)
            {
                return;
            }
            if (_commitListHeightUpdateQueued)
            {
                return;
            }

            _commitListHeightUpdateQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _commitListHeightUpdateQueued = false;
                UpdateCommitListHeight();
            }, DispatcherPriority.Background);
        }

        private void UpdateCommitListHeight()
        {
            if (_commitListHeightUserSet)
            {
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }

            double rowCount = Math.Max(1, viewModel.Commits.Count);
            double height = Math.Clamp(16 + rowCount * 31, 64, 300);
            double available = HistorySplitGrid.Bounds.Height - 318;
            if (available >= 64)
            {
                height = Math.Min(height, available);
            }

            HistorySplitGrid.RowDefinitions[0].Height = new GridLength(height, GridUnitType.Pixel);
        }

        private void HistorySplitGridSizeChanged(object sender, SizeChangedEventArgs eventArgs)
        {
            ScheduleCommitListHeightUpdate();
        }

        private void CommitSplitterPointerPressed(object sender, PointerPressedEventArgs eventArgs)
        {
            _commitListHeightUserSet = true;
        }

        private void CommitSplitterKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key == Key.Up || eventArgs.Key == Key.Down)
            {
                _commitListHeightUserSet = true;
            }
        }

        private void ExternalAuthorPhotosClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not CheckBox checkBox)
            {
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            viewModel.ExternalAuthorPhotosEnabled = checkBox.IsChecked == true;
        }

        private async void AllScopeClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (viewModel.IsAllScope)
            {
                return;
            }
            GitRepository repository = viewModel.CurrentRepository;
            ResetCommitScroll();
            await viewModel.SetScopeAsync(GitHistoryScope.All);
            if (viewModel.CurrentRepository == repository && viewModel.IsAllScope)
            {
                ResetCommitScroll();
            }
        }

        private async void CurrentBranchScopeClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (viewModel.IsCurrentBranchScope)
            {
                return;
            }
            GitRepository repository = viewModel.CurrentRepository;
            ResetCommitScroll();
            await viewModel.SetScopeAsync(GitHistoryScope.CurrentBranch);
            if (viewModel.CurrentRepository == repository && viewModel.IsCurrentBranchScope)
            {
                ResetCommitScroll();
            }
        }

        private void ResetCommitScroll()
        {
            if (_commitScrollViewer == null)
            {
                return;
            }
            _commitScrollViewer.Offset = new Vector(0, 0);
        }

        private void AttachCommitScrollViewer()
        {
            ScrollViewer viewer = HistoryCommitList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (viewer == _commitScrollViewer)
            {
                return;
            }
            DetachCommitScrollViewer();
            _commitScrollViewer = viewer;
            if (viewer != null)
            {
                viewer.ScrollChanged += CommitScrollChanged;
            }
        }

        private void DetachCommitScrollViewer()
        {
            if (_commitScrollViewer == null)
            {
                return;
            }
            _commitScrollViewer.ScrollChanged -= CommitScrollChanged;
            _commitScrollViewer = null;
        }

        private void CommitScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
        {
            if (sender is not ScrollViewer viewer)
            {
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (viewModel.CurrentRepository == null)
            {
                return;
            }
            if (viewModel.LoadMoreCommand.CanExecute(null) == false)
            {
                return;
            }
            if (eventArgs.OffsetDelta.Y <= 0)
            {
                return;
            }
            if (eventArgs.ExtentDelta.Y != 0)
            {
                return;
            }
            if (eventArgs.ViewportDelta.Y != 0)
            {
                return;
            }
            if (viewer.Extent.Height <= viewer.Viewport.Height + 1)
            {
                return;
            }

            double remaining = viewer.Extent.Height - viewer.Viewport.Height - viewer.Offset.Y;
            if (remaining > Math.Max(120, viewer.Viewport.Height * 0.25))
            {
                return;
            }

            if (_lastAutoViewModel != viewModel || _lastAutoListVersion != viewModel.ListVersion)
            {
                _lastAutoViewModel = viewModel;
                _lastAutoListVersion = viewModel.ListVersion;
                _lastAutoCount = -1;
            }
            if (_lastAutoCount == viewModel.Commits.Count)
            {
                return;
            }

            _lastAutoCount = viewModel.Commits.Count;
            viewModel.LoadMoreCommand.Execute(null);
        }

        private async void CopyHashClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (viewModel.SelectedCommit == null)
            {
                return;
            }

            IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }
            await clipboard.SetTextAsync(viewModel.SelectedCommit.Hash);
        }

        private void CommitContextOpened(object sender, RoutedEventArgs eventArgs)
        {
            _menuCommitHash = null;
            _menuRepositoryRoot = null;
            if (sender is not ContextMenu menu)
            {
                return;
            }
            HistoryCommitItem commit = menu.DataContext as HistoryCommitItem;
            if (commit == null)
            {
                if (menu.PlacementTarget is Control target)
                {
                    commit = target.DataContext as HistoryCommitItem;
                }
            }
            if (commit == null)
            {
                menu.Close();
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                menu.Close();
                return;
            }
            if (viewModel.CurrentRepository == null)
            {
                menu.Close();
                return;
            }

            _menuCommitHash = commit.Hash;
            _menuRepositoryRoot = viewModel.CurrentRepository.RootPath;
            MenuItem tag = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Tag as string == "create-tag");
            if (tag != null)
            {
                tag.Header = GitActionDialogs.TagText("HistoryCreateTagHere", viewModel.Strings);
            }
            MenuItem reset = menu.Items.OfType<MenuItem>().LastOrDefault();
            if (reset != null)
            {
                reset.IsEnabled = viewModel.CurrentRepository.CurrentBranch != "Detached HEAD";
            }
        }

        private async void CopyCommitShaClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_menuCommitHash == null)
            {
                return;
            }
            IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }
            await clipboard.SetTextAsync(_menuCommitHash);
        }

        private async void CreateCommitBranchClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            if (_menuCommitHash == null)
            {
                return;
            }

            string hash = _menuCommitHash;
            string root = _menuRepositoryRoot;
            await GitActionDialogs.RequestNewBranchAsync(owner, viewModel.Strings.GetString("HistoryCreateBranchAtCommitTitle"), hash.Substring(0, 8), string.Empty, async name =>
            {
                bool created = await viewModel.CreateBranchAsync(root, hash, name, true);
                if (created == true)
                {
                    return null;
                }
                if (string.IsNullOrWhiteSpace(viewModel.ErrorText) == false)
                {
                    return viewModel.ErrorText;
                }
                return viewModel.Strings.GetString("HistoryBranchCreateRepositoryChanged");
            }, viewModel.Strings);
        }

        private async void CreateCommitTagClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            if (_menuCommitHash == null)
            {
                return;
            }

            string hash = _menuCommitHash;
            string root = _menuRepositoryRoot;
            await GitActionDialogs.RequestTagAsync(owner, hash, async name =>
            {
                string success = GitActionDialogs.FormatTagText("ReferenceLocalTagCreated", name.Trim(), viewModel.Strings);
                bool created = await viewModel.CreateTagAsync(root, hash, name, success);
                if (created)
                {
                    return null;
                }
                return viewModel.ErrorText;
            }, viewModel.Strings);
        }

        private async void CheckoutCommitClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            if (_menuCommitHash == null)
            {
                return;
            }

            string hash = _menuCommitHash;
            string root = _menuRepositoryRoot;
            bool confirmed = await GitActionDialogs.ConfirmAsync(owner,
                viewModel.Strings.GetString("HistoryCheckoutConfirmTitle"),
                viewModel.Strings.Format("HistoryCheckoutConfirmMessage", hash.Substring(0, 8)),
                viewModel.Strings.GetString("HistoryCheckoutConfirmAction"), viewModel.Strings);
            if (confirmed == true)
            {
                await viewModel.SwitchDetachedAsync(root, hash);
            }
        }

        private async void ResetCommitClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            if (_menuCommitHash == null)
            {
                return;
            }

            string hash = _menuCommitHash;
            string root = _menuRepositoryRoot;
            try
            {
                GitResetPreview preview = await viewModel.GetResetPreviewAsync(root, hash);
                GitResetChoice choice = await GitActionDialogs.RequestResetAsync(owner, preview, viewModel.Strings);
                if (choice == null)
                {
                    return;
                }

                bool hardConfirmed = false;
                if (choice.Mode == GitResetMode.Hard)
                {
                    hardConfirmed = await GitActionDialogs.ConfirmAsync(owner,
                        viewModel.Strings.GetString("HistoryHardResetConfirmTitle"),
                        viewModel.Strings.Format("HistoryHardResetConfirmMessage", preview.BranchName, preview.ShortHash),
                        viewModel.Strings.GetString("HistoryHardResetConfirmAction"), viewModel.Strings);
                    if (hardConfirmed == false)
                    {
                        return;
                    }
                }

                await viewModel.ResetAsync(root, preview, choice.Mode, hardConfirmed);
            }
            catch (Exception exception)
            {
                viewModel.ReportActionError(exception);
            }
        }

        private async void ParentClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (button.DataContext is not string hash)
            {
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            await viewModel.SelectCommitAsync(hash);
        }

        private async void CopyParentClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (button.DataContext is not string hash)
            {
                return;
            }
            IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }
            await clipboard.SetTextAsync(hash);
        }

        private async void DiffExpanded(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Expander expander)
            {
                return;
            }
            if (expander.DataContext is not HistoryInspectionFileItem file)
            {
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            await viewModel.ExpandFileAsync(file);
        }

        private async void ExpandAllClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            await viewModel.ExpandAllAsync();
        }

        private void CollapseAllClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            viewModel.CollapseAll();
        }

        private async void TreeExpanded(object sender, RoutedEventArgs eventArgs)
        {
            if (eventArgs.Source is not TreeViewItem item)
            {
                return;
            }
            if (item.DataContext is not HistoryTreeItem treeItem)
            {
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            try { await viewModel.ExpandTreeAsync(treeItem); }
            catch (Exception exception) { viewModel.ReportActionError(exception); }
        }

        private void FileContextOpened(object sender, RoutedEventArgs eventArgs)
        {
            _menuFilePath = null;
            _menuFileDeleted = false;
            _menuRepositoryRoot = null;
            _menuCommitHash = null;
            if (sender is not ContextMenu menu)
            {
                return;
            }
            if (menu.PlacementTarget is not Control target)
            {
                return;
            }
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            if (viewModel.CurrentRepository == null)
            {
                return;
            }
            if (viewModel.Inspection == null)
            {
                return;
            }
            bool isDirectory = false;
            bool isGitlink = false;
            if (target.DataContext is HistoryInspectionFileItem changed)
            {
                _menuFilePath = changed.Path;
                _menuFileDeleted = changed.File.StatusCode == 'D';
            }
            else if (target.DataContext is HistoryTreeItem tree)
            {
                if (tree.IsPlaceholder == true) return;
                _menuFilePath = tree.Path;
                isDirectory = tree.IsDirectory;
                isGitlink = tree.Entry.IsGitlink;
            }
            if (_menuFilePath == null) return;
            _menuRepositoryRoot = viewModel.CurrentRepository.RootPath;
            _menuCommitHash = viewModel.Inspection.Hash;
            MenuItem[] items = menu.Items.OfType<MenuItem>().ToArray();
            if (items.Length < 6) return;
            string workingPath = viewModel.FileActions.GetWorkingPath(viewModel.CurrentRepository, _menuFilePath);
            items[0].IsEnabled = isDirectory == false;
            items[1].IsEnabled = isDirectory == false && isGitlink == false && File.Exists(workingPath);
            if (items[1].IsEnabled == false) ToolTip.SetTip(items[1], viewModel.Strings.GetString("HistoryWorkingFileAbsentTooltip"));
            else ToolTip.SetTip(items[1], string.Empty);
            items[2].IsEnabled = isDirectory == false;
            items[3].IsEnabled = isDirectory == false && _menuFileDeleted == false;
            items[4].IsEnabled = isDirectory == false && isGitlink == false && _menuFileDeleted == false;
        }

        private bool TryGetFileContext(out HistoryViewModel viewModel, out GitRepository repository)
        {
            viewModel = DataContext as HistoryViewModel;
            repository = null;
            if (viewModel == null)
            {
                return false;
            }
            if (_menuFilePath == null)
            {
                return false;
            }
            try
            {
                repository = viewModel.RequireSelectedRepository(_menuRepositoryRoot, _menuCommitHash);
                return true;
            }
            catch (Exception exception)
            {
                viewModel.ReportActionError(exception);
                return false;
            }
        }

        private async void OpenFileClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(out HistoryViewModel viewModel, out GitRepository repository) == false)
            {
                return;
            }
            if (viewModel.SelectedTab == 0) viewModel.SelectedTab = 1;
            await viewModel.OpenFileAsync(_menuFilePath, _menuFileDeleted);
        }

        private void ExplorerFileClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(out HistoryViewModel viewModel, out GitRepository repository) == false) return;
            try
            {
                string absolute = viewModel.FileActions.GetWorkingPath(repository, _menuFilePath);
                if (File.Exists(absolute) == false)
                {
                    throw new GitException("HistoryWorkingFileAbsent", null, _menuFilePath);
                }
                ProcessStartInfo start = new();
                if (OperatingSystem.IsWindows() == true)
                {
                    start.FileName = "explorer.exe";
                    start.ArgumentList.Add($"/select,{absolute}");
                }
                else if (OperatingSystem.IsMacOS() == true)
                {
                    start.FileName = "open";
                    start.ArgumentList.Add("-R");
                    start.ArgumentList.Add(absolute);
                }
                else
                {
                    start.FileName = "xdg-open";
                    start.ArgumentList.Add(Path.GetDirectoryName(absolute));
                }
                Process.Start(start);
            }
            catch (Exception exception) { viewModel.ReportActionError(exception); }
        }

        private async void FileHistoryClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(out HistoryViewModel viewModel, out GitRepository repository) == false)
            {
                return;
            }
            await viewModel.ShowFileHistoryAsync(_menuFilePath);
        }

        private async void ShowInTreeClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(out HistoryViewModel viewModel, out GitRepository repository) == false)
            {
                return;
            }
            await viewModel.ShowInTreeAsync(_menuFilePath);
        }

        private async void SaveFileClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(out HistoryViewModel viewModel, out GitRepository repository) == false)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            string root = _menuRepositoryRoot;
            string hash = _menuCommitHash;
            string path = _menuFilePath;
            try
            {
                IStorageFile file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = Path.GetFileName(path) });
                if (file == null) return;
                viewModel.RequireSelectedRepository(root, hash);
                string localPath = file.TryGetLocalPath();
                if (localPath != null && File.Exists(localPath) == true)
                {
                    bool confirmed = await GitActionDialogs.ConfirmAsync(owner,
                        viewModel.Strings.GetString("HistoryExportOverwriteTitle"),
                        viewModel.Strings.Format("HistoryExportOverwriteMessage", localPath, path, hash.Substring(0, 8)),
                        viewModel.Strings.GetString("HistoryExportOverwriteAction"), viewModel.Strings);
                    if (confirmed == false) return;
                }
                viewModel.RequireSelectedRepository(root, hash);
                byte[] bytes = await viewModel.FileActions.GetSnapshotBytesAsync(repository, hash, path);
                viewModel.RequireSelectedRepository(root, hash);
                await using Stream stream = await file.OpenWriteAsync();
                await stream.WriteAsync(bytes);
            }
            catch (Exception exception) { viewModel.ReportActionError(exception); }
        }

        private async void CopyPathClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(out HistoryViewModel viewModel, out GitRepository repository) == false) return;
            IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) await clipboard.SetTextAsync(_menuFilePath);
        }

        private void CloseAuxiliaryClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            viewModel.CloseAuxiliary();
        }

    }
}
