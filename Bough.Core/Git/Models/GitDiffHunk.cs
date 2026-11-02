namespace Bough.Core.Git.Models
{
    public class GitDiffHunk
    {
        internal GitDiffHunk(string patch)
        {
            Patch = patch;
        }

        internal string Patch { get; }
    }
}
