using System.Collections.Generic;
using Bough.Core.Git;
using Dignus.Collections;
using System.Linq;

namespace Bough.App.ViewModels
{
    public class HistoryGraphBuilder
    {
        private class ActiveLane
        {
            public ActiveLane(string hash, int colorIndex)
            {
                Hash = hash;
                ColorIndex = colorIndex;
            }

            public string Hash { get; }
            public int ColorIndex { get; }
        }

        public IReadOnlyList<HistoryGraphRow> Build(IReadOnlyList<GitHistoryCommit> commits)
        {
            return CreateCursor().Append(commits);
        }

        public Cursor CreateCursor()
        {
            return new Cursor();
        }

        public class Cursor
        {
            private List<ActiveLane> _active = [];
            private int _nextColor;

            public IReadOnlyList<HistoryGraphRow> Append(IReadOnlyList<GitHistoryCommit> commits)
            {
                ArrayQueue<HistoryGraphRow> rows = [];

                foreach (GitHistoryCommit commit in commits)
                {
                    int nodeLane = FindLane(_active, commit.Hash);
                    bool hasIncomingLine = nodeLane >= 0;
                    if (hasIncomingLine == false)
                    {
                        nodeLane = _active.Count;
                        _active.Add(new ActiveLane(commit.Hash, _nextColor));
                        _nextColor++;
                    }

                    ActiveLane current = _active[nodeLane];
                    List<ActiveLane> next = new(_active);
                    next.RemoveAt(nodeLane);
                    int insertionLane = nodeLane;
                    foreach (string parent in commit.Parents)
                    {
                        if (FindLane(next, parent) >= 0)
                        {
                            continue;
                        }

                        int colorIndex = current.ColorIndex;
                        if (parent != commit.Parents[0])
                        {
                            colorIndex = _nextColor;
                            _nextColor++;
                        }

                        next.Insert(insertionLane, new ActiveLane(parent, colorIndex));
                        insertionLane++;
                    }

                    ArrayQueue<HistoryGraphSegment> segments = [];
                    for (int lane = 0; lane < _active.Count; lane++)
                    {
                        ActiveLane before = _active[lane];
                        if (lane == nodeLane)
                        {
                            if (hasIncomingLine == true)
                            {
                                segments.Add(new HistoryGraphSegment(lane, lane, 0, 0.5, before.ColorIndex));
                            }

                            continue;
                        }

                        int afterLane = FindLane(next, before.Hash);
                        if (afterLane >= 0)
                        {
                            segments.Add(new HistoryGraphSegment(lane, afterLane, 0, 1, before.ColorIndex));
                        }
                    }

                    foreach (string parent in commit.Parents)
                    {
                        int afterLane = FindLane(next, parent);
                        if (afterLane >= 0)
                        {
                            segments.Add(new HistoryGraphSegment(nodeLane, afterLane, 0.5, 1, next[afterLane].ColorIndex));
                        }
                    }

                    rows.Add(new HistoryGraphRow(nodeLane, current.ColorIndex, commit.Parents.Count > 1, segments.ToArray()));
                    _active = next;
                }

                return rows.ToArray();
            }
        }

        private static int FindLane(IReadOnlyList<ActiveLane> lanes, string hash)
        {
            for (int index = 0; index < lanes.Count; index++)
            {
                if (lanes[index].Hash == hash)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
