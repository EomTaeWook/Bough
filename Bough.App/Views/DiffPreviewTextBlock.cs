using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Bough.App.Views
{
    public class DiffPreviewTextBlock : SelectableTextBlock
    {
        public static readonly StyledProperty<string> DiffTextProperty = AvaloniaProperty.Register<DiffPreviewTextBlock, string>(nameof(DiffText), string.Empty);
        public static readonly StyledProperty<bool> IsUnifiedDiffProperty = AvaloniaProperty.Register<DiffPreviewTextBlock, bool>(nameof(IsUnifiedDiff));

        public DiffPreviewTextBlock()
        {
            ActualThemeVariantChanged += (sender, eventArgs) => UpdateInlines();
        }

        public string DiffText
        {
            get { return GetValue(DiffTextProperty); }
            set { SetValue(DiffTextProperty, value); }
        }

        public bool IsUnifiedDiff
        {
            get { return GetValue(IsUnifiedDiffProperty); }
            set { SetValue(IsUnifiedDiffProperty, value); }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == DiffTextProperty || change.Property == IsUnifiedDiffProperty)
            {
                UpdateInlines();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
        {
            base.OnAttachedToVisualTree(eventArgs);
            UpdateInlines();
        }

        private void UpdateInlines()
        {
            InlineCollection inlines = [];
            string text = DiffText;
            if (string.IsNullOrEmpty(text) == true)
            {
                Inlines = inlines;
                return;
            }

            if (IsUnifiedDiff == false)
            {
                inlines.Add(new Run(text));
                Inlines = inlines;
                return;
            }

            IBrush contextBrush = GetResourceBrush("BoughBrushText");
            IBrush addedBrush = GetResourceBrush("BoughBrushDiffAdded");
            IBrush removedBrush = GetResourceBrush("BoughBrushDiffRemoved");
            IBrush hunkBrush = GetResourceBrush("BoughBrushDiffHunk");
            StringBuilder segment = new();
            IBrush segmentBrush = null;
            bool inHunk = false;
            int position = 0;
            while (position < text.Length)
            {
                int lineEnd = text.IndexOf('\n', position);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                IBrush lineBrush = GetLineBrush(text, position, lineEnd - position, ref inHunk, contextBrush, addedBrush, removedBrush, hunkBrush);
                if (segmentBrush != lineBrush)
                {
                    AddSegment(inlines, segment, segmentBrush);
                    segmentBrush = lineBrush;
                }

                int nextPosition = lineEnd;
                if (nextPosition < text.Length)
                {
                    nextPosition++;
                }

                segment.Append(text, position, nextPosition - position);
                position = nextPosition;
            }

            AddSegment(inlines, segment, segmentBrush);
            Inlines = inlines;
        }

        private IBrush GetResourceBrush(string key)
        {
            if (this.TryFindResource(key, ActualThemeVariant, out object resource) == true)
            {
                if (resource is IBrush brush)
                {
                    return brush;
                }
            }

            return Foreground;
        }

        private static IBrush GetLineBrush(string text, int start, int length, ref bool inHunk, IBrush contextBrush, IBrush addedBrush, IBrush removedBrush, IBrush hunkBrush)
        {
            if (length >= 11 && string.CompareOrdinal(text, start, "diff --git ", 0, 11) == 0)
            {
                inHunk = false;
                return contextBrush;
            }

            if (length >= 2 && text[start] == '@' && text[start + 1] == '@')
            {
                inHunk = true;
                return hunkBrush;
            }

            if (inHunk == false || length == 0)
            {
                return contextBrush;
            }

            if (text[start] == '+')
            {
                return addedBrush;
            }

            if (text[start] == '-')
            {
                return removedBrush;
            }

            return contextBrush;
        }

        private static void AddSegment(InlineCollection inlines, StringBuilder segment, IBrush brush)
        {
            if (segment.Length == 0)
            {
                return;
            }

            inlines.Add(new Run(segment.ToString()) { Foreground = brush });
            segment.Clear();
        }
    }
}
