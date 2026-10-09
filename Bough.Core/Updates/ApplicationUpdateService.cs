using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Updates.Models;

namespace Bough.Core.Updates
{
    public class ApplicationUpdateService
    {
        private const string _latestApi = "https://api.github.com/repos/EomTaeWook/Bough/releases/latest";
        private const string _releasePrefix = "https://github.com/EomTaeWook/Bough/releases/tag/";
        private const string _assetPrefix = "https://github.com/EomTaeWook/Bough/releases/download/";
        private const long _maximumAssetSize = 1024L * 1024 * 1024;
        private static readonly HttpClient _client = CreateClient();
        private readonly ApplicationUpdateEnvironment _environment;

        public ApplicationUpdateService(ApplicationUpdateEnvironment environment)
        {
            _environment = environment;
        }

        public string CurrentVersion { get { return _environment.Version; } }

        public Task<ApplicationUpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => CheckCoreAsync(cancellationToken), cancellationToken);
        }

        public Task<VerifiedApplicationUpdate> DownloadAsync(ApplicationUpdateRelease release, IProgress<ApplicationUpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(release);
            return Task.Run(() => DownloadCoreAsync(release, progress, cancellationToken), cancellationToken);
        }

        public Task DiscardAsync(VerifiedApplicationUpdate update)
        {
            ArgumentNullException.ThrowIfNull(update);
            return Task.Run(() => ApplicationUpdateInstaller.DeleteOwnedDownload(update));
        }

        internal string GetInstallBlockedCode()
        {
            if (OperatingSystem.IsWindows() == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (_environment.PublishedSingleFile == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (Debugger.IsAttached)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            string path = _environment.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (Path.IsPathFullyQualified(path) == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) == false)
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            string directory = Path.GetDirectoryName(path);
            if (File.Exists(Path.Combine(directory, "Bough.dll")))
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            if (File.Exists(Path.Combine(directory, "Bough.deps.json")))
            {
                return "AppUpdateCoreDeploymentRequired";
            }
            return null;
        }

        internal void ValidateInstallEnvironment()
        {
            string blocked = GetInstallBlockedCode();
            if (blocked != null)
            {
                throw new ApplicationUpdateException(blocked, null, Array.Empty<object>());
            }
        }

        internal ApplicationUpdateEnvironment Environment { get { return _environment; } }

        private async Task<ApplicationUpdateCheck> CheckCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (ApplicationReleaseVersion.TryParse(CurrentVersion, out ApplicationReleaseVersion current) == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreCurrentVersionInvalid", null, Array.Empty<object>());
                }
                using CancellationTokenSource queryLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                queryLifetime.CancelAfter(TimeSpan.FromSeconds(30));
                CancellationToken queryToken = queryLifetime.Token;
                using HttpRequestMessage request = new(HttpMethod.Get, _latestApi);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, queryToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new ApplicationUpdateCheck(current.Text, null, false, GetInstallBlockedCode());
                }
                if (response.IsSuccessStatusCode == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreQueryFailed", null, (int)response.StatusCode);
                }
                using Stream body = await response.Content.ReadAsStreamAsync(queryToken).ConfigureAwait(false);
                using MemoryStream bounded = new();
                byte[] buffer = new byte[8192];
                int read;
                while ((read = await body.ReadAsync(buffer, queryToken).ConfigureAwait(false)) > 0)
                {
                    if (bounded.Length + read > 1024 * 1024)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                    }
                    bounded.Write(buffer, 0, read);
                }
                using JsonDocument json = JsonDocument.Parse(bounded.ToArray());
                JsonElement root = json.RootElement;
                if (root.GetProperty("draft").GetBoolean())
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                if (root.GetProperty("prerelease").GetBoolean())
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                string tag = root.GetProperty("tag_name").GetString();
                if (ApplicationReleaseVersion.TryParse(tag, out ApplicationReleaseVersion latest) == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                if (latest.IsStable == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                if (tag != latest.Text && tag != "v" + latest.Text)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                Uri page = new(_releasePrefix + Uri.EscapeDataString(tag));
                ApplicationUpdateRelease release = ReadAsset(root, tag, latest.Text, page);
                string blocked = GetInstallBlockedCode();
                if (release.HasDigest == false)
                {
                    blocked = "AppUpdateCoreDigestMissing";
                }
                return new ApplicationUpdateCheck(current.Text, release, latest.CompareTo(current) > 0, blocked);
            }
            catch (OperationCanceledException exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                throw new ApplicationUpdateException("AppUpdateCoreQueryUnavailable", exception, Array.Empty<object>());
            }
            catch (ApplicationUpdateException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ApplicationUpdateException("AppUpdateCoreQueryUnavailable", exception, Array.Empty<object>());
            }
        }

        private static ApplicationUpdateRelease ReadAsset(JsonElement root, string tag, string version, Uri page)
        {
            string expectedName = "Bough-" + tag + "-win-x64.exe";
            JsonElement selected = default;
            int matches = 0;
            foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString();
                if (name != expectedName && name != "Bough.exe")
                {
                    continue;
                }
                selected = asset;
                matches++;
            }
            if (matches != 1)
            {
                throw new ApplicationUpdateException("AppUpdateCoreAssetUnavailable", null, Array.Empty<object>()) { ReleasePage = page };
            }
            if (selected.GetProperty("state").GetString() != "uploaded")
            {
                throw new ApplicationUpdateException("AppUpdateCoreAssetUnavailable", null, Array.Empty<object>()) { ReleasePage = page };
            }
            long size = selected.GetProperty("size").GetInt64();
            if (size <= 0)
            {
                throw new ApplicationUpdateException("AppUpdateCoreAssetUnavailable", null, Array.Empty<object>()) { ReleasePage = page };
            }
            if (size > _maximumAssetSize)
            {
                throw new ApplicationUpdateException("AppUpdateCoreAssetUnavailable", null, Array.Empty<object>()) { ReleasePage = page };
            }
            string namePart = Uri.EscapeDataString(selected.GetProperty("name").GetString());
            Uri expected = new(_assetPrefix + Uri.EscapeDataString(tag) + "/" + namePart);
            if (selected.GetProperty("browser_download_url").GetString() != expected.AbsoluteUri)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>()) { ReleasePage = page };
            }
            string digest = null;
            if (selected.TryGetProperty("digest", out JsonElement digestValue))
            {
                if (digestValue.ValueKind == JsonValueKind.String)
                {
                    string value = digestValue.GetString();
                    if (value.StartsWith("sha256:", StringComparison.Ordinal))
                    {
                        string hash = value.Substring(7);
                        if (IsSha256(hash))
                        {
                            digest = hash.ToLowerInvariant();
                        }
                    }
                }
            }
            return new ApplicationUpdateRelease(version, tag, page, expected, digest, size);
        }

        private async Task<VerifiedApplicationUpdate> DownloadCoreAsync(ApplicationUpdateRelease release, IProgress<ApplicationUpdateProgress> progress, CancellationToken cancellationToken)
        {
            string directory = null;
            try
            {
                ValidateInstallEnvironment();
                if (release.HasDigest == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreDigestMissing", null, Array.Empty<object>());
                }
                if (ApplicationReleaseVersion.TryParse(CurrentVersion, out ApplicationReleaseVersion current) == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreCurrentVersionInvalid", null, Array.Empty<object>());
                }
                if (ApplicationReleaseVersion.TryParse(release.Version, out ApplicationReleaseVersion candidate) == false)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                if (candidate.CompareTo(current) <= 0)
                {
                    throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
                }
                cancellationToken.ThrowIfCancellationRequested();
                directory = ApplicationUpdateInstaller.CreateOwnedDirectory();
                string partial = Path.Combine(directory, "download.partial");
                Uri address = release.AssetUrl;
                using HttpResponseMessage response = await OpenDownloadAsync(address, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode == false)
                {
                    throw new ApplicationUpdateException("AppUpdateDownloadRequestFailed", null, (int)response.StatusCode);
                }
                if (response.Content.Headers.ContentLength.HasValue)
                {
                    if (response.Content.Headers.ContentLength.Value != release.Size)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                    }
                }
                using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (FileStream target = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        received += read;
                        if (received > release.Size)
                        {
                            throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                        }
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        progress?.Report(new ApplicationUpdateProgress(received, release.Size));
                    }
                    await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                    if (received != release.Size)
                    {
                        throw new ApplicationUpdateException("AppUpdateCoreDownloadIncomplete", null, Array.Empty<object>());
                    }
                }
                string verified = Path.Combine(directory, "verified.exe");
                await ApplicationUpdateInstaller.VerifyHashAsync(partial, release.Sha256, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(partial, verified);
                return new VerifiedApplicationUpdate(release, directory, verified);
            }
            catch (OperationCanceledException exception)
            {
                if (directory != null)
                {
                    ApplicationUpdateInstaller.DeleteOwnedDirectory(directory);
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                throw new ApplicationUpdateException("AppUpdateCoreDownloadUnavailable", exception, Array.Empty<object>()) { ReleasePage = release.ReleasePage };
            }
            catch (Exception exception)
            {
                if (directory != null)
                {
                    ApplicationUpdateInstaller.DeleteOwnedDirectory(directory);
                }
                ApplicationUpdateException error = exception as ApplicationUpdateException;
                if (error == null)
                {
                    error = new ApplicationUpdateException("AppUpdateCoreDownloadUnavailable", exception, Array.Empty<object>());
                }
                error.ReleasePage = release.ReleasePage;
                throw error;
            }
        }

        private static async Task<HttpResponseMessage> OpenDownloadAsync(Uri address, CancellationToken cancellationToken)
        {
            for (int redirect = 0; redirect < 6; redirect++)
            {
                ValidateDownloadAddress(address);
                using HttpRequestMessage request = new(HttpMethod.Get, address);
                request.Headers.Accept.ParseAdd("application/octet-stream");
                HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status >= 300 && status < 400)
                {
                    Uri location = response.Headers.Location;
                    response.Dispose();
                    if (location == null)
                    {
                        break;
                    }
                    address = new Uri(address, location);
                    continue;
                }
                return response;
            }
            throw new ApplicationUpdateException("AppUpdateCoreDownloadUnavailable", null, Array.Empty<object>());
        }

        private static void ValidateDownloadAddress(Uri address)
        {
            if (address.Scheme != Uri.UriSchemeHttps)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (address.IsDefaultPort == false)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (address.UserInfo.Length != 0)
            {
                throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
            }
            if (address.Host == "github.com")
            {
                if (address.AbsolutePath.StartsWith("/EomTaeWook/Bough/releases/download/", StringComparison.Ordinal))
                {
                    return;
                }
            }
            if (address.Host == "release-assets.githubusercontent.com" || address.Host == "objects.githubusercontent.com")
            {
                return;
            }
            throw new ApplicationUpdateException("AppUpdateCoreReleaseInvalid", null, Array.Empty<object>());
        }

        internal static bool IsSha256(string hash)
        {
            if (hash.Length != 64)
            {
                return false;
            }
            foreach (char character in hash)
            {
                if (Uri.IsHexDigit(character) == false)
                {
                    return false;
                }
            }
            return true;
        }

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new();
            handler.AllowAutoRedirect = false;
            handler.UseCookies = false;
            handler.UseDefaultCredentials = false;
            HttpClient client = new(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bough-ApplicationUpdate/1.0");
            client.Timeout = TimeSpan.FromMinutes(20);
            return client;
        }
    }
}