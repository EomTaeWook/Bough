using System;
using System.IO;
using System.Text.Json;

namespace Bough.App.Controls
{
    public class GitHubAuthorLinkSettings
    {
        private readonly string _filePath;

        public GitHubAuthorLinkSettings()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bough", "github-author-link.json"))
        {
        }

        public GitHubAuthorLinkSettings(string filePath)
        {
            _filePath = filePath;
        }

        public static event Action Changed;

        public GitHubAuthorLink Load()
        {
            try
            {
                if (File.Exists(_filePath) == false)
                {
                    return new GitHubAuthorLink();
                }

                GitHubAuthorLink link = JsonSerializer.Deserialize<GitHubAuthorLink>(File.ReadAllText(_filePath));
                if (link == null)
                {
                    return new GitHubAuthorLink();
                }

                if (IsValidEmail(link.AuthorEmail) == false)
                {
                    return new GitHubAuthorLink();
                }

                if (GitHubRepositoryAddress.IsValidUserName(link.UserName) == false)
                {
                    return new GitHubAuthorLink();
                }

                link.AuthorEmail = link.AuthorEmail.Trim();
                return link;
            }
            catch (Exception exception) when (exception is JsonException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return new GitHubAuthorLink();
            }
        }

        public void Save(string authorEmail, string userName)
        {
            if (string.IsNullOrWhiteSpace(authorEmail) == true)
            {
                if (string.IsNullOrWhiteSpace(userName) == true)
                {
                    if (File.Exists(_filePath) == true)
                    {
                        File.Delete(_filePath);
                    }

                    Changed?.Invoke();
                    return;
                }
            }

            if (IsValidEmail(authorEmail) == false)
            {
                throw new ArgumentException("A valid Git author email is required.", nameof(authorEmail));
            }

            if (GitHubRepositoryAddress.IsValidUserName(userName) == false)
            {
                throw new ArgumentException("A valid GitHub user name is required.", nameof(userName));
            }

            string directory = Path.GetDirectoryName(_filePath);
            if (directory == null)
            {
                throw new IOException("The local settings path has no directory.");
            }

            Directory.CreateDirectory(directory);
            GitHubAuthorLink link = new() { AuthorEmail = authorEmail.Trim(), UserName = userName };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(link));
            Changed?.Invoke();
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) == true)
            {
                return false;
            }

            string normalized = email.Trim();
            if (normalized.Length > 254)
            {
                return false;
            }

            if (normalized.IndexOf('@') <= 0)
            {
                return false;
            }

            if (normalized.IndexOf('@') == normalized.Length - 1)
            {
                return false;
            }

            foreach (char character in normalized)
            {
                if (char.IsWhiteSpace(character) == true)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
