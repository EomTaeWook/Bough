namespace Bough.Core.Git
{
    public class GitIgnoreEntry
    {
        public GitIgnoreEntry(string path, string rule)
        {
            Path = path;
            Rule = rule;
        }

        public string Path { get; }

        public string Rule { get; }
    }
}
