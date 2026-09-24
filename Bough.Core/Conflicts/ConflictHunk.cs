namespace Bough.Core.Conflicts
{
    public class ConflictHunk : ConflictSection
    {
        public ConflictHunk(int id, int startLine, string oursLabel, string oursText, bool hasBase, string baseLabel, string baseText, string theirsLabel, string theirsText, string originalText)
        {
            Id = id;
            StartLine = startLine;
            OursLabel = oursLabel;
            OursText = oursText;
            HasBase = hasBase;
            BaseLabel = baseLabel;
            BaseText = baseText;
            TheirsLabel = theirsLabel;
            TheirsText = theirsText;
            OriginalText = originalText;
        }

        public int Id { get; }

        public int StartLine { get; }

        public string OursLabel { get; }

        public string OursText { get; }

        public bool HasBase { get; }

        public string BaseLabel { get; }

        public string BaseText { get; }

        public string TheirsLabel { get; }

        public string TheirsText { get; }

        public string OriginalText { get; }
    }
}
