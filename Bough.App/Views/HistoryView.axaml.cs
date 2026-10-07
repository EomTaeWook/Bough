using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bough.App.ViewModels;
using Bough.App.Localization;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Views
{
    public partial class HistoryView : UserControl
    {
        private string _menuCommitHash;
        private string _menuRepositoryRoot;
        private ScrollViewer _commitScrollViewer;
        private HistoryViewModel _lastAutoViewModel;
        private int _lastAutoListVersion = -1;
        private int _lastAutoCount = -1;
        private HistoryViewModel _layoutViewModel;
        private bool _commitListHeightUserSet;
        private bool _commitListHeightUpdateQueued;
        private readonly HashSet<ContextMenu> _openFileMenus = [];
        private TaskCompletionSource<bool> _previewMenusClosed;
        private bool _previewRightClickPending;
        private int _previewPointerVersion;

        public HistoryView()
        {
            InitializeComponent();
            LanguageChangeBinding.Bind(this, () => (DataContext as HistoryViewModel)?.Strings);
            AddHandler(TreeViewItem.ExpandedEvent, TreeExpanded);
            HistoryChangedFiles.AddHandler(InputElement.PointerPressedEvent, PreviewPointerPressed, RoutingStrategies.Tunnel, true);
            HistoryChangedFiles.AddHandler(InputElement.PointerReleasedEvent, PreviewPointerReleased, RoutingStrategies.Bubble, true);
            HistoryCommitList.TemplateApplied += (_, _) => AttachCommitScrollViewer();
            DataContextChanged += (_, _) =>
            {
                _layoutViewModel?.ExternalFileOpen.Invalidate();
                _layoutViewModel?.FilePreview.Suspend();
                ResetPreviewMenuGate();
                AttachLayoutViewModel();
            };
            AttachedToVisualTree += (_, _) =>
            {
                AttachCommitScrollViewer();
                AttachLayoutViewModel();
                ScheduleCommitListHeightUpdate();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                (DataContext as HistoryViewModel)?.ExternalFileOpen.Invalidate();
                (DataContext as HistoryViewModel)?.FilePreview.Suspend();
                ResetPreviewMenuGate();
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
            _layoutViewModel.FilePreview.SetPresentationScheduler(SchedulePreviewPresentationAsync);
            _layoutViewModel.FilePreview.Resume();
            ScheduleCommitListHeightUpdate();
        }

        private void DetachLayoutViewModel()
        {
            if (_layoutViewModel == null)
            {
                return;
            }

            _layoutViewModel.PropertyChanged -= LayoutViewModelPropertyChanged;
            _layoutViewModel.FilePreview.SetPresentationScheduler(null);
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
            HistoryCommitItem commit = menu.Tag as HistoryCommitItem;
            if (commit == null)
            {
                commit = menu.DataContext as HistoryCommitItem;
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
            MenuItem reset = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Tag as string == "reset");
            if (reset != null)
            {
                reset.IsEnabled = viewModel.CurrentRepository.CurrentBranch != "Detached HEAD";
            }
        }

        private async void CloseFileTreeClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not HistoryViewModel viewModel)
            {
                return;
            }
            await viewModel.CloseFileTreeAsync();
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
            HistoryCommitSelectionResult result = await viewModel.SelectCommitAsync(hash);
            viewModel.ReportCommitSelectionResult(result);
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

        private void PreviewPointerPressed(object sender, PointerPressedEventArgs eventArgs)
        {
            if (eventArgs.GetCurrentPoint(HistoryChangedFiles).Properties.IsRightButtonPressed == false)
            {
                return;
            }
            _previewRightClickPending = true;
            _previewPointerVersion++;
            EnsurePreviewMenuGate();
        }

        private void PreviewPointerReleased(object sender, PointerReleasedEventArgs eventArgs)
        {
            if (_previewRightClickPending == false)
            {
                return;
            }
            int version = _previewPointerVersion;
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _previewPointerVersion)
                {
                    return;
                }
                _previewRightClickPending = false;
                ReleasePreviewMenuGate();
            }, DispatcherPriority.Background);
        }

        private void EnsurePreviewMenuGate()
        {
            if (_previewMenusClosed == null)
            {
                _previewMenusClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private void ReleasePreviewMenuGate()
        {
            if (_previewRightClickPending)
            {
                return;
            }
            if (_openFileMenus.Count > 0)
            {
                return;
            }
            TaskCompletionSource<bool> completion = _previewMenusClosed;
            _previewMenusClosed = null;
            completion?.TrySetResult(true);
        }

        private void ResetPreviewMenuGate()
        {
            _previewPointerVersion++;
            _previewRightClickPending = false;
            _openFileMenus.Clear();
            ReleasePreviewMenuGate();
        }

        private async Task SchedulePreviewPresentationAsync(Action apply, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TaskCompletionSource<bool> gate = _previewMenusClosed;
                if (gate != null)
                {
                    await gate.Task.WaitAsync(cancellationToken);
                }
                bool applied = false;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_previewRightClickPending)
                    {
                        return;
                    }
                    if (_openFileMenus.Count > 0)
                    {
                        return;
                    }
                    apply();
                    applied = true;
                }, DispatcherPriority.Background, cancellationToken);
                if (applied)
                {
                    return;
                }
            }
        }

        private void FileContextClosed(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not ContextMenu menu)
            {
                return;
            }
            _openFileMenus.Remove(menu);
            ReleasePreviewMenuGate();
        }

        private static void DisableFileMenu(ContextMenu menu)
        {
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                item.Tag = null;
                item.IsEnabled = false;
            }
        }

        private void FileContextOpened(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not ContextMenu menu)
            {
                return;
            }
            _previewRightClickPending = false;
            _openFileMenus.Add(menu);
            EnsurePreviewMenuGate();
            DisableFileMenu(menu);
            if (DataContext is not HistoryViewModel viewModel)
            {
                menu.Close();
                return;
            }
            try
            {
                HistoryFileActionContext context = viewModel.CreateFileActionContext(menu.Tag);
                viewModel.RequireFileActionRepository(context);
                foreach (MenuItem item in menu.Items.OfType<MenuItem>())
                {
                    item.Tag = context;
                    ToolTip.SetTip(item, null);
                    switch (item.CommandParameter as string)
                    {
                        case "open":
                        case "history":
                            item.IsEnabled = context.IsDirectory == false;
                            break;
                        case "explorer":
                            item.IsEnabled = context.IsDirectory == false && context.IsGitlink == false;
                            break;
                        case "save":
                            item.IsEnabled = context.IsDirectory == false && context.IsGitlink == false && context.IsDeleted == false;
                            break;
                        case "copy":
                            item.IsEnabled = true;
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                DisableFileMenu(menu);
                menu.Close();
                viewModel.ReportActionError(exception);
            }
        }

        private bool TryGetFileContext(object sender, out HistoryViewModel viewModel,
            out GitRepository repository, out HistoryFileActionContext context)
        {
            viewModel = DataContext as HistoryViewModel;
            repository = null;
            context = null;
            if (viewModel == null)
            {
                return false;
            }
            try
            {
                if (sender is not MenuItem item)
                {
                    throw new GitException("HistorySelectedCommitChanged", null, Array.Empty<object>());
                }
                if (item.Tag is not HistoryFileActionContext captured)
                {
                    throw new GitException("HistorySelectedCommitChanged", null, Array.Empty<object>());
                }
                repository = viewModel.RequireFileActionRepository(captured);
                context = captured;
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
            if (TryGetFileContext(sender, out HistoryViewModel viewModel, out GitRepository repository, out HistoryFileActionContext context) == false)
            {
                return;
            }
            GitTemporarySnapshotFile snapshot = null;
            bool launched = false;
            int openRequest = -1;
            try
            {
                Task<GitTemporarySnapshotFile> preparation = viewModel.ExternalFileOpen.PrepareAsync(context);
                openRequest = viewModel.ExternalFileOpen.RequestVersion;
                snapshot = await preparation;
                if (snapshot == null)
                {
                    return;
                }
                if (ReferenceEquals(DataContext, viewModel) == false)
                {
                    return;
                }
                if (openRequest != viewModel.ExternalFileOpen.RequestVersion)
                {
                    return;
                }
                viewModel.RequireFileActionRepository(context);
                ProcessStartInfo start = new();
                if (OperatingSystem.IsWindows())
                {
                    start.FileName = snapshot.FilePath;
                    start.UseShellExecute = true;
                }
                else
                {
                    start.UseShellExecute = false;
                    if (OperatingSystem.IsMacOS())
                    {
                        start.FileName = "open";
                    }
                    else
                    {
                        start.FileName = "xdg-open";
                    }
                    start.ArgumentList.Add(snapshot.FilePath);
                    start.RedirectStandardOutput = true;
                    start.RedirectStandardError = true;
                }
                try
                {
                    using Process process = Process.Start(start);
                    launched = true;
                    if (OperatingSystem.IsWindows())
                    {
                        return;
                    }
                    if (process == null)
                    {
                        return;
                    }
                    Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
                    Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                    await process.WaitForExitAsync();
                    await Task.WhenAll(output, error);
                    if (process.ExitCode != 0)
                    {
                        throw new GitException("HistorySnapshotOpenFailed", null, context.Path);
                    }
                }
                catch (Exception exception)
                {
                    throw new GitException("HistorySnapshotOpenFailed", exception, context.Path);
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(DataContext, viewModel) == false)
                {
                    return;
                }
                if (openRequest != viewModel.ExternalFileOpen.RequestVersion)
                {
                    return;
                }
                try
                {
                    viewModel.RequireFileActionRepository(context);
                }
                catch (GitException)
                {
                    return;
                }
                viewModel.ReportActionError(exception);
            }
            finally
            {
                if (launched == false)
                {
                    await viewModel.FileActions.DiscardSnapshotFileAsync(snapshot);
                }
            }
        }

        private void ExplorerFileClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(sender, out HistoryViewModel viewModel, out GitRepository repository, out HistoryFileActionContext context) == false)
            {
                return;
            }
            try
            {
                string absolute = viewModel.FileActions.GetWorkingPath(repository, context.Path);
                if (File.Exists(absolute) == false)
                {
                    throw new GitException("HistoryWorkingFileAbsent", null, context.Path);
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
            catch (Exception exception)
            {
                viewModel.ReportActionError(exception);
            }
        }

        private void FileHistoryClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(sender, out HistoryViewModel viewModel, out GitRepository repository, out HistoryFileActionContext context) == false)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            try
            {
                FileHistoryWindow window = new(viewModel.CreateFileHistoryPresenter(context.RepositoryRoot, context.CommitHash, context.Path), viewModel.Strings);
                window.Show(owner);
            }
            catch (Exception exception)
            {
                viewModel.ReportActionError(exception);
            }
        }

        private async void SaveFileClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(sender, out HistoryViewModel viewModel, out GitRepository repository, out HistoryFileActionContext context) == false)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            string hash = context.CommitHash;
            string path = context.Path;
            try
            {
                IStorageFile file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = Path.GetFileName(path) });
                if (file == null)
                {
                    return;
                }
                viewModel.RequireFileActionRepository(context);
                string localPath = file.TryGetLocalPath();
                if (localPath != null && File.Exists(localPath) == true)
                {
                    bool confirmed = await GitActionDialogs.ConfirmAsync(owner,
                        viewModel.Strings.GetString("HistoryExportOverwriteTitle"),
                        viewModel.Strings.Format("HistoryExportOverwriteMessage", localPath, path, hash.Substring(0, 8)),
                        viewModel.Strings.GetString("HistoryExportOverwriteAction"), viewModel.Strings);
                    if (confirmed == false)
                    {
                        return;
                    }
                }
                viewModel.RequireFileActionRepository(context);
                byte[] bytes = await viewModel.FileActions.GetSnapshotBytesAsync(repository, hash, path);
                viewModel.RequireFileActionRepository(context);
                await using Stream stream = await file.OpenWriteAsync();
                await stream.WriteAsync(bytes);
            }
            catch (Exception exception)
            {
                viewModel.ReportActionError(exception);
            }
        }

        private async void CopyPathClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (TryGetFileContext(sender, out HistoryViewModel viewModel, out GitRepository repository, out HistoryFileActionContext context) == false)
            {
                return;
            }
            try
            {
                IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(context.Path);
                }
            }
            catch (Exception exception)
            {
                viewModel.ReportActionError(exception);
            }
        }
    }
}
