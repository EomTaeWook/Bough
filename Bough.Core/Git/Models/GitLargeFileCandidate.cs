using System.Globalization;

namespace Bough.Core.Git
{
    public class GitLargeFileCandidate
    {
        public GitLargeFileCandidate(string path, long sizeBytes)
        {
            Path = path;
            SizeBytes = sizeBytes;
        }

        public string Path { get; }

        public long SizeBytes { get; }

        public string DisplaySize
        {
            get { return $"{SizeBytes / 1048576d:0.00} MiB · {SizeBytes.ToString("N0", CultureInfo.CurrentCulture)} B"; }
        }
    }
}
