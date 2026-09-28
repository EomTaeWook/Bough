using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class PullRequestLauncher
    {
        public void Open(GitRemote remote, string sourceBranch)
        {
            Uri address = CreateAddress(remote, sourceBranch);
            Process process;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = address.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Win32Exception exception)
            {
                throw new GitException("PullRequestBrowserStartFailed", exception, address.AbsoluteUri);
            }
            if (process == null)
            {
                throw new GitException("PullRequestBrowserStartFailed", null, address.AbsoluteUri);
            }
            process.Dispose();
        }

        public Uri CreateAddress(GitRemote remote, string sourceBranch)
        {
            ArgumentNullException.ThrowIfNull(remote);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceBranch);
            if (TryParseRemote(remote.Url, out Uri repositoryAddress, out string repositoryPath) == false)
            {
                throw new GitException("PullRequestProviderUnsupported", null, remote.Url);
            }

            string targetBranch = GetTargetBranch(remote, sourceBranch);
            if (IsGitHub(repositoryAddress.Host) == true)
            {
                string comparePath = $"/{EscapePath(repositoryPath)}/compare/{Uri.EscapeDataString(targetBranch)}...{Uri.EscapeDataString(sourceBranch)}";
                UriBuilder builder = new(repositoryAddress.Scheme, repositoryAddress.Host, repositoryAddress.Port, comparePath)
                {
                    Query = "quick_pull=1"
                };
                return builder.Uri;
            }
            if (IsGitLab(repositoryAddress.Host) == true)
            {
                string mergePath = $"/{EscapePath(repositoryPath)}/-/merge_requests/new";
                string query = $"merge_request%5Bsource_branch%5D={Uri.EscapeDataString(sourceBranch)}&merge_request%5Btarget_branch%5D={Uri.EscapeDataString(targetBranch)}";
                UriBuilder builder = new(repositoryAddress.Scheme, repositoryAddress.Host, repositoryAddress.Port, mergePath)
                {
                    Query = query
                };
                return builder.Uri;
            }
            throw new GitException("PullRequestProviderUnsupported", null, repositoryAddress.Host);
        }

        private static string GetTargetBranch(GitRemote remote, string sourceBranch)
        {
            if (remote.DefaultBranch.Length > 0 && remote.DefaultBranch != sourceBranch)
            {
                return remote.DefaultBranch;
            }
            GitRemoteBranch target = remote.Branches.FirstOrDefault(branch => branch.Name == "main" && branch.Name != sourceBranch);
            target ??= remote.Branches.FirstOrDefault(branch => branch.Name == "master" && branch.Name != sourceBranch);
            target ??= remote.Branches.FirstOrDefault(branch => branch.Name != sourceBranch);
            if (target == null)
            {
                throw new GitException("PullRequestTargetBranchMissing", null, remote.Name);
            }
            return target.Name;
        }

        private static bool TryParseRemote(string remoteUrl, out Uri repositoryAddress, out string repositoryPath)
        {
            repositoryAddress = null;
            repositoryPath = null;
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                return false;
            }

            string host;
            string path;
            int webPort = -1;
            string webScheme = Uri.UriSchemeHttps;
            if (remoteUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase) == true)
            {
                int separator = remoteUrl.IndexOf(':');
                if (separator < 5)
                {
                    return false;
                }
                host = remoteUrl[4..separator];
                path = remoteUrl[(separator + 1)..];
            }
            else
            {
                if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri remoteAddress) == false)
                {
                    return false;
                }
                if (remoteAddress.Scheme != Uri.UriSchemeHttps && remoteAddress.Scheme != Uri.UriSchemeHttp && remoteAddress.Scheme != "ssh")
                {
                    return false;
                }
                host = remoteAddress.Host;
                path = remoteAddress.AbsolutePath.Trim('/');
                if (remoteAddress.Scheme == Uri.UriSchemeHttp || remoteAddress.Scheme == Uri.UriSchemeHttps)
                {
                    webScheme = remoteAddress.Scheme;
                    webPort = remoteAddress.IsDefaultPort ? -1 : remoteAddress.Port;
                }
            }

            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase) == true)
            {
                path = path[..^4];
            }
            string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return false;
            }
            if (IsGitHub(host) == true && segments.Length != 2)
            {
                return false;
            }
            repositoryAddress = new UriBuilder(webScheme, host, webPort).Uri;
            repositoryPath = string.Join('/', segments);
            return true;
        }

        private static bool IsGitHub(string host)
        {
            return host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGitLab(string host)
        {
            return host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("gitlab.", StringComparison.OrdinalIgnoreCase)
                || host.Contains(".gitlab.", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapePath(string path)
        {
            IEnumerable<string> segments = path.Split('/').Select(Uri.EscapeDataString);
            return string.Join('/', segments);
        }
    }
}
