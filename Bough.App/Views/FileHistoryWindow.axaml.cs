using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;

namespace Bough.App.Views
{
    public partial class FileHistoryWindow : Window
    {
        private readonly FileHistoryPresenter _presenter;
        private readonly FileHistoryViewModel _model;
        private readonly StringHelper _strings;
        private readonly GitErrorLocalizer _errors;

        public FileHistoryWindow(FileHistoryPresenter presenter, StringHelper strings)
        {
            _presenter = presenter;
            _model = presenter.Model;
            _strings = strings;
            _errors = new GitErrorLocalizer(strings);
            InitializeComponent();
            DataContext = _model;
            _model.PropertyChanged += ModelChanged;
            _model.Entries.CollectionChanged += EntriesChanged;
            ActualThemeVariantChanged += ThemeChanged;
            LanguageChangeBinding.Bind(this, () => strings, RefreshDisplay);
            RefreshDisplay();
            Opened += async delegate { await _presenter.LoadAsync(); };
            Closed += delegate
            {
                _model.PropertyChanged -= ModelChanged;
                _model.Entries.CollectionChanged -= EntriesChanged;
                ActualThemeVariantChanged -= ThemeChanged;
                _presenter.Dispose();
            };
        }

        private void ModelChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(FileHistoryViewModel.SelectedEntry))
            {
                DiffScroller.Offset = new Vector(0, 0);
            }
            RefreshDisplay();
        }
        private void EntriesChanged(object sender, NotifyCollectionChangedEventArgs eventArgs) { RefreshDisplay(); }
        private void ThemeChanged(object sender, EventArgs eventArgs) { RefreshDisplay(); }

        private void RefreshDisplay()
        {
            Title = _strings.GetString("FileHistoryTitle");
            HeadingText.Text = Title;
            BaselineText.Text = _strings.Format("FileHistoryBaseline", _model.Repository.DisplayName, _model.RevisionHash.Substring(0, 8));
            RefreshLabel.Text = _strings.GetString("FileHistoryRefreshAction");
            CloseLabel.Text = _strings.GetString("FileHistoryCloseAction");
            CancelButton.Content = _strings.GetString("FileHistoryCancelAction");
            AutomationProperties.SetName(RefreshButton, RefreshLabel.Text);
            AutomationProperties.SetName(CloseButton, CloseLabel.Text);
            AutomationProperties.SetName(CancelButton, _strings.GetString("FileHistoryCancelAction"));
            AutomationProperties.SetName(FileCommits, _strings.GetString("FileHistoryCommitsAccessible"));
            AutomationProperties.SetName(DiffScroller, _strings.GetString("FileHistoryDiffAccessible"));
            AutomationProperties.SetName(PanelSplitter, _strings.GetString("FileHistoryResizePanels"));
            ToolTip.SetTip(RefreshButton, RefreshLabel.Text);
            ToolTip.SetTip(CancelButton, _strings.GetString("FileHistoryCancelAction"));
            ToolTip.SetTip(CloseButton, CloseLabel.Text);
            ToolTip.SetTip(PanelSplitter, _strings.GetString("FileHistoryResizePanels"));
            ToolTip.SetTip(BaselineText, _model.Repository.RootPath + " · " + _model.RevisionHash);
            CancelButton.IsVisible = _model.IsLoadingList || _model.IsLoadingDiff;
            LoadingProgress.IsVisible = CancelButton.IsVisible;
            ToolTip.SetTip(DateTimeline, _strings.GetString("FileHistoryTimelineTip"));
            AutomationProperties.SetName(DateTimeline, _strings.GetString("FileHistoryTimelineTip"));
            foreach (FileHistoryEntryItem entry in _model.Entries)
            {
                entry.SetStatusText(GetStatusText(entry.Status));
            }
            CountText.Text = _strings.Format("FileHistoryCount", _model.Entries.Count);
            ListMessage.Text = string.Empty;
            ListMessage.Foreground = GetBrush("BoughBrushTextMuted");
            if (_model.IsLoadingList)
            {
                ListMessage.Text = _strings.GetString("HistoryLoadingCommits");
            }
            else if (_model.ListError != null)
            {
                ListMessage.Text = _errors.GetDisplayMessage(_model.ListError);
                ListMessage.Foreground = GetBrush("BoughBrushError");
            }
            else if (_model.IsListCanceled)
            {
                ListMessage.Text = _strings.GetString("FileHistoryCanceled");
            }
            else if (_model.Entries.Count == 0)
            {
                ListMessage.Text = _strings.GetString("FileHistoryEmpty");
            }
            ListMessage.IsVisible = ListMessage.Text.Length > 0;
            FileHistoryEntryItem selected = _model.SelectedEntry;
            SelectedPathText.Text = _model.FilePath;
            SelectedTitleText.Text = string.Empty;
            SelectedMetadataText.Text = string.Empty;
            SelectionPositionText.Text = string.Empty;
            if (selected != null)
            {
                SelectedPathText.Text = selected.PathDescription;
                SelectedTitleText.Text = selected.Title;
                SelectedMetadataText.Text = _strings.Format("FileHistorySelectedCommit", selected.Author, selected.ShortHash, selected.Date);
                SelectionPositionText.Text = _strings.Format("FileHistoryTimelineSelection", _model.Entries.IndexOf(selected) + 1, _model.Entries.Count, selected.Date);
            }
            ToolTip.SetTip(SelectedPathText, SelectedPathText.Text);
            DiffMessage.Text = string.Empty;
            DiffMessage.Foreground = GetBrush("BoughBrushTextMuted");
            if (_model.IsLoadingDiff)
            {
                DiffMessage.Text = _strings.GetString("HistoryLoadingDiff");
            }
            else if (_model.DiffError != null)
            {
                DiffMessage.Text = _errors.GetDisplayMessage(_model.DiffError);
                DiffMessage.Foreground = GetBrush("BoughBrushError");
            }
            else if (_model.IsDiffCanceled)
            {
                DiffMessage.Text = _strings.GetString("FileHistoryCanceled");
            }
            else if (_model.Diff != null)
            {
                if (_model.Diff.ReasonCode.Length > 0)
                {
                    DiffMessage.Text = _strings.Format(_model.Diff.ReasonCode, _model.Diff.ReasonArguments.ToArray());
                }
                else if (_model.Diff.Hunks.Count == 0)
                {
                    DiffMessage.Text = _strings.GetString("FileHistoryMetadataOnly");
                }
            }
            else if (selected == null)
            {
                DiffMessage.Text = _strings.GetString("FileHistorySelectCommit");
            }
            DiffMessage.IsVisible = DiffMessage.Text.Length > 0;
            RefreshTimeline();
        }

        private string GetStatusText(string status)
        {
            if (status.Length == 0)
            {
                return string.Empty;
            }
            switch (status[0])
            {
                case 'A': return _strings.GetString("HistoryStatusAdded");
                case 'M': return _strings.GetString("HistoryStatusModified");
                case 'D': return _strings.GetString("HistoryStatusDeleted");
                case 'R': return _strings.GetString("HistoryStatusRenamed");
                case 'C': return _strings.GetString("HistoryStatusCopied");
                case 'T': return _strings.GetString("HistoryStatusTypeChanged");
                default: return status;
            }
        }

        private IBrush GetBrush(string key)
        {
            if (this.TryFindResource(key, ActualThemeVariant, out object resource) == false)
            {
                return Foreground;
            }
            if (resource is not IBrush brush)
            {
                return Foreground;
            }
            return brush;
        }

        private void TimelineSizeChanged(object sender, SizeChangedEventArgs eventArgs) { RefreshTimeline(); }

        private void RefreshTimeline()
        {
            DateTimeline.Children.Clear();
            FirstDateText.Text = string.Empty;
            LastDateText.Text = string.Empty;
            if (_model.Entries.Count == 0)
            {
                return;
            }
            DateTimeOffset first = _model.Entries.Min(item => item.Entry.AuthoredAt);
            DateTimeOffset last = _model.Entries.Max(item => item.Entry.AuthoredAt);
            FirstDateText.Text = first.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
            LastDateText.Text = last.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
            double width = Math.Max(0, DateTimeline.Bounds.Width - 16);
            Border axis = new() { Width = width, Height = 1, Background = GetBrush("BoughBrushBorder") };
            Canvas.SetLeft(axis, 8);
            Canvas.SetTop(axis, 18);
            DateTimeline.Children.Add(axis);
            double span = Math.Max(1, (last - first).TotalSeconds);
            foreach (FileHistoryEntryItem item in _model.Entries)
            {
                double height = 8;
                IBrush brush = GetBrush("BoughBrushTextSubtle");
                if (ReferenceEquals(item, _model.SelectedEntry))
                {
                    height = 18;
                    brush = GetBrush("BoughBrushAccent");
                }
                Border tick = new() { Width = 2, Height = height, Background = brush };
                Canvas.SetLeft(tick, 8 + (item.Entry.AuthoredAt - first).TotalSeconds / span * width);
                Canvas.SetTop(tick, 18 - height);
                DateTimeline.Children.Add(tick);
            }
        }

        private void TimelinePressed(object sender, PointerPressedEventArgs eventArgs)
        {
            if (eventArgs.GetCurrentPoint(DateTimeline).Properties.IsLeftButtonPressed == false)
            {
                return;
            }
            if (_model.Entries.Count == 0)
            {
                return;
            }
            DateTimeOffset first = _model.Entries.Min(item => item.Entry.AuthoredAt);
            DateTimeOffset last = _model.Entries.Max(item => item.Entry.AuthoredAt);
            double width = Math.Max(1, DateTimeline.Bounds.Width - 16);
            double position = Math.Clamp((eventArgs.GetPosition(DateTimeline).X - 8) / width, 0, 1);
            DateTimeOffset target = first.AddSeconds((last - first).TotalSeconds * position);
            _model.SelectedEntry = _model.Entries.MinBy(item => Math.Abs((item.Entry.AuthoredAt - target).TotalSeconds));
            FileCommits.ScrollIntoView(_model.SelectedEntry);
            eventArgs.Handled = true;
        }

        private async void RefreshClicked(object sender, RoutedEventArgs eventArgs) { await _presenter.LoadAsync(); }
        private void CancelClicked(object sender, RoutedEventArgs eventArgs) { _presenter.CancelLoading(); }
        private void CloseClicked(object sender, RoutedEventArgs eventArgs) { Close(); }
    }
}
