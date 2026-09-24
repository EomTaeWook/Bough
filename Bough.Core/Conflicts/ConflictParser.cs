using Bough.Core.Conflicts.Exceptions;
using Dignus.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bough.Core.Conflicts
{
    public class ConflictParser
    {
        public ConflictDocument Parse(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            var lines = SplitLines(text);
            ArrayQueue<ConflictSection> sections = [];
            StringBuilder unchanged = new();
            int lineIndex = 0;
            int hunkId = 0;

            while (lineIndex < lines.Count)
            {
                bool hasStartMarker = TryReadMarker(lines[lineIndex], "<<<<<<<", out string oursLabel);
                if (hasStartMarker == false)
                {
                    unchanged.Append(lines[lineIndex]);
                    lineIndex++;
                    continue;
                }

                FlushUnchanged(sections, unchanged);
                int startIndex = lineIndex;
                int startLine = lineIndex + 1;
                lineIndex++;

                string ours = ReadUntil(lines, ref lineIndex, "|||||||", "=======", out string middleMarker);
                bool hasBase = middleMarker == "|||||||";
                string baseLabel = string.Empty;
                string baseText = string.Empty;

                if (hasBase == true)
                {
                    TryReadMarker(lines[lineIndex], "|||||||", out baseLabel);
                    lineIndex++;
                    baseText = ReadUntil(lines, ref lineIndex, "=======", string.Empty, out _);
                }

                RequireMarker(lines, lineIndex, "=======", startLine);
                lineIndex++;
                string theirs = ReadUntil(lines, ref lineIndex, ">>>>>>>", string.Empty, out _);
                RequireMarker(lines, lineIndex, ">>>>>>>", startLine);
                TryReadMarker(lines[lineIndex], ">>>>>>>", out string theirsLabel);
                lineIndex++;

                string original = string.Concat(lines.Skip(startIndex).Take(lineIndex - startIndex));
                ConflictHunk hunk = new(hunkId, startLine, GetLabel(oursLabel, "Current change"), ours, hasBase, baseLabel, baseText, GetLabel(theirsLabel, "Incoming change"), theirs, original);
                sections.Add(hunk);
                hunkId++;
            }

            FlushUnchanged(sections, unchanged);
            return new ConflictDocument(sections);
        }

        private static string ReadUntil(ArrayQueue<string> lines, ref int lineIndex, string firstMarker, string secondMarker, out string foundMarker)
        {
            StringBuilder content = new();

            while (lineIndex < lines.Count)
            {
                if (TryReadMarker(lines[lineIndex], firstMarker, out _) == true)
                {
                    foundMarker = firstMarker;
                    return content.ToString();
                }

                if (secondMarker.Length > 0)
                {
                    if (TryReadMarker(lines[lineIndex], secondMarker, out _) == true)
                    {
                        foundMarker = secondMarker;
                        return content.ToString();
                    }
                }

                content.Append(lines[lineIndex]);
                lineIndex++;
            }

            foundMarker = string.Empty;
            return content.ToString();
        }

        private static void RequireMarker(ArrayQueue<string> lines, int lineIndex, string marker, int startLine)
        {
            if (lineIndex >= lines.Count)
            {
                throw new ConflictParseException($"Conflict beginning at line {startLine} has no {marker} marker.");
            }

            if (TryReadMarker(lines[lineIndex], marker, out _) == false)
            {
                throw new ConflictParseException($"Conflict beginning at line {startLine} has no {marker} marker.");
            }
        }

        private static bool TryReadMarker(string line, string marker, out string label)
        {
            string content = line.TrimEnd('\r', '\n');
            if (content.StartsWith(marker, StringComparison.Ordinal) == false)
            {
                label = string.Empty;
                return false;
            }

            if (content.Length > marker.Length)
            {
                if (char.IsWhiteSpace(content[marker.Length]) == false)
                {
                    label = string.Empty;
                    return false;
                }
            }

            label = content[marker.Length..].Trim();
            return true;
        }

        private ArrayQueue<string> SplitLines(string text)
        {
            ArrayQueue<string> lines = [];
            int start = 0;

            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] != '\n')
                {
                    continue;
                }

                lines.Add(text[start..(index + 1)]);
                start = index + 1;
            }

            if (start < text.Length)
            {
                lines.Add(text[start..]);
            }

            return lines;
        }

        private static void FlushUnchanged(ArrayQueue<ConflictSection> sections, StringBuilder unchanged)
        {
            if (unchanged.Length == 0)
            {
                return;
            }

            sections.Add(new UnchangedSection(unchanged.ToString()));
            unchanged.Clear();
        }

        private static string GetLabel(string label, string defaultLabel)
        {
            if (string.IsNullOrWhiteSpace(label) == true)
            {
                return defaultLabel;
            }

            return label;
        }
    }
}
