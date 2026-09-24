using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.Core.Git;

namespace Bough.App.Views
{
    public partial class RemoteOperationsView : UserControl
    {
        private StringHelper _stringHelper;
        public StringHelper StringHelper
        {
            get { return _stringHelper; }
            set
            {
                _stringHelper = value;
                if (value == null)
                {
                    return;
                }
                UpstreamLabel.Text = value.GetString("RemoteUpstreamLabel");
                FetchButton.Content = value.GetString("RemoteFetchAction");
                PullButton.Content = value.GetString("RemotePullAction");
                PushButton.Content = value.GetString("RemotePushAction");
                AutomationProperties.SetName(FetchButton, value.GetString("RemoteFetchAction"));
                AutomationProperties.SetName(PullButton, value.GetString("RemotePullFastForwardAutomation"));
                AutomationProperties.SetName(PushButton, value.GetString("RemotePushAction"));
                AutomationProperties.SetName(FetchMenuButton, value.GetString("RemoteFetchMenuAutomation"));
                AutomationProperties.SetName(PullMenuButton, value.GetString("RemotePullMenuAutomation"));
                AutomationProperties.SetName(PushMenuButton, value.GetString("RemotePushMenuAutomation"));
                ToolTip.SetTip(FetchMenuButton, value.GetString("RemoteFetchMenuTip"));
                ToolTip.SetTip(PullButton, value.GetString("RemotePullDefaultTip"));
                ToolTip.SetTip(PullMenuButton, value.GetString("RemotePullMenuTip"));
                ToolTip.SetTip(PushMenuButton, value.GetString("RemotePushMenuTip"));
            }
        }
        public GitErrorLocalizer ErrorLocalizer { get; set; }
        public GitOperationQueue OperationQueue { get; set; }
        public Func<RemoteOperationsViewModel> CreateOperationSession { get; set; }
        public Func<int> GetRepositoryRequestVersion { get; set; }
        public Func<GitRepository, RemoteOperationStateSnapshot, bool, int, Task> OperationFinishedAsync { get; set; }

        public event Action InternalDialogOpening;
        public event Action InternalDialogClosed;

        public RemoteOperationsView()
        {
            InitializeComponent();
        }

        private void FetchMenuClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }

            ContextMenu menu = new();
            MenuItem remoteGroup = new() { Header = StringHelper.GetString("RemoteSelectFetchRemote") };
            ToolTip.SetTip(remoteGroup, StringHelper.GetString("RemoteSelectFetchRemoteTip"));
            foreach (string remote in viewModel.Remotes)
            {
                MenuItem remoteItem = new() { Header = remote, ToggleType = MenuItemToggleType.Radio, IsChecked = remote == viewModel.SelectedRemote, MinWidth = 220 };
                ToolTip.SetTip(remoteItem, StringHelper.GetString("RemoteFetchRemoteItemTip"));
                remoteItem.Click += delegate
                {
                    viewModel.SelectedRemote = remote;
                };
                remoteGroup.Items.Add(remoteItem);
            }
            menu.Items.Add(remoteGroup);

            MenuItem prune = new() { Header = StringHelper.GetString("RemotePruneOption"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = viewModel.Prune };
            ToolTip.SetTip(prune, StringHelper.GetString("RemotePruneOptionTip"));
            prune.Click += delegate
            {
                bool enabled = viewModel.Prune == false;
                viewModel.Prune = enabled;
                prune.IsChecked = enabled;
            };
            menu.Items.Add(prune);
            menu.Items.Add(new Separator());

            MenuItem fetchAll = new() { Header = StringHelper.GetString("RemoteFetchAllAction"), IsEnabled = viewModel.CanFetchAll };
            ToolTip.SetTip(fetchAll, StringHelper.GetString("RemoteFetchAllTip"));
            fetchAll.Click += FetchAllClicked;
            menu.Items.Add(fetchAll);
            OpenMenu(button, menu);
        }

        private void PullMenuClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }

            ContextMenu menu = new();
            MenuItem pullFrom = new() { Header = StringHelper.GetString("RemotePullChooseBranch") };
            ToolTip.SetTip(pullFrom, StringHelper.GetString("RemotePullChooseBranchTip"));
            pullFrom.Click += PullFromClicked;
            menu.Items.Add(pullFrom);
            menu.Items.Add(new Separator());
            MenuItem merge = new() { Header = StringHelper.GetString("RemotePullMergeAction") };
            ToolTip.SetTip(merge, StringHelper.GetString("RemotePullMergeTip"));
            merge.Click += MergeClicked;
            menu.Items.Add(merge);
            MenuItem rebase = new() { Header = StringHelper.GetString("RemotePullRebaseAction") };
            ToolTip.SetTip(rebase, StringHelper.GetString("RemotePullRebaseTip"));
            rebase.Click += RebaseClicked;
            menu.Items.Add(rebase);
            OpenMenu(button, menu);
        }

        private void PushMenuClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }

            ContextMenu menu = new();
            MenuItem pushTo = new() { Header = StringHelper.GetString("RemotePushChooseTarget"), IsEnabled = viewModel.CanPushTo };
            ToolTip.SetTip(pushTo, StringHelper.GetString("RemotePushChooseTargetTip"));
            pushTo.Click += PushToClicked;
            menu.Items.Add(pushTo);
            OpenMenu(button, menu);
        }

        private static void OpenMenu(Button button, ContextMenu menu)
        {
            menu.MinWidth = 240;
            menu.Placement = PlacementMode.BottomEdgeAlignedRight;
            menu.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.FlipY;
            button.ContextMenu = menu;
            menu.Open(button);
        }

        private async void FetchClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanFetch == false)
            {
                return;
            }
            await ShowOperationAsync(viewModel, RemoteOperationKind.Fetch, false, StringHelper.GetString("RemoteFetchAction"), viewModel.SelectedRemote,
                session => session.FetchAsync(false), true, false);
        }

        private async void FetchAllClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanFetchAll == false)
            {
                return;
            }
            await ShowOperationAsync(viewModel, RemoteOperationKind.Fetch, true, StringHelper.GetString("RemoteFetchAllAction"), StringHelper.GetString("RemoteAllRemotes"),
                session => session.FetchAsync(true), true, false);
        }

        private async void PullClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanPull == false)
            {
                return;
            }
            await RunPullAsync(viewModel, GitPullStrategy.FastForwardOnly, false);
        }

        private async void PullFromClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanPull == false)
            {
                return;
            }
            await RunPullAsync(viewModel, GitPullStrategy.FastForwardOnly, true);
        }

        private async void MergeClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanPull == false)
            {
                return;
            }
            await RunPullAsync(viewModel, GitPullStrategy.Merge, false);
        }

        private async void RebaseClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanPull == false)
            {
                return;
            }
            await RunPullAsync(viewModel, GitPullStrategy.Rebase, false);
        }

        private async void PushClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanPush == false)
            {
                return;
            }
            await RunPushAsync(viewModel, false);
        }

        private async void PushToClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not RemoteOperationsViewModel viewModel)
            {
                return;
            }
            if (viewModel.CanPushTo == false)
            {
                return;
            }
            await RunPushAsync(viewModel, true);
        }

        private async Task RunPullAsync(RemoteOperationsViewModel viewModel, GitPullStrategy strategy, bool chooseTarget)
        {
            GitRepository repository = viewModel.CurrentRepository;
            string strategyName = StringHelper.GetString("RemoteStrategyFastForward");
            if (strategy == GitPullStrategy.Merge)
            {
                strategyName = StringHelper.GetString("RemoteStrategyMerge");
            }
            if (strategy == GitPullStrategy.Rebase)
            {
                strategyName = StringHelper.GetString("RemoteStrategyRebase");
            }
            RemoteOperationTarget target;
            bool needsTarget = chooseTarget;
            if (viewModel.HasUpstream == false)
            {
                needsTarget = true;
            }
            if (needsTarget == true)
            {
                if (TopLevel.GetTopLevel(this) is not Window owner)
                {
                    return;
                }
                InternalDialogOpening?.Invoke();
                try
                {
                    target = await RemoteTargetDialogs.SelectPullAsync(owner, viewModel, strategyName, StringHelper, ErrorLocalizer);
                }
                finally
                {
                    InternalDialogClosed?.Invoke();
                }
                if (target == null)
                {
                    return;
                }
            }
            else
            {
                target = new RemoteOperationTarget(viewModel.UpstreamRemote, viewModel.UpstreamBranch);
            }
            if (viewModel.CurrentRepository != repository)
            {
                return;
            }
            if (viewModel.CanPull == false)
            {
                return;
            }
            await ShowOperationAsync(viewModel, RemoteOperationKind.Pull, false, StringHelper.Format("RemotePullOperationTitle", strategyName),
                $"{target.Remote}/{target.Branch} → {viewModel.CurrentBranchText}",
                session => session.PullAsync(strategy, target.Remote, target.Branch), true, true);
        }

        private async Task RunPushAsync(RemoteOperationsViewModel viewModel, bool chooseTarget)
        {
            GitRepository repository = viewModel.CurrentRepository;
            RemoteOperationTarget target;
            bool confirmed = false;
            bool needsTarget = chooseTarget;
            if (viewModel.HasUpstream == false)
            {
                needsTarget = true;
            }
            if (needsTarget == true)
            {
                if (TopLevel.GetTopLevel(this) is not Window owner)
                {
                    return;
                }
                InternalDialogOpening?.Invoke();
                try
                {
                    target = await RemoteTargetDialogs.SelectPushAsync(owner, viewModel, StringHelper, ErrorLocalizer);
                }
                finally
                {
                    InternalDialogClosed?.Invoke();
                }
                if (target == null)
                {
                    return;
                }
                confirmed = true;
            }
            else
            {
                target = new RemoteOperationTarget(viewModel.UpstreamRemote, viewModel.UpstreamBranch);
            }
            if (viewModel.CurrentRepository != repository)
            {
                return;
            }
            if (viewModel.CanPushTo == false)
            {
                return;
            }
            if (OperationQueue == null)
            {
                return;
            }
            GitOperationQueueState queueState = OperationQueue.GetState(repository.RootPath);
            if (queueState.IsRunning == false)
            {
                if (queueState.PendingCount == 0)
                {
                    if (await viewModel.PrepareUpstreamPushAsync(target.Remote, target.Branch) == false)
                    {
                        return;
                    }
                }
            }
            await ShowOperationAsync(viewModel, RemoteOperationKind.Push, false, StringHelper.GetString("RemotePushAction"),
                $"{viewModel.CurrentBranchText} → {target.Remote}/{target.Branch}", async session =>
            {
                if (await session.PrepareUpstreamPushAsync(target.Remote, target.Branch) == false)
                {
                    return false;
                }
                return await session.PushAsync(target.Remote, target.Branch, confirmed);
            }, false, false);
        }

        private async Task ShowOperationAsync(RemoteOperationsViewModel viewModel, RemoteOperationKind kind, bool fetchAll,
            string operationName, string target,
            Func<RemoteOperationsViewModel, Task<bool>> operation, bool closeOnSuccess, bool worktreeMayChange)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            if (OperationQueue == null)
            {
                return;
            }
            if (CreateOperationSession == null)
            {
                return;
            }
            if (GetRepositoryRequestVersion == null)
            {
                return;
            }
            GitRepository requestedRepository = viewModel.CurrentRepository;
            if (requestedRepository == null)
            {
                return;
            }
            int repositoryRequestVersion = GetRepositoryRequestVersion();
            string requestedBranch = viewModel.CurrentBranchText;
            string requestedRemote = viewModel.SelectedRemote;
            bool requestedPrune = viewModel.Prune;
            RemoteOperationsViewModel session = CreateOperationSession();
            if (session == null)
            {
                return;
            }
            session.BindRepository(requestedRepository);
            session.SelectedRemote = requestedRemote;
            session.Prune = requestedPrune;
            using CancellationTokenSource cancellation = new();
            Func<Task<bool>> observedOperation = () => OperationQueue.EnqueueAsync(
                requestedRepository.RootPath, $"{operationName} · {target}", async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await session.SetRepositoryAsync(requestedRepository);
                    token.ThrowIfCancellationRequested();
                    session.SelectedRemote = requestedRemote;
                    session.Prune = requestedPrune;
                    StringComparison comparison = StringComparison.Ordinal;
                    if (OperatingSystem.IsWindows())
                    {
                        comparison = StringComparison.OrdinalIgnoreCase;
                    }
                    GitRepository sessionRepository = session.CurrentRepository;
                    if (sessionRepository == null)
                    {
                        throw new InvalidOperationException(StringHelper.GetString("RemoteQueueRepositoryUnavailable"));
                    }
                    if (string.Equals(sessionRepository.RootPath, requestedRepository.RootPath, comparison) == false)
                    {
                        throw new InvalidOperationException(StringHelper.GetString("RemoteQueueRepositoryChanged"));
                    }
                    if (kind == RemoteOperationKind.Fetch)
                    {
                        if (fetchAll == false)
                        {
                            if (session.SelectedRemote != requestedRemote)
                            {
                                throw new InvalidOperationException(StringHelper.GetString("RemoteQueueFetchRemoteChanged"));
                            }
                            if (session.CanFetch == false)
                            {
                                throw new InvalidOperationException(StringHelper.GetString("RemoteQueueFetchRemoteUnavailable"));
                            }
                        }
                        if (fetchAll == true)
                        {
                            if (session.CanFetchAll == false)
                            {
                                throw new InvalidOperationException(StringHelper.GetString("RemoteQueueFetchAllUnavailable"));
                            }
                        }
                    }
                    if (kind == RemoteOperationKind.Pull || kind == RemoteOperationKind.Push)
                    {
                        if (session.CurrentBranchText != requestedBranch)
                        {
                            throw new InvalidOperationException(StringHelper.GetString("RemoteQueueBranchChanged"));
                        }
                    }
                    try
                    {
                        return await operation(session);
                    }
                    finally
                    {
                        GitRepository completedRepository = session.CurrentRepository;
                        if (completedRepository != null)
                        {
                            if (string.Equals(completedRepository.RootPath, requestedRepository.RootPath, comparison))
                            {
                                if (OperationFinishedAsync != null)
                                {
                                    await OperationFinishedAsync(completedRepository, session.LatestOperationStateSnapshot,
                                        worktreeMayChange, repositoryRequestVersion);
                                }
                            }
                        }
                    }
                }, cancellationToken: cancellation.Token);
            RemoteOperationWindow dialog = new(session, operationName, target, observedOperation, closeOnSuccess, StringHelper, ErrorLocalizer, cancellation, OperationQueue, requestedRepository.RootPath);
            InternalDialogOpening?.Invoke();
            try
            {
                await dialog.ShowDialog(owner);
            }
            finally
            {
                InternalDialogClosed?.Invoke();
            }
        }
    }
}
