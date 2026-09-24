namespace Bough.Core.Git
{
    public class GitFilePreview
    {
        public GitFilePreview(string text, string description, bool isBinary, long size)
        {
            Text = text;
            Description = description;
            IsBinary = isBinary;
            Size = size;
        }

        public string Text { get; }

        public string Description { get; }

        public bool IsBinary { get; }

        public long Size { get; }
    }
}
