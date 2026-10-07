namespace Bough.Core.Git.Models
{
    public class GitRevertParent
    {
        public GitRevertParent(int number, string hash, string subject)
        {
            Number = number;
            Hash = hash;
            Subject = subject;
        }

        public int Number { get; }
        public string Hash { get; }
        public string Subject { get; }
    }
}
