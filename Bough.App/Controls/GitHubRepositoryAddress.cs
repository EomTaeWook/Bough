using System;

namespace Bough.App.Controls
{
    public static class GitHubRepositoryAddress
    {
        public static bool TryParse(string remoteUrl, out string owner, out string repository)
        {
            owner = null;
            repository = null;
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                return false;
            }

            string path;
            if (remoteUrl.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            {
                path = remoteUrl.Substring("git@github.com:".Length);
            }
            else
            {
                if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri address) == false)
                {
                    return false;
                }

                if (address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) == false)
                {
                    return false;
                }

                if (address.Scheme != Uri.UriSchemeHttps)
                {
                    if (address.Scheme != "ssh")
                    {
                        return false;
                    }

                    if (address.UserInfo != "git")
                    {
                        return false;
                    }
                }
                else if (address.UserInfo.Length > 0)
                {
                    return false;
                }

                if (address.Query.Length > 0)
                {
                    return false;
                }

                if (address.Fragment.Length > 0)
                {
                    return false;
                }

                path = address.AbsolutePath.Trim('/');
            }

            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - 4);
            }

            string[] segments = path.Split('/');
            if (segments.Length != 2)
            {
                return false;
            }

            if (IsValidUserName(segments[0]) == false)
            {
                return false;
            }

            if (IsValidRepositoryName(segments[1]) == false)
            {
                return false;
            }

            owner = segments[0];
            repository = segments[1];
            return true;
        }

        public static bool IsValidUserName(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return false;
            }

            if (userName.Length > 39)
            {
                return false;
            }

            if (char.IsLetterOrDigit(userName[0]) == false)
            {
                return false;
            }

            if (char.IsLetterOrDigit(userName[userName.Length - 1]) == false)
            {
                return false;
            }

            foreach (char character in userName)
            {
                if (IsAsciiLetterOrDigit(character) == true)
                {
                    continue;
                }

                if (character != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidRepositoryName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (name.Length > 100)
            {
                return false;
            }

            if (name == "." || name == "..")
            {
                return false;
            }

            foreach (char character in name)
            {
                if (IsAsciiLetterOrDigit(character) == true)
                {
                    continue;
                }

                if (character != '-' && character != '_' && character != '.')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiLetterOrDigit(char character)
        {
            if (character >= 'a' && character <= 'z')
            {
                return true;
            }

            if (character >= 'A' && character <= 'Z')
            {
                return true;
            }

            return character >= '0' && character <= '9';
        }
    }
}
