using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.Core.Git;

namespace Bough.App.Views
{
    public class RemoteOperationTarget
    {
        public RemoteOperationTarget(string remote, string branch)
        {
            Remote = remote;
            Branch = branch;
        }

        public string Remote { get; }
        public string Branch { get; }
    }

    public class RemoteTargetDialogs
    {
        public static async Task<RemoteOperationTarget> SelectPullAsync(Window owner, RemoteOperationsViewModel viewModel, string strategy, GitErrorLocalizer errorLocalizer)
        {
            GitRepository repository = viewModel.CurrentRepository;
            string currentBranch = viewModel.CurrentBranchText;
            string upstream = viewModel.UpstreamText;
            Window dialog = CreateWindow("Choose Pull source");
            TextBlock current = new() { Text = $"Current branch: {currentBranch}" };
            TextBlock remoteLabel = new() { Text = "Remote" };
            ComboBox remote = new() { ItemsSource = viewModel.Remotes, MinWidth = 250 };
            TextBlock branchLabel = new() { Text = "Existing remote branch" };
            ComboBox branch = new() { MinWidth = 250, IsEnabled = false };
            TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
            TextBlock error = CreateError();
            Button cancel = new() { Content = "Cancel", IsCancel = true };
            Button confirm = new() { Content = $"Pull · {strategy}", IsDefault = true, IsEnabled = false };
            int selectionRequest = 0;

            bool IsContextCurrent()
            {
                if (viewModel.CurrentRepository != repository)
                {
                    return false;
                }
                if (viewModel.CurrentBranchText != currentBranch)
                {
                    return false;
                }
                if (viewModel.UpstreamText != upstream)
                {
                    return false;
                }
                return true;
            }

            void UpdateSummary()
            {
                if (remote.SelectedItem is not string selectedRemote)
                {
                    confirm.IsEnabled = false;
                    summary.Text = "Choose a remote and an existing branch.";
                    return;
                }
                if (branch.SelectedItem is not string selectedBranch)
                {
                    confirm.IsEnabled = false;
                    summary.Text = "Choose a remote and an existing branch.";
                    return;
                }
                summary.Text = $"{selectedRemote}/{selectedBranch} → {currentBranch}";
                confirm.IsEnabled = false;
                if (IsContextCurrent() == false)
                {
                    return;
                }
                if (viewModel.CanPull == false)
                {
                    return;
                }
                confirm.IsEnabled = true;
            }

            remote.SelectionChanged += async delegate
            {
                int request = ++selectionRequest;
                branch.ItemsSource = null;
                branch.SelectedItem = null;
                branch.IsEnabled = false;
                error.IsVisible = false;
                UpdateSummary();
                if (remote.SelectedItem is not string selectedRemote)
                {
                    return;
                }
                try
                {
                    IReadOnlyList<string> choices = await viewModel.GetRemoteBranchesAsync(selectedRemote);
                    if (request != selectionRequest)
                    {
                        return;
                    }
                    if (IsContextCurrent() == false)
                    {
                        error.Text = "The selected repository changed. Open the selection again.";
                        error.IsVisible = true;
                        return;
                    }
                    branch.ItemsSource = choices;
                    branch.IsEnabled = choices.Count > 0;
                    if (choices.Count == 0)
                    {
                        error.Text = "No remote branches are available. Fetch this remote first.";
                        error.IsVisible = true;
                    }
                    if (selectedRemote == viewModel.UpstreamRemote)
                    {
                        if (choices.Contains(viewModel.UpstreamBranch))
                        {
                            branch.SelectedItem = viewModel.UpstreamBranch;
                        }
                    }
                    UpdateSummary();
                }
                catch (Exception exception)
                {
                    if (request != selectionRequest)
                    {
                        return;
                    }
                    error.Text = errorLocalizer.GetDisplayMessage(exception);
                    error.IsVisible = true;
                }
            };
            branch.SelectionChanged += delegate { UpdateSummary(); };
            cancel.Click += delegate { dialog.Close(null); };
            confirm.Click += delegate
            {
                if (IsContextCurrent() == false)
                {
                    error.Text = "The selected repository changed. Open the selection again.";
                    error.IsVisible = true;
                    return;
                }
                if (remote.SelectedItem is not string selectedRemote)
                {
                    return;
                }
                if (branch.SelectedItem is not string selectedBranch)
                {
                    return;
                }
                dialog.Close(new RemoteOperationTarget(selectedRemote, selectedBranch));
            };
            dialog.Closed += delegate { selectionRequest++; };
            dialog.Content = CreateContent(current, remoteLabel, remote, branchLabel, branch, summary, error, CreateButtons(cancel, confirm));
            if (viewModel.HasUpstream)
            {
                remote.SelectedItem = viewModel.UpstreamRemote;
            }
            else if (viewModel.Remotes.Count == 1)
            {
                remote.SelectedItem = viewModel.Remotes[0];
            }
            UpdateSummary();
            return await dialog.ShowDialog<RemoteOperationTarget>(owner);
        }

        public static async Task<RemoteOperationTarget> SelectPushAsync(Window owner, RemoteOperationsViewModel viewModel, GitErrorLocalizer errorLocalizer)
        {
            GitRepository repository = viewModel.CurrentRepository;
            string currentBranch = viewModel.CurrentBranchText;
            string upstream = viewModel.UpstreamText;
            Window dialog = CreateWindow("Confirm Push destination");
            TextBlock current = new() { Text = $"Current branch: {currentBranch}" };
            TextBlock remoteLabel = new() { Text = "Destination remote" };
            ComboBox remote = new() { ItemsSource = viewModel.Remotes, MinWidth = 250 };
            TextBlock branchLabel = new() { Text = "Destination branch" };
            string initialBranch = viewModel.CurrentBranchText;
            if (viewModel.HasUpstream == true)
            {
                initialBranch = viewModel.UpstreamBranch;
            }
            TextBox branch = new() { Text = initialBranch, MinWidth = 250 };
            TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
            TextBlock effect = new() { TextWrapping = TextWrapping.Wrap };
            TextBlock error = CreateError();
            Button cancel = new() { Content = "Cancel", IsCancel = true };
            Button confirm = new() { Content = "Confirm Push", IsDefault = true };
            bool validating = false;

            bool IsContextCurrent()
            {
                if (viewModel.CurrentRepository != repository)
                {
                    return false;
                }
                if (viewModel.CurrentBranchText != currentBranch)
                {
                    return false;
                }
                if (viewModel.UpstreamText != upstream)
                {
                    return false;
                }
                return true;
            }

            void UpdateSummary()
            {
                string selectedRemote = remote.SelectedItem as string;
                string selectedBranch = branch.Text?.Trim() ?? string.Empty;
                summary.Text = $"{currentBranch} → {selectedRemote ?? "(choose remote)"}/{selectedBranch}";
                if (viewModel.HasUpstream)
                {
                    effect.Text = $"Existing upstream remains {viewModel.UpstreamText}.";
                }
                else
                {
                    effect.Text = "A successful first Push will set this destination as upstream.";
                }
                confirm.IsEnabled = false;
                if (validating == true)
                {
                    return;
                }
                if (selectedRemote == null)
                {
                    return;
                }
                if (selectedBranch.Length == 0)
                {
                    return;
                }
                if (IsContextCurrent() == false)
                {
                    return;
                }
                if (viewModel.CanPushTo == false)
                {
                    return;
                }
                confirm.IsEnabled = true;
            }

            remote.SelectionChanged += delegate { error.IsVisible = false; UpdateSummary(); };
            branch.TextChanged += delegate { error.IsVisible = false; UpdateSummary(); };
            cancel.Click += delegate { dialog.Close(null); };
            confirm.Click += async delegate
            {
                if (validating)
                {
                    return;
                }
                if (remote.SelectedItem is not string selectedRemote)
                {
                    return;
                }
                string selectedBranch = branch.Text?.Trim() ?? string.Empty;
                if (selectedBranch.Length == 0)
                {
                    return;
                }
                validating = true;
                remote.IsEnabled = false;
                branch.IsEnabled = false;
                cancel.IsEnabled = false;
                UpdateSummary();
                try
                {
                    await viewModel.ValidatePushTargetAsync(selectedRemote, selectedBranch);
                    if (IsContextCurrent() == false)
                    {
                        error.Text = "The selected repository changed. Open the selection again.";
                        error.IsVisible = true;
                        return;
                    }
                    dialog.Close(new RemoteOperationTarget(selectedRemote, selectedBranch));
                }
                catch (Exception exception)
                {
                    error.Text = errorLocalizer.GetDisplayMessage(exception);
                    error.IsVisible = true;
                }
                finally
                {
                    validating = false;
                    remote.IsEnabled = true;
                    branch.IsEnabled = true;
                    cancel.IsEnabled = true;
                    UpdateSummary();
                }
            };
            dialog.Content = CreateContent(current, remoteLabel, remote, branchLabel, branch, summary, effect, error, CreateButtons(cancel, confirm));
            if (viewModel.HasUpstream)
            {
                remote.SelectedItem = viewModel.UpstreamRemote;
            }
            else if (viewModel.Remotes.Count == 1)
            {
                remote.SelectedItem = viewModel.Remotes[0];
            }
            UpdateSummary();
            return await dialog.ShowDialog<RemoteOperationTarget>(owner);
        }

        private static Window CreateWindow(string title)
        {
            return new Window
            {
                Title = title,
                Width = 460,
                MinHeight = 220,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
        }

        private static TextBlock CreateError()
        {
            TextBlock error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            error.Classes.Add("boughError");
            return error;
        }

        private static StackPanel CreateContent(params Control[] controls)
        {
            StackPanel content = new() { Margin = new Thickness(20), Spacing = 10 };
            foreach (Control control in controls)
            {
                content.Children.Add(control);
            }
            return content;
        }

        private static StackPanel CreateButtons(Button cancel, Button confirm)
        {
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            return buttons;
        }
    }
}
