using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;
using Bough.Core.Internals;
using Bough.App.Internals;

namespace Bough.App.Views
{
    public partial class RemoteOperationsView : UserControl
    {
        private StringHelper _stringHelper;
        private GitSettingsService _settingsService;
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
                AutomationProperties.SetName(PushButton, value.GetString("RemotePushAction"));
                AutomationProperties.SetName(FetchMenuButton, value.GetString("RemoteFetchMenuAutomation"));
                AutomationProperties.SetName(PushMenuButton, value.GetString("RemotePushMenuAutomation"));
                ToolTip.SetTip(FetchMenuButton, value.GetString("RemoteFetchMenuTip"));
                ToolTip.SetTip(PushMenuButton, value.GetString("RemotePushMenuTip"));
                UpdatePullStrategyPresentation();
            }
        }
        public GitSettingsService SettingsService
        {
            get { return _settingsService; }
            set
            {
                if (ReferenceEquals(_settingsService, value))
                {
                    return;
                }
                if (_settingsService != null)
                {
                    _settingsService.DefaultPullStrategyChanged -= OnDefaultPullStrategyChanged;
                }
                _settingsService = value;
                if (_settingsService != null)
                {
                    _settingsService.DefaultPullStrategyChanged += OnDefaultPullStrategyChanged;
                }
                UpdatePullStrategyPresentation();
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

        private void OnDefaultPullStrategyChanged(GitPullStrategy strategy)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                UpdatePullStrategyPresentation();
                return;
            }
            Dispatcher.UIThread.Post(UpdatePullStrategyPresentation);
        }

        private void UpdatePullStrategyPresentation()
        {
            if (StringHelper == null)
            {
                return;
            }

            string strategyName = GetPullStrategyName(GetDefaultPullStrategy());
            AutomationProperties.SetName(PullButton, StringHelper.Format("RemotePullConfiguredDefaultAutomation", strategyName));
            AutomationProperties.SetName(PullMenuButton, StringHelper.Format("RemotePullStrategyMenuAutomation", strategyName));
            ToolTip.SetTip(PullButton, StringHelper.Format("RemotePullConfiguredDefaultTip", strategyName));
            ToolTip.SetTip(PullMenuButton, StringHelper.Format("RemotePullStrategyMenuTip", strategyName));
        }

        private GitPullStrategy GetDefaultPullStrategy()
        {
            if (SettingsService == null)
            {
                return GitPullStrategy.FastForwardOnly;
            }
            return SettingsService.DefaultPullStrategy;
        }

        private string GetPullStrategyName(GitPullStrategy strategy)
        {
            if (strategy == GitPullStrategy.Merge)
            {
                return StringHelper.GetString("RemoteStrategyMerge");
            }
            if (strategy == GitPullStrategy.Rebase)
            {
                return StringHelper.GetString("RemoteStrategyRebase");
            }
            return StringHelper.GetString("RemoteStrategyFastForward");
        }

        private string GetPullStrategyMenuLabel(string key, string fallbackKey, GitPullStrategy strategy)
        {
            string label = StringHelper.GetString(key);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = StringHelper.GetString(fallbackKey);
            }
            if (string.IsNullOrWhiteSpace(label))
            {
                label = GetPullStrategyName(strategy);
            }
            if (string.IsNullOrWhiteSpace(label))
            {
                label = strategy.ToString();
            }
            if (GetDefaultPullStrategy() == strategy)
            {
                string suffix = StringHelper.GetString("RemotePullDefaultSuffix");
                if (string.IsNullOrWhiteSpace(suffix))
                {
                    return label;
                }
                return label + suffix;
            }
            return label;
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
            ToolTip.SetTip(pullFrom, StringHelper.Format("RemotePullChooseBranchDefaultTip", GetPullStrategyName(GetDefaultPullStrategy())));
            pullFrom.Click += PullFromClicked;
            menu.Items.Add(pullFrom);
            menu.Items.Add(new Separator());
            MenuItem fastForward = new() { Header = GetPullStrategyMenuLabel("RemotePullFastForwardAction", "RemotePullFastForwardAutomation", GitPullStrategy.FastForwardOnly) };
            ToolTip.SetTip(fastForward, StringHelper.GetString("RemotePullFastForwardTip"));
            fastForward.Click += FastForwardClicked;
            menu.Items.Add(fastForward);
            MenuItem merge = new() { Header = GetPullStrategyMenuLabel("RemotePullMergeAction", "RemoteStrategyMerge", GitPullStrategy.Merge) };
            ToolTip.SetTip(merge, StringHelper.GetString("RemotePullMergeTip"));
            merge.Click += MergeClicked;
            menu.Items.Add(merge);
            MenuItem rebase = new() { Header = GetPullStrategyMenuLabel("RemotePullRebaseAction", "RemoteStrategyRebase", GitPullStrategy.Rebase) };
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
            await RunPullAsync(viewModel, GetDefaultPullStrategy(), false);
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
            await RunPullAsync(viewModel, GetDefaultPullStrategy(), true);
        }

        private async void FastForwardClicked(object sender, RoutedEventArgs eventArgs)
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
            RemoteOperationPresenter presenter = new(OperationQueue, requestedRepository, session,
                repositoryRequestVersion, requestedBranch, requestedRemote, requestedPrune, OperationFinishedAsync);
            using CancellationTokenSource cancellation = new();
            Func<Task<bool>> observedOperation = () => presenter.ExecuteAsync(kind, fetchAll, operationName, target,
                operation, worktreeMayChange, cancellation.Token);            RemoteOperationWindow dialog = new(session, operationName, target, observedOperation, closeOnSuccess, StringHelper, ErrorLocalizer, cancellation, OperationQueue, requestedRepository.RootPath);
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
