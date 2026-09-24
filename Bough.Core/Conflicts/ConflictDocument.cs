using Bough.Core.Internals;
using Dignus.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bough.Core.Conflicts
{
    public class ConflictDocument
    {
        private readonly ArrayQueue<ConflictSection> _sections;
        private readonly ArrayQueue<ConflictHunk> _hunks;
        private readonly IReadOnlyList<ConflictSection> _sectionView;
        private readonly IReadOnlyList<ConflictHunk> _hunkView;

        public ConflictDocument(IEnumerable<ConflictSection> sections)
        {
            _sections = [.. sections];
            _hunks = [.. _sections.OfType<ConflictHunk>()];
            _sectionView = new ReadOnlyQueueView<ConflictSection>(_sections);
            _hunkView = new ReadOnlyQueueView<ConflictHunk>(_hunks);
        }

        public IReadOnlyList<ConflictSection> Sections
        {
            get
            {
                return _sectionView;
            }
        }

        public IReadOnlyList<ConflictHunk> Hunks
        {
            get
            {
                return _hunkView;
            }
        }

        public string Render(IReadOnlyDictionary<int, ResolutionChoiceType> choices)
        {
            StringBuilder result = new();

            foreach (ConflictSection section in _sections)
            {
                UnchangedSection unchangedSection = section as UnchangedSection;
                if (unchangedSection != null)
                {
                    result.Append(unchangedSection.Text);
                    continue;
                }

                ConflictHunk hunk = (ConflictHunk)section;
                var choice = ResolutionChoiceType.Unresolved;
                if (choices.TryGetValue(hunk.Id, out ResolutionChoiceType selectedChoice) == true)
                {
                    choice = selectedChoice;
                }

                switch (choice)
                {
                    case ResolutionChoiceType.Ours:
                        result.Append(hunk.OursText);
                        break;
                    case ResolutionChoiceType.Theirs:
                        result.Append(hunk.TheirsText);
                        break;
                    case ResolutionChoiceType.Both:
                        result.Append(Combine(hunk.OursText, hunk.TheirsText));
                        break;
                    case ResolutionChoiceType.Remove:
                        break;
                    default:
                        result.Append(hunk.OriginalText);
                        break;
                }
            }

            return result.ToString();
        }

        private static string Combine(string ours, string theirs)
        {
            if (ours.Length == 0)
            {
                return theirs;
            }

            if (theirs.Length == 0)
            {
                return ours;
            }

            if (EndsWithLineBreak(ours) == true)
            {
                return ours + theirs;
            }

            string newLine = "\n";
            if (ours.Contains("\r\n", StringComparison.Ordinal) == true)
            {
                newLine = "\r\n";
            }

            return ours + newLine + theirs;
        }

        private static bool EndsWithLineBreak(string text)
        {
            if (text.EndsWith('\n') == true)
            {
                return true;
            }

            return text.EndsWith('\r');
        }

        private class ReadOnlyQueueView<T> : IReadOnlyList<T>
        {
            private readonly ArrayQueue<T> _queue;

            public ReadOnlyQueueView(ArrayQueue<T> queue)
            {
                _queue = queue;
            }

            public int Count => _queue.Count;

            public T this[int index] => _queue[index];

            public IEnumerator<T> GetEnumerator() => _queue.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
