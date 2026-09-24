namespace Bough.App.ViewModels
{
    public class HistoryDiffLineItem
    {
        public HistoryDiffLineItem(string oldNumber, string newNumber, string text, char kind)
        {
            OldNumber = oldNumber;
            NewNumber = newNumber;
            Text = text;
            Kind = kind;
        }

        public string OldNumber { get; }
        public string NewNumber { get; }
        public string Text { get; }
        public char Kind { get; }
        public bool IsHunk { get { return Kind == 'H'; } }
        public bool IsAdded { get { return Kind == '+'; } }
        public bool IsRemoved { get { return Kind == '-'; } }
    }
}
