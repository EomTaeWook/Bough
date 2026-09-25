namespace Bough.Core.Git.Models
{
    public class GitConflictFile
    {
        public GitConflictFile(string relativePath, string workingText, string baseText, string oursText, string theirsText, string oursSource, string theirsSource, string originalContentHash, bool hasUtf8Bom)
        {
            RelativePath = relativePath;
            WorkingText = workingText;
            BaseText = baseText;
            OursText = oursText;
            TheirsText = theirsText;
            OursSource = oursSource;
            TheirsSource = theirsSource;
            OriginalContentHash = originalContentHash;
            HasUtf8Bom = hasUtf8Bom;
        }

        public string RelativePath { get; }

        public string WorkingText { get; }

        public string BaseText { get; }

        public string OursText { get; }

        public string TheirsText { get; }

        public string OursSource { get; }

        public string TheirsSource { get; }

        public string OriginalContentHash { get; }

        public bool HasUtf8Bom { get; }
    }
}
