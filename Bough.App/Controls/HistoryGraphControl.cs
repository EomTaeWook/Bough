using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;

namespace Bough.App.Controls
{
    public class HistoryGraphControl : Control
    {
        private const double _laneStart = 16;
        private const double _laneSpacing = 18;

        public static readonly StyledProperty<HistoryGraphRow> RowProperty = AvaloniaProperty.Register<HistoryGraphControl, HistoryGraphRow>(nameof(Row));

        private static readonly string[] _laneBrushKeys = new string[]
        {
            "BoughBrushGraphOne",
            "BoughBrushGraphTwo",
            "BoughBrushGraphThree",
            "BoughBrushGraphFour",
            "BoughBrushGraphFive",
            "BoughBrushGraphSix",
            "BoughBrushGraphSeven",
            "BoughBrushGraphEight"
        };
        private IBrush[] _laneBrushes;
        private Pen[] _lanePens;
        private IBrush _focusBrush;
        private IBrush _surfaceBrush;

        public HistoryGraphControl()
        {
            ActualThemeVariantChanged += (sender, eventArgs) =>
            {
                _laneBrushes = null;
                _lanePens = null;
                InvalidateVisual();
            };
        }

        static HistoryGraphControl()
        {
            AffectsRender<HistoryGraphControl>(RowProperty);
        }

        public HistoryGraphRow Row
        {
            get { return GetValue(RowProperty); }
            set { SetValue(RowProperty, value); }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (Row == null)
            {
                return;
            }
            EnsureBrushes();

            double height = Bounds.Height;
            foreach (HistoryGraphSegment segment in Row.Segments)
            {
                double x1 = LaneX(segment.FromLane);
                double x2 = LaneX(segment.ToLane);
                double y1 = height * segment.FromLevel;
                double y2 = height * segment.ToLevel;
                Pen pen = GetPen(segment.ColorIndex);
                if (x1 == x2)
                {
                    context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                    continue;
                }

                StreamGeometry path = new();
                using (StreamGeometryContext geometry = path.Open())
                {
                    geometry.BeginFigure(new Point(x1, y1), false);
                    double firstTurn = y1 + (y2 - y1) * 0.42;
                    double secondTurn = y1 + (y2 - y1) * 0.58;
                    geometry.CubicBezierTo(new Point(x1, firstTurn), new Point(x2, secondTurn), new Point(x2, y2));

                    geometry.EndFigure(false);
                }

                context.DrawGeometry(null, pen, path);
            }

            Point center = new(LaneX(Row.NodeLane), height / 2);
            IBrush brush = GetBrush(Row.NodeColorIndex);
            if (Row.IsCurrentHead == true)
            {
                context.DrawEllipse(null, new Pen(_focusBrush, 2.5), center, 8, 8);
            }
            if (Row.IsMerge == true)
            {
                context.DrawEllipse(_surfaceBrush, new Pen(brush, 2), center, 5, 5);
                context.DrawEllipse(brush, null, center, 1.8, 1.8);
            }
            else if (Row.HasReferences == true)
            {
                context.DrawEllipse(brush, new Pen(_surfaceBrush, 1.2), center, 5, 5);
            }
            else
            {
                context.DrawEllipse(brush, new Pen(_surfaceBrush, 1), center, 4, 4);
            }
        }

        private static double LaneX(int lane)
        {
            return _laneStart + lane * _laneSpacing;
        }

        private void EnsureBrushes()
        {
            if (_laneBrushes != null)
            {
                return;
            }
            _laneBrushes = new IBrush[_laneBrushKeys.Length];
            _lanePens = new Pen[_laneBrushKeys.Length];
            for (int index = 0; index < _laneBrushKeys.Length; index++)
            {
                _laneBrushes[index] = GetResourceBrush(_laneBrushKeys[index]);
                _lanePens[index] = new Pen(_laneBrushes[index], 2);
            }
            _focusBrush = GetResourceBrush("BoughBrushFocus");
            _surfaceBrush = GetResourceBrush("BoughBrushSurface");
        }

        private IBrush GetBrush(int colorIndex)
        {
            return _laneBrushes[colorIndex % _laneBrushes.Length];
        }

        private Pen GetPen(int colorIndex)
        {
            return _lanePens[colorIndex % _lanePens.Length];
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
            return Brushes.Gray;
        }
    }
}
