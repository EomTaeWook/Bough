using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Views
{
    public class GitResetChoice
    {
        public GitResetChoice(GitResetMode mode)
        {
            Mode = mode;
        }

        public GitResetMode Mode { get; }
    }

    public class GitActionDialogs
    {
        public static string TagText(string name, StringHelper stringHelper = null)
        {
            return ResolveStrings(stringHelper).GetString(name);
        }

        private static StringHelper ResolveStrings(StringHelper stringHelper)
        {
            if (stringHelper != null)
            {
                return stringHelper;
            }
            if (Application.Current is App app)
            {
                if (app.Strings != null)
                {
                    return app.Strings;
                }
            }
            throw new ArgumentNullException(nameof(stringHelper));
        }

        public static string FormatTagText(string name, string value, StringHelper stringHelper = null)
        {
            return string.Format(CultureInfo.CurrentCulture, TagText(name, stringHelper), value);
        }

        public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string confirmText, StringHelper stringHelper = null)
        {
            Window dialog = CreateWindow(title);
            TextBlock description = new() { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Button cancel = new() { Content = TagText("ReferenceCancel", stringHelper) };
            Button confirm = new() { Content = confirmText };
            cancel.Click += delegate { dialog.Close(false); };
            confirm.Click += delegate { dialog.Close(true); };
            dialog.Content = CreateContent(description, CreateButtons(cancel, confirm));
            return await dialog.ShowDialog<bool>(owner);
        }

        public static async Task<bool> RequestTagDeletionAsync(Window owner, GitTag tag, string[] remoteNames,
            Func<string, CancellationToken, Task<GitRemoteTagDeletionPreview>> lookup,
            Func<Task<string>> deleteLocal, Func<GitRemoteTagDeletionPreview, Task<string>> deleteRemote, StringHelper stringHelper)
        {
            Window dialog = CreateWindow(TagText("TagDeleteLocalMenu", stringHelper));
            dialog.MaxHeight = 640;
            dialog.CanResize = true;
            RadioButton localScope = new() { Content = TagText("TagDeleteDialogLocalScope", stringHelper), GroupName = "TagDeletionScope", IsChecked = true };
            RadioButton remoteScope = new() { Content = TagText("TagDeleteDialogRemoteScope", stringHelper), GroupName = "TagDeletionScope" };
            StackPanel scopes = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            scopes.Children.Add(localScope);
            scopes.Children.Add(remoteScope);
            TextBlock localImpact = new()
            {
                Text = stringHelper.Format("TagDeleteLocalConfirm", tag.Name, tag.ObjectId),
                TextWrapping = TextWrapping.Wrap
            };
            TextBlock remoteHint = new() { Text = TagText("TagDeleteDialogRemoteHint", stringHelper), TextWrapping = TextWrapping.Wrap };
            TextBlock remoteLabel = new() { Text = TagText("TagDeleteSelectRemoteTitle", stringHelper) };
            ComboBox remotes = new() { ItemsSource = remoteNames, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(remotes, TagText("TagDeleteSelectRemoteTitle", stringHelper));
            ToolTip.SetTip(remotes, TagText("TagDeleteSelectRemoteTitle", stringHelper));
            Button reload = new() { Content = TagText("TagDeleteDialogReload", stringHelper), Height = 32, MinWidth = 80 };
            Grid remoteSelection = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(reload, 1);
            remoteSelection.Children.Add(remotes);
            remoteSelection.Children.Add(reload);
            TextBlock remoteTarget = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            CheckBox remoteConfirmation = new() { IsVisible = false };
            TextBlock confirmationLabel = new() { TextWrapping = TextWrapping.Wrap };
            remoteConfirmation.Content = confirmationLabel;
            StackPanel remoteContent = new() { Spacing = 8, IsVisible = false };
            remoteContent.Children.Add(remoteHint);
            remoteContent.Children.Add(remoteLabel);
            remoteContent.Children.Add(remoteSelection);
            remoteContent.Children.Add(remoteTarget);
            remoteContent.Children.Add(remoteConfirmation);
            TextBlock progress = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            TextBlock error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            Button cancel = new() { Content = TagText("ReferenceCancel", stringHelper), IsCancel = true };
            Button delete = new() { Content = TagText("TagDeleteAction", stringHelper), IsDefault = true };
            delete.Classes.Add("primary");
            string initialRemote = string.Empty;
            if (remoteNames.Length > 0)
            {
                initialRemote = remoteNames[0];
            }
            TagDeletionDialogPresenter presenter = new(initialRemote, lookup, deleteLocal, deleteRemote);

            void Render()
            {
                TagDeletionDialogModel state = presenter.State;
                bool editable = state.IsDeleting == false;
                localScope.IsEnabled = editable;
                remoteScope.IsEnabled = editable;
                cancel.IsEnabled = editable;
                localImpact.IsVisible = state.IsRemoteMode == false;
                remoteContent.IsVisible = state.IsRemoteMode;
                remotes.IsEnabled = editable;
                reload.IsEnabled = editable;
                if (state.IsLookingUp)
                {
                    reload.IsEnabled = false;
                }
                if (remoteNames.Length == 0)
                {
                    remotes.IsEnabled = false;
                    reload.IsEnabled = false;
                }
                remoteHint.IsVisible = state.RemotePreview == null;
                remoteTarget.IsVisible = state.RemotePreview != null;
                remoteConfirmation.IsVisible = state.RemotePreview != null;
                remoteConfirmation.IsEnabled = editable;
                remoteConfirmation.IsChecked = state.RemoteConfirmed;
                remoteTarget.Text = string.Empty;
                confirmationLabel.Text = string.Empty;
                if (state.RemotePreview != null)
                {
                    GitRemoteTagDeletionPreview preview = state.RemotePreview;
                    remoteTarget.Text = stringHelper.Format("TagDeleteRemoteConfirm", preview.RemoteName, preview.TagName, preview.ObjectId);
                    confirmationLabel.Text = stringHelper.Format("TagDeleteDialogConfirmRemote", preview.RemoteName, preview.TagName);
                    Avalonia.Automation.AutomationProperties.SetName(remoteConfirmation, confirmationLabel.Text);
                }
                progress.Text = string.Empty;
                if (state.IsLookingUp)
                {
                    progress.Text = stringHelper.Format("TagDeleteDialogLoading", state.RemoteName, tag.Name);
                }
                if (state.IsDeleting)
                {
                    progress.Text = TagText("TagDeleteDialogDeleting", stringHelper);
                }
                progress.IsVisible = string.IsNullOrEmpty(progress.Text) == false;
                error.Text = state.FailureMessage ?? string.Empty;
                if (state.Error != null)
                {
                    error.Text = DisplayFailure(state.Error, stringHelper);
                }
                if (state.IsRemoteMode)
                {
                    if (remoteNames.Length == 0)
                    {
                        error.Text = TagText("TagDeleteRemoteRequired", stringHelper);
                    }
                }
                error.IsVisible = string.IsNullOrEmpty(error.Text) == false;
                error.Foreground = GetResourceBrush(dialog, "BoughBrushError");
                progress.Foreground = GetResourceBrush(dialog, "BoughBrushTextMuted");
                delete.IsEnabled = state.CanDelete;
                ApplyPrimaryButtonColors(dialog, delete, true);
            }

            presenter.StateChanged += Render;
            localScope.PropertyChanged += async delegate(object sender, AvaloniaPropertyChangedEventArgs eventArgs)
            {
                if (eventArgs.Property != Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty)
                {
                    return;
                }
                if (localScope.IsChecked != true)
                {
                    return;
                }
                await presenter.SelectScopeAsync(false);
            };
            remoteScope.PropertyChanged += async delegate(object sender, AvaloniaPropertyChangedEventArgs eventArgs)
            {
                if (eventArgs.Property != Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty)
                {
                    return;
                }
                if (remoteScope.IsChecked != true)
                {
                    return;
                }
                await presenter.SelectScopeAsync(true);
            };
            remotes.SelectionChanged += async delegate { await presenter.SelectRemoteAsync(remotes.SelectedItem as string); };
            reload.Click += async delegate { await presenter.ReloadAsync(); };
            remoteConfirmation.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs eventArgs)
            {
                if (eventArgs.Property != Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty)
                {
                    return;
                }
                presenter.ConfirmRemote(remoteConfirmation.IsChecked == true);
            };
            cancel.Click += delegate { dialog.Close(false); };
            delete.Click += async delegate
            {
                if (await presenter.DeleteAsync())
                {
                    dialog.Close(true);
                }
            };
            dialog.Closing += delegate(object sender, WindowClosingEventArgs eventArgs)
            {
                if (presenter.State.IsDeleting)
                {
                    eventArgs.Cancel = true;
                }
            };
            dialog.Closed += delegate { presenter.Close(); };
            Grid content = new() { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 16 };
            StackPanel body = new() { Spacing = 8 };
            body.Children.Add(scopes);
            body.Children.Add(localImpact);
            body.Children.Add(remoteContent);
            body.Children.Add(progress);
            body.Children.Add(error);
            ScrollViewer scroll = new() { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            StackPanel buttons = CreateButtons(cancel, delete);
            Grid.SetRow(buttons, 1);
            content.Children.Add(scroll);
            content.Children.Add(buttons);
            dialog.Content = content;
            dialog.Opened += delegate
            {
                Render();
                localScope.Focus();
            };
            dialog.ActualThemeVariantChanged += delegate { Render(); };
            return await dialog.ShowDialog<bool>(owner);
        }

        public static async Task<bool> RequestReferenceRenameAsync(Window owner, string title, string oldName, bool isTag,
            Func<string, Task<string>> submit, StringHelper stringHelper)
        {
            Window dialog = CreateWindow(title);
            TextBlock original = new() { Text = TagText("ReferenceRenameOriginalName", stringHelper) + ": " + oldName, TextWrapping = TextWrapping.Wrap };
            TextBlock scope = new() { Text = TagText("ReferenceRenameLocalOnly", stringHelper), TextWrapping = TextWrapping.Wrap };
            TextBlock tagHint = new() { Text = TagText("ReferenceRenameTagObjectsHint", stringHelper), IsVisible = isTag, TextWrapping = TextWrapping.Wrap };
            string nameLabel = TagText("ReferenceRenameNewName", stringHelper);
            TextBlock label = new() { Text = nameLabel };
            TextBox name = new() { Text = oldName, PlaceholderText = nameLabel };
            Avalonia.Automation.AutomationProperties.SetName(name, nameLabel);
            TextBlock error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            TextBlock progress = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            Button cancel = new() { Content = TagText("ReferenceCancel", stringHelper), IsCancel = true, MinWidth = 80 };
            Button rename = new() { Content = TagText("ReferenceRenameAction", stringHelper), IsDefault = true, MinWidth = 100 };
            rename.Classes.Add("primary");
            BranchCreationPresenter presenter = new(submit);

            void UpdateControls()
            {
                name.IsEnabled = presenter.IsInputClosed == false;
                cancel.IsEnabled = presenter.IsInputClosed == false;
                rename.IsEnabled = presenter.IsInputClosed == false && string.IsNullOrWhiteSpace(name.Text) == false;
                ApplyPrimaryButtonColors(dialog, rename, true);
                progress.IsVisible = presenter.ProcessingName != null;
                if (presenter.ProcessingName == null)
                {
                    return;
                }
                if (presenter.IsCancelRequested)
                {
                    progress.Text = stringHelper.Format("ReferenceRenameQueueClosing", presenter.ProcessingName);
                    return;
                }
                progress.Text = stringHelper.Format("ReferenceRenameQueueProgress", presenter.ProcessingName, presenter.PendingCount);
            }

            void CloseAfterRequests(bool result)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (dialog.IsVisible == false)
                    {
                        return;
                    }
                    dialog.Close(result);
                });
            }

            presenter.StateChanged += UpdateControls;
            presenter.RequestFailed += delegate(string requestedName, string failure, Exception exception)
            {
                if (dialog.IsVisible == false)
                {
                    return;
                }
                if (exception != null)
                {
                    failure = DisplayFailure(exception, stringHelper);
                }
                error.Text = requestedName + ": " + failure;
                error.IsVisible = true;
            };
            presenter.Finished += delegate(bool succeeded)
            {
                if (succeeded)
                {
                    CloseAfterRequests(true);
                    return;
                }
                if (presenter.IsCancelRequested)
                {
                    CloseAfterRequests(false);
                    return;
                }
                progress.IsVisible = false;
                name.Focus();
            };
            cancel.Click += delegate { presenter.CancelPending(); };
            dialog.Closing += delegate(object sender, WindowClosingEventArgs eventArgs)
            {
                if (presenter.IsRunning == false)
                {
                    return;
                }
                eventArgs.Cancel = true;
                presenter.CancelPending();
            };
            dialog.Closed += delegate { presenter.CancelPending(); };
            name.TextChanged += delegate
            {
                UpdateControls();
                error.IsVisible = false;
            };
            rename.Click += delegate
            {
                if (presenter.IsInputClosed)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    return;
                }
                presenter.Submit(name.Text.Trim());
            };
            void ApplyColors()
            {
                error.Foreground = GetResourceBrush(dialog, "BoughBrushError");
                progress.Foreground = GetResourceBrush(dialog, "BoughBrushTextMuted");
                ApplyPrimaryButtonColors(dialog, rename, true);
            }
            dialog.Content = CreateContent(original, scope, tagHint, label, name, progress, error, CreateButtons(cancel, rename));
            dialog.Opened += delegate
            {
                ApplyColors();
                name.Focus();
                name.SelectAll();
            };
            dialog.ActualThemeVariantChanged += delegate { ApplyColors(); };
            bool result = await dialog.ShowDialog<bool>(owner);
            await presenter.Completion;
            return result;
        }

        public static async Task<bool> RequestNewBranchAsync(Window owner, string title, string startPoint, string suggestedName, Func<string, Task<string>> submit, StringHelper stringHelper = null, bool remoteCheckout = false, string warning = null)
        {
            Window dialog = CreateWindow(title);
            string namePlaceholder = TagText("ReferenceBranchName", stringHelper);
            string startLabel = TagText("ReferenceStartPoint", stringHelper);
            string cancelText = TagText("ReferenceCancel", stringHelper);
            string createText = TagText("ReferenceCreateAndSwitch", stringHelper);
            string descriptionText = string.Empty;
            if (remoteCheckout)
            {
                startLabel = TagText("RemoteCheckoutSourceLabel", stringHelper);
                namePlaceholder = TagText("RemoteCheckoutLocalName", stringHelper);
                createText = TagText("RemoteCheckoutAction", stringHelper);
                descriptionText = TagText("RemoteCheckoutDescription", stringHelper);
                dialog.Width = 440;
            }
            TextBlock description = new() { Text = descriptionText, TextWrapping = TextWrapping.Wrap, IsVisible = remoteCheckout };
            TextBlock start = new() { Text = $"{startLabel}: {startPoint}", TextWrapping = TextWrapping.Wrap };
            TextBlock nameLabel = new() { Text = namePlaceholder };
            TextBox name = new() { Text = suggestedName, PlaceholderText = namePlaceholder };
            TextBlock warningText = new() { Text = warning ?? string.Empty, TextWrapping = TextWrapping.Wrap, IsVisible = string.IsNullOrEmpty(warning) == false };
            TextBlock error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            TextBlock progress = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            dialog.Opened += delegate
            {
                ApplyMessageColors(dialog, warningText, error);
                progress.Foreground = GetResourceBrush(dialog, "BoughBrushTextMuted");
            };
            dialog.ActualThemeVariantChanged += delegate
            {
                ApplyMessageColors(dialog, warningText, error);
                progress.Foreground = GetResourceBrush(dialog, "BoughBrushTextMuted");
            };
            Button cancel = new() { Content = cancelText, IsCancel = true };
            Button create = new() { Content = createText, IsEnabled = string.IsNullOrWhiteSpace(suggestedName) == false, IsDefault = true };
            if (remoteCheckout)
            {
                cancel.MinWidth = 80;
                create.MinWidth = 132;
                create.Classes.Add("primary");
            }
            dialog.Opened += delegate { ApplyPrimaryButtonColors(dialog, create, remoteCheckout); };
            dialog.ActualThemeVariantChanged += delegate { ApplyPrimaryButtonColors(dialog, create, remoteCheckout); };
            BranchCreationPresenter presenter = new(submit);

            void UpdateControls()
            {
                name.IsEnabled = presenter.IsInputClosed == false;
                cancel.IsEnabled = presenter.IsInputClosed == false;
                create.IsEnabled = presenter.IsInputClosed == false && string.IsNullOrWhiteSpace(name.Text) == false;
                ApplyPrimaryButtonColors(dialog, create, remoteCheckout);
                if (presenter.ProcessingName == null)
                {
                    progress.IsVisible = false;
                    return;
                }
                if (presenter.IsCancelRequested)
                {
                    progress.Text = string.Format(CultureInfo.CurrentCulture, TagText("ReferenceBranchQueueClosing", stringHelper), presenter.ProcessingName);
                }
                else
                {
                    progress.Text = string.Format(CultureInfo.CurrentCulture, TagText("ReferenceBranchQueueProgress", stringHelper), presenter.ProcessingName, presenter.PendingCount);
                }
                progress.IsVisible = true;
            }

            void RequestClose()
            {
                presenter.CancelPending();
            }

            void CloseWhenConsumerFinishes(bool result)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (dialog.IsVisible == false)
                    {
                        return;
                    }
                    dialog.Close(result);
                });
            }

            presenter.StateChanged += UpdateControls;
            presenter.RequestFailed += delegate(string requestedName, string failure, Exception exception)
            {
                if (dialog.IsVisible == false)
                {
                    return;
                }
                if (exception != null)
                {
                    failure = DisplayFailure(exception, stringHelper);
                }
                error.Text = $"{requestedName}: {failure}";
                error.IsVisible = true;
            };
            presenter.Finished += delegate(bool succeeded)
            {
                if (succeeded)
                {
                    CloseWhenConsumerFinishes(true);
                    return;
                }
                if (presenter.IsCancelRequested)
                {
                    CloseWhenConsumerFinishes(false);
                    return;
                }
                progress.IsVisible = false;
                name.Focus();
            };

            cancel.Click += delegate { RequestClose(); };
            dialog.Closing += delegate(object sender, WindowClosingEventArgs eventArgs)
            {
                if (presenter.IsRunning == false)
                {
                    return;
                }
                if (presenter.ProcessingName == null)
                {
                    return;
                }
                eventArgs.Cancel = true;
                RequestClose();
            };
            dialog.Closed += delegate
            {
                presenter.CancelPending();
            };
            name.TextChanged += delegate
            {
                UpdateControls();
                error.IsVisible = false;
                warningText.IsVisible = false;
            };
            create.Click += delegate
            {
                if (presenter.IsInputClosed)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(name.Text) == true)
                {
                    return;
                }
                presenter.Submit(name.Text.Trim());
            };
            dialog.Content = CreateContent(description, start, nameLabel, name, warningText, progress, error, CreateButtons(cancel, create));
            dialog.Opened += delegate { name.Focus(); };
            bool result = await dialog.ShowDialog<bool>(owner);
            await presenter.Completion;
            return result;
        }

        public static async Task<bool> RequestTagAsync(Window owner, string target, Func<string, Task<string>> submit, StringHelper stringHelper = null)
        {
            Window dialog = CreateWindow(TagText("ReferenceCreateTag", stringHelper));
            dialog.Width = 420;
            TextBlock targetText = new() { Text = $"{TagText("ReferenceTagTarget", stringHelper)}: {target}", TextWrapping = TextWrapping.Wrap };
            TextBlock nameLabel = new() { Text = TagText("ReferenceTagName", stringHelper) };
            TextBox name = new() { PlaceholderText = TagText("ReferenceTagName", stringHelper) };
            TextBlock error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            Button cancel = new() { Content = TagText("ReferenceCancel", stringHelper), IsCancel = true, MinWidth = 80 };
            Button create = new() { Content = TagText("ReferenceTagAction", stringHelper), IsDefault = true, IsEnabled = false, MinWidth = 120 };
            create.Classes.Add("primary");
            TagCreationPresenter presenter = new(submit);
            dialog.Opened += delegate
            {
                error.Foreground = GetResourceBrush(dialog, "BoughBrushError");
                ApplyPrimaryButtonColors(dialog, create, true);
                name.Focus();
            };
            dialog.ActualThemeVariantChanged += delegate
            {
                error.Foreground = GetResourceBrush(dialog, "BoughBrushError");
                ApplyPrimaryButtonColors(dialog, create, true);
            };
            dialog.Closing += delegate(object sender, WindowClosingEventArgs eventArgs)
            {
                if (presenter.IsRunning)
                {
                    eventArgs.Cancel = true;
                }
            };
            cancel.Click += delegate { dialog.Close(false); };
            name.TextChanged += delegate
            {
                create.IsEnabled = presenter.IsRunning == false && string.IsNullOrWhiteSpace(name.Text) == false;
                ApplyPrimaryButtonColors(dialog, create, true);
                error.IsVisible = false;
            };
            create.Click += async delegate
            {
                if (presenter.IsRunning)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    return;
                }

                Task<TagCreationResult> operation = presenter.SubmitAsync(name.Text.Trim());
                create.IsEnabled = false;
                cancel.IsEnabled = false;
                name.IsEnabled = false;
                ApplyPrimaryButtonColors(dialog, create, true);
                TagCreationResult result = await operation;
                if (dialog.IsVisible == false)
                {
                    return;
                }
                if (result.Succeeded)
                {
                    dialog.Close(true);
                    return;
                }
                string failure = result.Failure;
                if (result.Error != null)
                {
                    failure = DisplayFailure(result.Error, stringHelper);
                }

                error.Text = failure;
                error.IsVisible = true;
                name.IsEnabled = true;
                cancel.IsEnabled = true;
                create.IsEnabled = string.IsNullOrWhiteSpace(name.Text) == false;
                ApplyPrimaryButtonColors(dialog, create, true);
                name.Focus();
            };
            dialog.Content = CreateContent(targetText, nameLabel, name, error, CreateButtons(cancel, create));
            return await dialog.ShowDialog<bool>(owner);
        }

        public static async Task<GitRevertChoice> RequestRevertAsync(Window owner, GitRevertPreview preview, StringHelper stringHelper)
        {
            Window dialog = CreateWindow(stringHelper.GetString("HistoryRevertTitle"));
            dialog.Width = 600;
            TextBlock target = new() { TextWrapping = TextWrapping.Wrap };
            TextBlock impact = new() { TextWrapping = TextWrapping.Wrap };
            bool isMerge = preview.Parents.Count > 1;
            TextBlock parentLabel = new() { IsVisible = isMerge };
            ComboBox parents = new() { SelectedIndex = -1, IsVisible = isMerge, HorizontalAlignment = HorizontalAlignment.Stretch };
            TextBlock warning = new() { TextWrapping = TextWrapping.Wrap, IsVisible = isMerge };
            Button cancel = new() { IsCancel = true };
            Button revert = new() { IsDefault = true, IsEnabled = isMerge == false };
            void RefreshLabels()
            {
                dialog.Title = stringHelper.GetString("HistoryRevertTitle");
                target.Text = stringHelper.Format("HistoryRevertTargetDescription", preview.RepositoryRoot,
                    preview.BranchName, preview.HeadHash, preview.TargetHash, preview.Subject);
                impact.Text = stringHelper.GetString("HistoryRevertImpact");
                parentLabel.Text = stringHelper.GetString("HistoryRevertParentLabel");
                warning.Text = stringHelper.GetString("HistoryRevertMergeWarning");
                cancel.Content = stringHelper.GetString("ReferenceCancel");
                revert.Content = stringHelper.GetString("HistoryRevertAction");
                Avalonia.Automation.AutomationProperties.SetName(parents, parentLabel.Text);
                int selected = parents.SelectedIndex;
                List<string> labels = new();
                foreach (GitRevertParent parent in preview.Parents)
                {
                    labels.Add(stringHelper.Format("HistoryRevertParentChoice", parent.Number, parent.Hash.Substring(0, 8), parent.Subject));
                }
                parents.ItemsSource = labels;
                parents.SelectedIndex = selected;
            }
            parents.SelectionChanged += delegate
            {
                if (isMerge)
                {
                    revert.IsEnabled = parents.SelectedIndex >= 0;
                }
            };
            cancel.Click += delegate { dialog.Close(null); };
            revert.Click += delegate
            {
                int mainline = 0;
                if (isMerge)
                {
                    if (parents.SelectedIndex < 0)
                    {
                        return;
                    }
                    mainline = parents.SelectedIndex + 1;
                }
                dialog.Close(new GitRevertChoice(mainline));
            };
            RefreshLabels();
            LanguageChangeBinding.Bind(dialog, () => stringHelper, RefreshLabels);
            dialog.Content = CreateContent(target, impact, parentLabel, parents, warning, CreateButtons(cancel, revert));
            return await dialog.ShowDialog<GitRevertChoice>(owner);
        }

        public static async Task<GitResetChoice> RequestResetAsync(Window owner, GitResetPreview preview, StringHelper stringHelper)
        {
            Window dialog = CreateWindow(TagText("GitResetTitle", stringHelper));
            TextBlock target = new()
            {
                Text = FormatText("GitResetTargetDescription", stringHelper, preview.BranchName, preview.ShortHash, preview.TargetSubject),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            TextBlock status = new() { Text = stringHelper.Format("CommitResetWorktreeSummary", preview.StagedCount, preview.WorkingCount, preview.UntrackedCount), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            string direction = TagText("GitResetAncestorDirection", stringHelper);
            if (preview.IsAncestor == false)
            {
                direction = TagText("GitResetDifferentDirection", stringHelper);
            }
            TextBlock movement = new() { Text = direction, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            string[] modeLabels = new string[] { TagText("GitResetModeSoft", stringHelper), TagText("GitResetModeMixed", stringHelper), TagText("GitResetModeHard", stringHelper) };
            ComboBox modes = new() { ItemsSource = modeLabels, SelectedIndex = 0 };
            TextBlock impact = new() { Text = TagText("GitResetImpactSoft", stringHelper), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            modes.SelectionChanged += delegate
            {
                if (modes.SelectedIndex == 1)
                {
                    impact.Text = TagText("GitResetImpactMixed", stringHelper);
                }
                if (modes.SelectedIndex == 2)
                {
                    impact.Text = TagText("GitResetImpactHard", stringHelper);
                }
                if (modes.SelectedIndex == 0)
                {
                    impact.Text = TagText("GitResetImpactSoft", stringHelper);
                }
            };
            Button cancel = new() { Content = TagText("ReferenceCancel", stringHelper) };
            Button reset = new() { Content = TagText("GitResetContinue", stringHelper) };
            cancel.Click += delegate { dialog.Close(null); };
            reset.Click += delegate { dialog.Close(new GitResetChoice((GitResetMode)modes.SelectedIndex)); };
            dialog.Content = CreateContent(target, status, movement, modes, impact, CreateButtons(cancel, reset));
            return await dialog.ShowDialog<GitResetChoice>(owner);
        }

        private static string FormatText(string name, StringHelper stringHelper, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, TagText(name, stringHelper), arguments);
        }

        private static string DisplayFailure(Exception exception, StringHelper stringHelper)
        {
            return new GitErrorLocalizer(ResolveStrings(stringHelper)).GetDisplayMessage(exception);
        }

        private static Window CreateWindow(string title)
        {
            return new Window
            {
                Title = title,
                Width = 480,
                MinHeight = 170,
                MaxHeight = 640,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
        }

        private static void ApplyMessageColors(Window dialog, TextBlock warning, TextBlock error)
        {
            warning.Foreground = GetResourceBrush(dialog, "BoughBrushWarning");
            error.Foreground = GetResourceBrush(dialog, "BoughBrushError");
        }

        private static void ApplyPrimaryButtonColors(Window dialog, Button create, bool isPrimary)
        {
            if (isPrimary == false)
            {
                return;
            }
            if (create.IsEnabled == false)
            {
                create.Background = GetResourceBrush(dialog, "BoughBrushDisabled");
                create.Foreground = GetResourceBrush(dialog, "BoughBrushDisabledText");
                create.BorderBrush = GetResourceBrush(dialog, "BoughBrushBorder");
                return;
            }

            create.Background = GetResourceBrush(dialog, "BoughBrushAccent");
            create.Foreground = GetResourceBrush(dialog, "BoughBrushAccentText");
            create.BorderBrush = GetResourceBrush(dialog, "BoughBrushAccent");
        }

        private static IBrush GetResourceBrush(Window dialog, string key)
        {
            if (dialog.TryFindResource(key, dialog.ActualThemeVariant, out object resource) == true)
            {
                if (resource is IBrush brush)
                {
                    return brush;
                }
            }
            return dialog.Foreground;
        }

        private static Grid CreateContent(params Control[] controls)
        {
            Grid content = new() { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 16 };
            StackPanel body = new() { Spacing = 8 };
            for (int index = 0; index < controls.Length - 1; index++)
            {
                body.Children.Add(controls[index]);
            }
            ScrollViewer scroll = new()
            {
                Content = body,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            Control buttons = controls[controls.Length - 1];
            Grid.SetRow(buttons, 1);
            content.Children.Add(scroll);
            content.Children.Add(buttons);
            return content;
        }

        private static StackPanel CreateButtons(Button cancel, Button confirm)
        {
            cancel.MinWidth = Math.Max(80, cancel.MinWidth);
            confirm.MinWidth = Math.Max(80, confirm.MinWidth);
            cancel.Height = Math.Max(32, cancel.MinHeight);
            confirm.Height = Math.Max(32, confirm.MinHeight);
            if (confirm.Classes.Contains("primary") == false)
            {
                confirm.Classes.Add("primary");
            }
            confirm.AttachedToVisualTree += delegate
            {
                if (TopLevel.GetTopLevel(confirm) is not Window dialog)
                {
                    return;
                }
                ApplyPrimaryButtonColors(dialog, confirm, true);
                dialog.ActualThemeVariantChanged += delegate { ApplyPrimaryButtonColors(dialog, confirm, true); };
            };
            confirm.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs eventArgs)
            {
                if (eventArgs.Property != Button.IsEnabledProperty)
                {
                    return;
                }
                if (TopLevel.GetTopLevel(confirm) is not Window dialog)
                {
                    return;
                }
                ApplyPrimaryButtonColors(dialog, confirm, true);
            };
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            return buttons;
        }
    }
}
