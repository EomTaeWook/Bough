using System.Collections.Generic;

namespace Bough.App.ViewModels
{
    public class HistoryGraphSegment
    {
        public HistoryGraphSegment(int fromLane, int toLane, double fromLevel, double toLevel, int colorIndex)
        {
            FromLane = fromLane;
            ToLane = toLane;
            FromLevel = fromLevel;
            ToLevel = toLevel;
            ColorIndex = colorIndex;
        }

        public int FromLane { get; }
        public int ToLane { get; }
        public double FromLevel { get; }
        public double ToLevel { get; }
        public int ColorIndex { get; }
    }

    public class HistoryGraphRow
    {
        public HistoryGraphRow(int nodeLane, int nodeColorIndex, bool isMerge, IReadOnlyList<HistoryGraphSegment> segments)
        {
            NodeLane = nodeLane;
            NodeColorIndex = nodeColorIndex;
            IsMerge = isMerge;
            Segments = segments;
        }

        public int NodeLane { get; }
        public int NodeColorIndex { get; }
        public bool IsMerge { get; }
        public IReadOnlyList<HistoryGraphSegment> Segments { get; }
    }
}
