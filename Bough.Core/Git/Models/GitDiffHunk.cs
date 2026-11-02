namespace Bough.Core.Git.Models
{
    public class GitDiffHunk
    {
        internal GitDiffHunk(int start, int length)
        {
            Start = start;
            Length = length;
        }

        internal int Start { get; }

        internal int Length { get; }
    }
}
