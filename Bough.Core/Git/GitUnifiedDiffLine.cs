namespace Bough.Core.Git
{
    public class GitUnifiedDiffLine
    {
        public GitUnifiedDiffLine(char kind, int oldLineNumber, int newLineNumber, string text)
        {
            Kind = kind;
            OldLineNumber = oldLineNumber;
            NewLineNumber = newLineNumber;
            Text = text;
        }

        public char Kind { get; }

        public int OldLineNumber { get; }

        public int NewLineNumber { get; }

        public string Text { get; }
    }
}
