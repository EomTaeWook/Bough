using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.Core.Git;
using DataContainer.Generated;
using Bough.Core.Git.Models;
using Bough.Core.Internals;
using Bough.App.Internals;

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
            if (stringHelper != null)
            {
                return stringHelper.GetString(name);
            }
            StringTemplate template = TemplateContainer<StringTemplate>.Find(name);
            if (template == null)
            {
                return name;
            }
            if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko")
            {
                return template.Kor;
            }
            return template.Eng;
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
            Grid content = new() { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12 };
            StackPanel buttons = CreateButtons(cancel, confirm);
            Grid.SetRow(buttons, 1);
            content.Children.Add(description);
            content.Children.Add(buttons);
            dialog.Content = content;
            return await dialog.ShowDialog<bool>(owner);
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
            if (stringHelper == null)
            {
                StringLanguage language = StringLanguage.English;
                if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko")
                {
                    language = StringLanguage.Korean;
                }
                stringHelper = new StringHelper(new StringLanguageSelection(language));
            }
            return new GitErrorLocalizer(stringHelper).GetDisplayMessage(exception);
        }

        private static Window CreateWindow(string title)
        {
            return new Window
            {
                Title = title,
                Width = 480,
                MinHeight = 170,
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

        private static StackPanel CreateContent(params Control[] controls)
        {
            StackPanel content = new() { Margin = new Avalonia.Thickness(20), Spacing = 12 };
            foreach (Control control in controls)
            {
                content.Children.Add(control);
            }
            return content;
        }

        private static StackPanel CreateButtons(Button cancel, Button confirm)
        {
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            return buttons;
        }
    }
}
