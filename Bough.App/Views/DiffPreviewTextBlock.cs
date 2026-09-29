using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace Bough.App.Views
{
    public class DiffPreviewTextBlock : SelectableTextBlock
    {
        public static readonly StyledProperty<string> DiffTextProperty = AvaloniaProperty.Register<DiffPreviewTextBlock, string>(nameof(DiffText), string.Empty);
        public static readonly StyledProperty<bool> IsUnifiedDiffProperty = AvaloniaProperty.Register<DiffPreviewTextBlock, bool>(nameof(IsUnifiedDiff));
        public static readonly StyledProperty<string> PreviousLineNumbersTextProperty = AvaloniaProperty.Register<DiffPreviewTextBlock, string>(nameof(PreviousLineNumbersText), string.Empty);
        public static readonly StyledProperty<string> CurrentLineNumbersTextProperty = AvaloniaProperty.Register<DiffPreviewTextBlock, string>(nameof(CurrentLineNumbersText), string.Empty);

        private readonly List<int> _lineStarts = [];
        private readonly List<int> _lineEnds = [];
        private Point _pointerPressPosition;
        private bool _pointerPressed;
        private bool _pointerExtendSelection;
        private bool _selectionByUser;
        private int _anchorLineIndex = -1;
        private int _selectedLineIndex = -1;

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

        public string PreviousLineNumbersText
        {
            get { return GetValue(PreviousLineNumbersTextProperty); }
            private set { SetValue(PreviousLineNumbersTextProperty, value); }
        }

        public string CurrentLineNumbersText
        {
            get { return GetValue(CurrentLineNumbersTextProperty); }
            private set { SetValue(CurrentLineNumbersTextProperty, value); }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == DiffTextProperty)
            {
                ResetLineSelection();
                UpdateInlines();
                return;
            }
            if (change.Property == IsUnifiedDiffProperty)
            {
                ResetLineSelection();
                UpdateInlines();
                return;
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
        {
            base.OnAttachedToVisualTree(eventArgs);
            UpdateInlines();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
        {
            base.OnPointerPressed(eventArgs);
            if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
            {
                return;
            }

            _selectedLineIndex = -1;
            _pointerPressPosition = eventArgs.GetPosition(this);
            _pointerExtendSelection = (eventArgs.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
            _pointerPressed = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
        {
            base.OnPointerReleased(eventArgs);
            if (_pointerPressed == false)
            {
                return;
            }

            _pointerPressed = false;
            Point releasedPosition = eventArgs.GetPosition(this);
            if (Math.Abs(releasedPosition.X - _pointerPressPosition.X) > 3)
            {
                _anchorLineIndex = -1;
                _selectionByUser = SelectionStart != SelectionEnd;
                return;
            }
            if (Math.Abs(releasedPosition.Y - _pointerPressPosition.Y) > 3)
            {
                _anchorLineIndex = -1;
                _selectionByUser = SelectionStart != SelectionEnd;
                return;
            }

            SelectLineAt(releasedPosition.Y, _pointerExtendSelection);
        }

        protected override void OnKeyDown(KeyEventArgs eventArgs)
        {
            base.OnKeyDown(eventArgs);
            if (SelectionStart != SelectionEnd)
            {
                _selectionByUser = true;
            }
        }

        public void SelectLineAt(double position, bool extendSelection)
        {
            if (LineHeight <= 0)
            {
                return;
            }

            int index = (int)Math.Floor(position / LineHeight);
            if (index < 0)
            {
                return;
            }
            if (index >= _lineStarts.Count)
            {
                return;
            }
            if (_lineStarts[index] < 0)
            {
                ResetLineSelection();
                return;
            }

            if (extendSelection == false)
            {
                _anchorLineIndex = index;
            }
            if (_anchorLineIndex < 0)
            {
                _anchorLineIndex = index;
            }
            if (_anchorLineIndex >= _lineStarts.Count)
            {
                _anchorLineIndex = index;
            }

            _selectedLineIndex = index;
            _selectionByUser = true;
            SelectLineRange();
            Focus();
        }

        private void ResetLineSelection()
        {
            _anchorLineIndex = -1;
            _selectedLineIndex = -1;
            _selectionByUser = false;
            _pointerPressed = false;
            _pointerExtendSelection = false;
            SelectionStart = 0;
            SelectionEnd = 0;
        }

        private void SelectLineRange()
        {
            int firstLine = Math.Min(_anchorLineIndex, _selectedLineIndex);
            int lastLine = Math.Max(_anchorLineIndex, _selectedLineIndex);
            SelectionStart = _lineStarts[firstLine];
            SelectionEnd = _lineEnds[lastLine];
        }

        public bool TryGetSelectedCodeLine(out string codeText)
        {
            codeText = string.Empty;
            if (_selectedLineIndex < 0)
            {
                return false;
            }
            if (_selectedLineIndex >= _lineStarts.Count)
            {
                return false;
            }
            if (_lineStarts[_selectedLineIndex] < 0)
            {
                return false;
            }
            if (SelectionStart != _lineStarts[_selectedLineIndex])
            {
                return false;
            }
            if (SelectionEnd != _lineEnds[_selectedLineIndex])
            {
                return false;
            }

            string selectedLine = SelectedText;
            if (selectedLine == null)
            {
                return false;
            }
            if (selectedLine.EndsWith("\n", StringComparison.Ordinal))
            {
                selectedLine = selectedLine.Substring(0, selectedLine.Length - 1);
            }
            if (selectedLine.EndsWith("\r", StringComparison.Ordinal))
            {
                selectedLine = selectedLine.Substring(0, selectedLine.Length - 1);
            }
            if (IsUnifiedDiff)
            {
                if (selectedLine.Length == 0)
                {
                    return false;
                }
                selectedLine = selectedLine.Substring(1);
            }

            codeText = selectedLine;
            return true;
        }

        private void UpdateInlines()
        {
            int selectionStart = SelectionStart;
            int selectionEnd = SelectionEnd;
            bool restoreLineSelection = IsSelectedLineRange(selectionStart, selectionEnd);
            InlineCollection inlines = [];
            _lineStarts.Clear();
            _lineEnds.Clear();
            string text = DiffText;
            if (string.IsNullOrEmpty(text) == true)
            {
                PreviousLineNumbersText = string.Empty;
                CurrentLineNumbersText = string.Empty;
                Inlines = inlines;
                RestoreSelection(selectionStart, selectionEnd, restoreLineSelection);
                return;
            }

            if (IsUnifiedDiff == false)
            {
                StringBuilder untrackedNumbers = new();
                int lineNumber = 1;
                int untrackedPosition = 0;
                while (untrackedPosition < text.Length)
                {
                    int lineEnd = text.IndexOf('\n', untrackedPosition);
                    if (lineEnd < 0)
                    {
                        lineEnd = text.Length;
                    }
                    int nextPosition = lineEnd;
                    if (nextPosition < text.Length)
                    {
                        nextPosition++;
                    }

                    _lineStarts.Add(untrackedPosition);
                    _lineEnds.Add(nextPosition);
                    AppendLineNumber(untrackedNumbers, lineNumber, lineEnd < text.Length);
                    lineNumber++;
                    untrackedPosition = nextPosition;
                }
                PreviousLineNumbersText = string.Empty;
                CurrentLineNumbersText = untrackedNumbers.ToString();
                inlines.Add(new Run(text));
                Inlines = inlines;
                RestoreSelection(selectionStart, selectionEnd, restoreLineSelection);
                return;
            }

            IBrush contextBrush = GetResourceBrush("BoughBrushText");
            IBrush addedBrush = GetResourceBrush("BoughBrushDiffAdded");
            IBrush removedBrush = GetResourceBrush("BoughBrushDiffRemoved");
            StringBuilder segment = new();
            StringBuilder previousNumbers = new();
            StringBuilder currentNumbers = new();
            IBrush segmentBrush = null;
            bool inHunk = false;
            int position = 0;
            int displayPosition = 0;
            int previousLine = 0;
            int currentLine = 0;
            while (position < text.Length)
            {
                int lineEnd = text.IndexOf('\n', position);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }
                int nextPosition = lineEnd;
                if (nextPosition < text.Length)
                {
                    nextPosition++;
                }

                int lineLength = lineEnd - position;
                if (lineLength >= 11)
                {
                    if (string.CompareOrdinal(text, position, "diff --git ", 0, 11) == 0)
                    {
                        inHunk = false;
                        position = nextPosition;
                        continue;
                    }
                }

                if (TryReadHunkHeader(text, position, lineLength, out int previousStart, out _, out int currentStart, out _))
                {
                    inHunk = true;
                    previousLine = previousStart;
                    currentLine = currentStart;
                    position = nextPosition;
                    continue;
                }

                IBrush lineBrush = contextBrush;
                if (inHunk == false)
                {
                    position = nextPosition;
                    continue;
                }
                if (lineLength == 0)
                {
                    position = nextPosition;
                    continue;
                }
                if (text[position] == '+')
                {
                    lineBrush = addedBrush;
                }
                else if (text[position] == '-')
                {
                    lineBrush = removedBrush;
                }
                else if (text[position] != ' ')
                {
                    position = nextPosition;
                    continue;
                }

                if (segmentBrush != lineBrush)
                {
                    AddSegment(inlines, segment, segmentBrush);
                    segmentBrush = lineBrush;
                }

                segment.Append(text, position, lineLength);
                int previousNumber = -1;
                int currentNumber = -1;
                if (text[position] != '+')
                {
                    previousNumber = previousLine;
                    previousLine++;
                }
                if (text[position] != '-')
                {
                    currentNumber = currentLine;
                    currentLine++;
                }
                _lineStarts.Add(displayPosition);
                AppendLineNumber(previousNumbers, previousNumber, lineEnd < text.Length);
                AppendLineNumber(currentNumbers, currentNumber, lineEnd < text.Length);
                if (lineEnd < text.Length)
                {
                    segment.Append('\n');
                }
                displayPosition += lineLength;
                if (lineEnd < text.Length)
                {
                    displayPosition++;
                }
                _lineEnds.Add(displayPosition);
                position = nextPosition;
            }

            AddSegment(inlines, segment, segmentBrush);
            PreviousLineNumbersText = previousNumbers.ToString();
            CurrentLineNumbersText = currentNumbers.ToString();
            Inlines = inlines;
            RestoreSelection(selectionStart, selectionEnd, restoreLineSelection);
        }

        private bool IsSelectedLineRange(int selectionStart, int selectionEnd)
        {
            if (_selectionByUser == false)
            {
                return false;
            }
            if (_anchorLineIndex < 0)
            {
                return false;
            }
            if (_selectedLineIndex < 0)
            {
                return false;
            }
            if (_anchorLineIndex >= _lineStarts.Count)
            {
                return false;
            }
            if (_selectedLineIndex >= _lineStarts.Count)
            {
                return false;
            }

            int firstLine = Math.Min(_anchorLineIndex, _selectedLineIndex);
            int lastLine = Math.Max(_anchorLineIndex, _selectedLineIndex);
            if (selectionStart != _lineStarts[firstLine])
            {
                return false;
            }
            return selectionEnd == _lineEnds[lastLine];
        }

        private void RestoreSelection(int selectionStart, int selectionEnd, bool restoreLineSelection)
        {
            if (restoreLineSelection)
            {
                RestoreLineSelection();
                if (_selectedLineIndex >= 0)
                {
                    return;
                }
                ResetLineSelection();
                return;
            }
            if (_selectionByUser)
            {
                if (selectionStart != selectionEnd)
                {
                    _anchorLineIndex = -1;
                    _selectedLineIndex = -1;
                    int textLength = 0;
                    if (_lineEnds.Count > 0)
                    {
                        textLength = _lineEnds[_lineEnds.Count - 1];
                    }
                    SelectionStart = Math.Clamp(selectionStart, 0, textLength);
                    SelectionEnd = Math.Clamp(selectionEnd, 0, textLength);
                    return;
                }
            }

            ResetLineSelection();
        }

        private static void AppendLineNumber(StringBuilder numbers, int number, bool hasLineEnding)
        {
            if (number >= 0)
            {
                numbers.Append(number.ToString(CultureInfo.InvariantCulture));
            }
            if (hasLineEnding)
            {
                numbers.Append('\n');
            }
        }

        private void RestoreLineSelection()
        {
            if (_selectedLineIndex < 0)
            {
                return;
            }
            if (_selectedLineIndex >= _lineStarts.Count)
            {
                _selectedLineIndex = -1;
                return;
            }
            if (_lineStarts[_selectedLineIndex] < 0)
            {
                _selectedLineIndex = -1;
                return;
            }

            if (_anchorLineIndex < 0)
            {
                _anchorLineIndex = _selectedLineIndex;
            }
            if (_anchorLineIndex >= _lineStarts.Count)
            {
                _anchorLineIndex = _selectedLineIndex;
            }
            if (_lineStarts[_anchorLineIndex] < 0)
            {
                _anchorLineIndex = _selectedLineIndex;
            }

            SelectLineRange();
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

        private static bool TryReadHunkHeader(string text, int start, int length, out int previousStart, out int previousCount, out int currentStart, out int currentCount)
        {
            previousStart = 0;
            previousCount = 0;
            currentStart = 0;
            currentCount = 0;
            int end = start + length;
            if (length < 10)
            {
                return false;
            }
            if (string.CompareOrdinal(text, start, "@@ -", 0, 4) != 0)
            {
                return false;
            }
            int position = start + 4;
            if (TryReadNumber(text, ref position, end, out previousStart) == false)
            {
                return false;
            }
            previousCount = 1;
            if (position < end && text[position] == ',')
            {
                position++;
                if (TryReadNumber(text, ref position, end, out previousCount) == false)
                {
                    return false;
                }
            }
            if (position + 1 >= end)
            {
                return false;
            }
            if (text[position] != ' ')
            {
                return false;
            }
            if (text[position + 1] != '+')
            {
                return false;
            }
            position += 2;
            if (TryReadNumber(text, ref position, end, out currentStart) == false)
            {
                return false;
            }
            currentCount = 1;
            if (position < end && text[position] == ',')
            {
                position++;
                if (TryReadNumber(text, ref position, end, out currentCount) == false)
                {
                    return false;
                }
            }
            if (position + 2 >= end)
            {
                return false;
            }
            if (string.CompareOrdinal(text, position, " @@", 0, 3) != 0)
            {
                return false;
            }

            return true;
        }

        private static bool TryReadNumber(string text, ref int position, int end, out int number)
        {
            int start = position;
            while (position < end && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
            if (position == start)
            {
                number = 0;
                return false;
            }

            return int.TryParse(text.AsSpan(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out number);
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
