using System;
using System.Globalization;

namespace Bough.Core.Updates.Models
{
    public class ApplicationReleaseVersion : IComparable<ApplicationReleaseVersion>
    {
        private ApplicationReleaseVersion(int major, int minor, int patch, string prerelease, string text)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
            Text = text;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string Prerelease { get; }
        public string Text { get; }
        public bool IsStable { get { return Prerelease.Length == 0; } }

        public static bool TryParse(string text, out ApplicationReleaseVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string value = text.Trim();
            if (value.StartsWith("v", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }
            int metadata = value.IndexOf('+');
            if (metadata >= 0)
            {
                value = value.Substring(0, metadata);
            }
            string prerelease = string.Empty;
            string numbers = value;
            int separator = value.IndexOf('-');
            if (separator >= 0)
            {
                prerelease = value.Substring(separator + 1);
                if (prerelease.Length == 0)
                {
                    return false;
                }
                foreach (char character in prerelease)
                {
                    if (char.IsAsciiLetterOrDigit(character) || character == '-' || character == '.')
                    {
                        continue;
                    }
                    return false;
                }
                numbers = value.Substring(0, separator);
            }
            string[] parts = numbers.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }
            int[] parsed = new int[3];
            for (int index = 0; index < parts.Length; index++)
            {
                if (parts[index].Length == 0)
                {
                    return false;
                }
                if (parts[index].Length > 1)
                {
                    if (parts[index][0] == '0')
                    {
                        return false;
                    }
                }
                if (int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[index]) == false)
                {
                    return false;
                }
            }
            version = new ApplicationReleaseVersion(parsed[0], parsed[1], parsed[2], prerelease, value);
            return true;
        }

        public int CompareTo(ApplicationReleaseVersion other)
        {
            if (other == null)
            {
                return 1;
            }
            int comparison = Major.CompareTo(other.Major);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = Minor.CompareTo(other.Minor);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = Patch.CompareTo(other.Patch);
            if (comparison != 0)
            {
                return comparison;
            }
            if (IsStable == other.IsStable)
            {
                return 0;
            }
            if (IsStable)
            {
                return 1;
            }
            return -1;
        }
    }

    public class ApplicationUpdateEnvironment
    {
        public ApplicationUpdateEnvironment(string version, string executablePath, bool publishedSingleFile)
        {
            Version = version;
            ExecutablePath = executablePath;
            PublishedSingleFile = publishedSingleFile;
        }

        public string Version { get; }
        public string ExecutablePath { get; }
        public bool PublishedSingleFile { get; }
    }

    public class ApplicationUpdateRelease
    {
        internal ApplicationUpdateRelease(string version, string tag, Uri page, Uri asset, string digest, long size)
        {
            Version = version;
            Tag = tag;
            ReleasePage = page;
            AssetUrl = asset;
            Sha256 = digest;
            Size = size;
        }

        public string Version { get; }
        public string Tag { get; }
        public Uri ReleasePage { get; }
        internal Uri AssetUrl { get; }
        public string Sha256 { get; }
        public long Size { get; }
        public bool HasDigest { get { return Sha256 != null; } }
    }

    public class ApplicationUpdateCheck
    {
        internal ApplicationUpdateCheck(string currentVersion, ApplicationUpdateRelease release, bool hasUpdate, string installBlockedCode)
        {
            CurrentVersion = currentVersion;
            Release = release;
            HasUpdate = hasUpdate;
            InstallBlockedCode = installBlockedCode;
        }

        public string CurrentVersion { get; }
        public ApplicationUpdateRelease Release { get; }
        public bool HasStableRelease { get { return Release != null; } }
        public bool HasUpdate { get; }
        public string InstallBlockedCode { get; }
        public bool CanInstall { get { return HasUpdate && InstallBlockedCode == null; } }
    }

    public class ApplicationUpdateProgress
    {
        internal ApplicationUpdateProgress(long bytesReceived, long totalBytes)
        {
            BytesReceived = bytesReceived;
            TotalBytes = totalBytes;
        }

        public long BytesReceived { get; }
        public long TotalBytes { get; }
        public double Percentage { get { return BytesReceived * 100.0 / TotalBytes; } }
    }

    public class VerifiedApplicationUpdate
    {
        internal VerifiedApplicationUpdate(ApplicationUpdateRelease release, string directory, string path)
        {
            Release = release;
            Directory = directory;
            FilePath = path;
        }

        public ApplicationUpdateRelease Release { get; }
        internal string Directory { get; }
        internal string FilePath { get; }
    }
}