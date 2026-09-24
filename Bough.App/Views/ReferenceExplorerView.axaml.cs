using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bough.App.ViewModels;
using Bough.Core.Git;

namespace Bough.App.Views
{
    public partial class ReferenceExplorerView : UserControl
    {
        private ReferenceTreeNode _menuNode;
        private string _menuRepositoryRoot;

        public ReferenceExplorerView()
        {
            InitializeComponent();
            ReferenceTree.AddHandler(InputElement.PointerPressedEvent, TreeNodePointerPressed, RoutingStrategies.Bubble, true);
        }

        private void TreeNodeTapped(object sender, TappedEventArgs eventArgs)
        {
            if (sender is not Border border)
            {
                return;
            }
            if (border.DataContext is not ReferenceTreeNode node)
            {
                return;
            }
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            viewModel.SelectedTreeNode = node;
            if (node.Kind == ReferenceTreeNodeKind.Branch)
            {
                return;
            }
            if (node.Kind == ReferenceTreeNodeKind.RemoteBranch)
            {
                return;
            }
            ActivateNonBranchNode(viewModel, node);
        }

        private void TreeNodePointerPressed(object sender, PointerPressedEventArgs eventArgs)
        {
            if (eventArgs.Source is not Control source)
            {
                return;
            }
            ReferenceTreeNode node = FindTreeNode(source);
            if (node == null)
            {
                return;
            }
            if (eventArgs.GetCurrentPoint(ReferenceTree).Properties.IsRightButtonPressed)
            {
                _menuNode = node;
                _menuRepositoryRoot = (DataContext as ReferenceExplorerViewModel)?.CurrentRepository?.RootPath;
            }
        }

        private async void TreeNodeDoubleTapped(object sender, TappedEventArgs eventArgs)
        {
            if (sender is not Border border)
            {
                return;
            }
            if (border.DataContext is not ReferenceTreeNode node)
            {
                return;
            }
            if (node.Kind != ReferenceTreeNodeKind.Branch && node.Kind != ReferenceTreeNodeKind.RemoteBranch)
            {
                return;
            }
            eventArgs.Handled = true;
            await ActivateBranchNodeAsync(node);
        }

        private static ReferenceTreeNode FindTreeNode(Control source)
        {
            if (source.DataContext is ReferenceTreeNode node)
            {
                return node;
            }
            foreach (Control ancestor in source.GetVisualAncestors().OfType<Control>())
            {
                if (ancestor.DataContext is ReferenceTreeNode ancestorNode)
                {
                    return ancestorNode;
                }
            }
            return null;
        }

        private void IgnoreTreeTap(object sender, TappedEventArgs eventArgs)
        {
            eventArgs.Handled = true;
        }

        private async void TreeKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Source is Button)
            {
                return;
            }
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            if (eventArgs.Key != Key.Enter)
            {
                return;
            }
            if (viewModel.SelectedTreeNode == null)
            {
                return;
            }

            eventArgs.Handled = true;
            ReferenceTreeNode node = viewModel.SelectedTreeNode;
            if (node.Kind == ReferenceTreeNodeKind.Branch)
            {
                await ActivateBranchNodeAsync(node);
                return;
            }
            if (node.Kind == ReferenceTreeNodeKind.RemoteBranch)
            {
                await ActivateBranchNodeAsync(node);
                return;
            }
            ActivateNonBranchNode(viewModel, node);
        }

        private async Task ActivateBranchNodeAsync(ReferenceTreeNode node)
        {
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            if (node == null)
            {
                return;
            }

            viewModel.SelectedTreeNode = node;
            if (node.Kind == ReferenceTreeNodeKind.Branch)
            {
                await viewModel.SwitchBranchAsync(node.Target as GitLocalBranch);
                return;
            }
            if (node.Kind == ReferenceTreeNodeKind.RemoteBranch)
            {
                await CheckoutRemoteBranchAsync(viewModel, (GitRemoteBranch)node.Target, viewModel.CurrentRepository?.RootPath);
            }
        }

        private void ActivateNonBranchNode(ReferenceExplorerViewModel viewModel, ReferenceTreeNode node)
        {
            if (node.Kind == ReferenceTreeNodeKind.Branch)
            {
                return;
            }
            if (node.Kind == ReferenceTreeNodeKind.RemoteBranch)
            {
                return;
            }
            if (node.IsStashSection == true)
            {
                viewModel.OpenStashes();
                return;
            }
            if (node.Kind == ReferenceTreeNodeKind.Section || node.Kind == ReferenceTreeNodeKind.Remote)
            {
                node.IsExpanded = node.IsExpanded == false;
                return;
            }
            if (node.Kind == ReferenceTreeNodeKind.Tag)
            {
                viewModel.SelectTag((GitTag)node.Target);
                return;
            }
            if (node.Kind == ReferenceTreeNodeKind.Stash)
            {
                viewModel.SelectStash((GitStashEntry)node.Target);
            }
        }

        private void TreeContextOpened(object sender, RoutedEventArgs eventArgs)
        {
            ReferenceTreeNode pointerNode = _menuNode;
            string pointerRepositoryRoot = _menuRepositoryRoot;
            _menuNode = null;
            _menuRepositoryRoot = null;
            if (sender is not ContextMenu menu)
            {
                return;
            }
            ReferenceTreeNode node = pointerNode;
            if (node == null)
            {
                node = menu.DataContext as ReferenceTreeNode;
            }
            if (node == null)
            {
                if (menu.PlacementTarget is Control target)
                {
                    node = FindTreeNode(target);
                }
            }
            if (node == null)
            {
                menu.Close();
                return;
            }
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                menu.Close();
                return;
            }
            if (viewModel.CurrentRepository == null)
            {
                menu.Close();
                return;
            }
            if (pointerRepositoryRoot != null)
            {
                StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (string.Equals(pointerRepositoryRoot, viewModel.CurrentRepository.RootPath, pathComparison) == false)
                {
                    menu.Close();
                    return;
                }
            }

            _menuNode = node;
            _menuRepositoryRoot = viewModel.CurrentRepository.RootPath;
            viewModel.SelectedTreeNode = node;
            if (node.IsEmpty == true)
            {
                menu.Close();
                return;
            }
            MenuItem[] items = menu.Items.OfType<MenuItem>().ToArray();
            if (items.Length < 7)
            {
                menu.Close();
                return;
            }
            foreach (MenuItem item in items)
            {
                item.DataContext = node;
                item.CommandParameter = viewModel.CurrentRepository.RootPath;
                item.Tag = node;
            }

            items[0].Header = viewModel.ReferenceText("ReferenceCreateBranch");
            items[1].Header = viewModel.ReferenceText("ReferenceCreateTag");
            items[2].Header = viewModel.ReferenceText("ReferenceOpenStashes");
            items[3].Header = viewModel.ReferenceText("ReferenceSwitchBranch");
            items[4].Header = viewModel.ReferenceText("ReferenceCreateFromBranch");
            items[5].Header = viewModel.ReferenceText("ReferenceCheckoutRemoteBranch");
            items[6].Header = viewModel.ReferenceText("ReferenceCopyName");
            items[0].IsVisible = node.IsBranchSection;
            items[0].IsEnabled = true;
            items[1].IsVisible = node.IsTagSection;
            items[1].IsEnabled = true;
            items[2].IsVisible = node.IsStashSection;
            items[3].IsVisible = node.Kind == ReferenceTreeNodeKind.Branch;
            items[3].IsEnabled = true;
            items[4].IsVisible = node.Kind == ReferenceTreeNodeKind.Branch;
            items[4].IsEnabled = true;
            items[5].IsVisible = node.Kind == ReferenceTreeNodeKind.RemoteBranch;
            items[5].IsEnabled = true;
            items[6].IsVisible = node.IsEmpty == false;
        }

        private void TreeContextClosed(object sender, RoutedEventArgs eventArgs)
        {
            _menuNode = null;
            _menuRepositoryRoot = null;
        }

        private void CreateSectionAddClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
            {
                return;
            }
            if (button.DataContext is not ReferenceTreeNode node)
            {
                return;
            }
            if (node.IsTagSection)
            {
                CreateTagSectionClicked(sender, eventArgs);
                return;
            }
            if (node.IsBranchSection)
            {
                CreateBranchSectionClicked(sender, eventArgs);
            }
        }

        private async void CreateTagSectionClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            if (viewModel.CurrentRepository == null)
            {
                return;
            }

            string repositoryRoot = viewModel.CurrentRepository.RootPath;
            if (sender is MenuItem item)
            {
                repositoryRoot = item.CommandParameter as string ?? repositoryRoot;
            }
            await GitActionDialogs.RequestTagAsync(owner, "HEAD", async name =>
            {
                bool created = await viewModel.CreateTagFromSectionAsync(repositoryRoot, name);
                if (created)
                {
                    return null;
                }
                return viewModel.StatusMessage;
            }, viewModel.Strings);
        }

        private async void CreateBranchSectionClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            if (viewModel.CurrentRepository == null)
            {
                return;
            }
            string repositoryRoot = viewModel.CurrentRepository.RootPath;
            if (sender is MenuItem menuItem)
            {
                repositoryRoot = menuItem.CommandParameter as string ?? repositoryRoot;
            }
            await GitActionDialogs.RequestNewBranchAsync(owner, viewModel.ReferenceText("ReferenceCreateBranch"), "HEAD", string.Empty, async name =>
            {
                bool created = await viewModel.CreateBranchFromSectionAsync(repositoryRoot, name);
                if (created == true)
                {
                    return null;
                }
                return viewModel.StatusMessage;
            }, viewModel.Strings);
        }

        private void OpenStashesClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            viewModel.OpenStashes();
        }

        private async void SwitchMenuBranchClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            if (sender is not MenuItem item)
            {
                return;
            }
            GitLocalBranch branch = (item.Tag as ReferenceTreeNode)?.Target as GitLocalBranch;
            if (branch == null)
            {
                branch = (item.DataContext as ReferenceTreeNode)?.Target as GitLocalBranch;
            }
            string repositoryRoot = item.CommandParameter as string;
            await viewModel.SwitchMenuBranchAsync(repositoryRoot, branch);
        }

        private async void CreateFromBranchClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            GitLocalBranch branch = _menuNode?.Target as GitLocalBranch;
            if (branch == null)
            {
                if (sender is MenuItem item)
                {
                    branch = (item.DataContext as ReferenceTreeNode)?.Target as GitLocalBranch;
                }
            }
            if (branch == null)
            {
                return;
            }

            string repositoryRoot = (sender as MenuItem)?.CommandParameter as string ?? _menuRepositoryRoot ?? viewModel.CurrentRepository?.RootPath;
            await GitActionDialogs.RequestNewBranchAsync(owner, viewModel.ReferenceText("ReferenceCreateFromBranch"), branch.Name, string.Empty, async name =>
            {
                bool created = await viewModel.CreateFromBranchAsync(repositoryRoot, branch, name);
                if (created == true)
                {
                    return null;
                }
                return viewModel.StatusMessage;
            }, viewModel.Strings);
        }

        private async void TrackMenuRemoteClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not ReferenceExplorerViewModel viewModel)
            {
                return;
            }
            GitRemoteBranch branch = null;
            if (sender is MenuItem item)
            {
                branch = (item.Tag as ReferenceTreeNode)?.Target as GitRemoteBranch;
                if (branch == null)
                {
                    branch = (item.DataContext as ReferenceTreeNode)?.Target as GitRemoteBranch;
                }
            }
            if (branch == null)
            {
                branch = _menuNode?.Target as GitRemoteBranch;
            }
            string repositoryRoot = (sender as MenuItem)?.CommandParameter as string ?? _menuRepositoryRoot ?? viewModel.CurrentRepository?.RootPath;
            await CheckoutRemoteBranchAsync(viewModel, branch, repositoryRoot);
        }

        private async Task CheckoutRemoteBranchAsync(ReferenceExplorerViewModel viewModel, GitRemoteBranch branch, string repositoryRoot)
        {
            bool showDialog = await viewModel.PrepareRemoteBranchCheckoutAsync(repositoryRoot, branch);
            if (showDialog == false)
            {
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }
            string warning = viewModel.RemoteCheckoutWarning;
            string suggestedName = branch.Name;
            if (warning.Length > 0)
            {
                suggestedName = string.Empty;
            }

            await GitActionDialogs.RequestNewBranchAsync(owner, viewModel.ReferenceText("ReferenceCheckoutRemoteBranch"), branch.FullName, suggestedName, async name =>
            {
                bool created = await viewModel.TrackMenuRemoteAsync(repositoryRoot, branch, name);
                if (created == true)
                {
                    return null;
                }
                return viewModel.StatusMessage;
            }, viewModel.Strings, true, warning);
        }

        private async void CopyTreeNameClicked(object sender, RoutedEventArgs eventArgs)
        {
            IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }
            ReferenceTreeNode node = _menuNode;
            if (node == null)
            {
                if (sender is MenuItem item)
                {
                    node = item.DataContext as ReferenceTreeNode;
                }
            }
            if (node == null)
            {
                return;
            }

            string name = node.Label;
            if (node.Target is GitRemoteBranch branch)
            {
                name = branch.FullName;
            }
            await clipboard.SetTextAsync(name);
        }
    }
}
