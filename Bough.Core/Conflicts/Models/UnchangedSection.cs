namespace Bough.Core.Conflicts.Models
{
    public class UnchangedSection : ConflictSection
    {
        public UnchangedSection(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }
}
