using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;

namespace Bough.App.Controls
{
    public class HistoryGraphControl : Control
    {
        public static readonly StyledProperty<HistoryGraphRow> RowProperty = AvaloniaProperty.Register<HistoryGraphControl, HistoryGraphRow>(nameof(Row));

        private static readonly string[] _laneBrushKeys = new string[]
        {
            "BoughBrushGraphOne",
            "BoughBrushGraphTwo",
            "BoughBrushGraphThree",
            "BoughBrushGraphFour"
        };
        private IBrush[] _laneBrushes;
        private IBrush _selectionBrush;
        private IBrush _surfaceBrush;

        public HistoryGraphControl()
        {
            ActualThemeVariantChanged += (sender, eventArgs) =>
            {
                _laneBrushes = null;
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
                StreamGeometry path = new();
                using (StreamGeometryContext geometry = path.Open())
                {
                    geometry.BeginFigure(new Point(x1, y1), false);
                    if (x1 == x2)
                    {
                        geometry.LineTo(new Point(x2, y2));
                    }
                    else
                    {
                        double middle = (y1 + y2) / 2;
                        geometry.CubicBezierTo(new Point(x1, middle), new Point(x2, middle), new Point(x2, y2));
                    }

                    geometry.EndFigure(false);
                }

                context.DrawGeometry(null, new Pen(GetBrush(segment.ColorIndex), 1.7), path);
            }

            Point center = new(LaneX(Row.NodeLane), height / 2);
            IBrush brush = GetBrush(Row.NodeColorIndex);
            context.DrawEllipse(_selectionBrush, null, center, 9, 9);
            if (Row.IsMerge == true)
            {
                context.DrawEllipse(_surfaceBrush, new Pen(brush, 2), center, 5, 5);
            }
            else
            {
                context.DrawEllipse(brush, new Pen(_surfaceBrush, 1), center, 4, 4);
            }
        }

        private static double LaneX(int lane)
        {
            return 16 + lane * 16;
        }

        private void EnsureBrushes()
        {
            if (_laneBrushes != null)
            {
                return;
            }
            _laneBrushes = new IBrush[_laneBrushKeys.Length];
            for (int index = 0; index < _laneBrushKeys.Length; index++)
            {
                _laneBrushes[index] = GetResourceBrush(_laneBrushKeys[index]);
            }
            _selectionBrush = GetResourceBrush("BoughBrushSelection");
            _surfaceBrush = GetResourceBrush("BoughBrushSurface");
        }

        private IBrush GetBrush(int colorIndex)
        {
            return _laneBrushes[colorIndex % _laneBrushes.Length];
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
