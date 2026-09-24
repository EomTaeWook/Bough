using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bough.App.Controls
{
    internal class GitHubAuthorPhotoService
    {
        private const int MaximumEntries = 128;
        private const int MaximumCacheBytes = 8 * 1024 * 1024;
        private const int MaximumPending = 64;
        private const int MaximumJsonBytes = 1024 * 1024;
        private const int MaximumCommitRequestsPerHour = 24;
        private const int MaximumUserRequestsPerHour = 6;

        private readonly HttpClient _client;
        private readonly SemaphoreSlim _slots = new(3);
        private readonly object _sync = new();
        private readonly Dictionary<string, CacheEntry> _cache = [];
        private readonly Dictionary<string, Task<byte[]>> _pending = [];
        private readonly LinkedList<string> _recent = new();
        private DateTimeOffset _requestWindow = DateTimeOffset.UtcNow;
        private int _commitRequests;
        private int _userRequests;
        private int _cacheBytes;

        public GitHubAuthorPhotoService(HttpClient client)
        {
            _client = client;
        }

        public Task<byte[]> GetCommitPhotoAsync(string remoteUrl, string commitHash, string authorEmail)
        {
            if (GitHubRepositoryAddress.TryParse(remoteUrl, out string owner, out string repository) == false)
            {
                return Task.FromResult<byte[]>(null);
            }

            if (IsCommitHash(commitHash) == false)
            {
                return Task.FromResult<byte[]>(null);
            }

            string normalizedEmail = authorEmail?.Trim().ToLowerInvariant() ?? string.Empty;
            string key = $"commit:{owner.ToLowerInvariant()}/{repository.ToLowerInvariant()}:{commitHash.ToLowerInvariant()}:{normalizedEmail}";
            return GetOrFetchAsync(key, token => FetchCommitPhotoAsync(owner, repository, commitHash, normalizedEmail, token));
        }

        public Task<byte[]> GetLinkedPhotoAsync(string userName, string authorEmail)
        {
            if (GitHubRepositoryAddress.IsValidUserName(userName) == false)
            {
                return Task.FromResult<byte[]>(null);
            }

            string normalizedEmail = authorEmail.Trim().ToLowerInvariant();
            string key = $"linked:{normalizedEmail}:{userName.ToLowerInvariant()}";
            return GetOrFetchAsync(key, token => FetchLinkedPhotoAsync(userName, token));
        }

        private Task<byte[]> GetOrFetchAsync(string key, Func<CancellationToken, Task<byte[]>> fetch)
        {
            lock (_sync)
            {
                if (_cache.TryGetValue(key, out CacheEntry cached))
                {
                    if (cached.ExpiresAt > DateTimeOffset.UtcNow)
                    {
                        _recent.Remove(cached.Node);
                        _recent.AddFirst(cached.Node);
                        return Task.FromResult(cached.Data);
                    }

                    RemoveCached(key, cached);
                }

                if (_pending.TryGetValue(key, out Task<byte[]> pending))
                {
                    return pending;
                }

                if (_pending.Count >= MaximumPending)
                {
                    return Task.FromResult<byte[]>(null);
                }

                TaskCompletionSource<byte[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(key, completion.Task);
                _ = FetchAndCacheAsync(key, fetch, completion);
                return completion.Task;
            }
        }

        private async Task FetchAndCacheAsync(string key, Func<CancellationToken, Task<byte[]>> fetch, TaskCompletionSource<byte[]> completion)
        {
            byte[] data = null;
            bool entered = false;
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            try
            {
                await _slots.WaitAsync(timeout.Token).ConfigureAwait(false);
                entered = true;
                data = await fetch(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                data = null;
            }
            finally
            {
                if (entered == true)
                {
                    _slots.Release();
                }
            }

            TimeSpan lifetime = TimeSpan.FromMinutes(1);
            if (data != null)
            {
                lifetime = TimeSpan.FromHours(1);
            }

            lock (_sync)
            {
                _pending.Remove(key);
                AddCached(key, data, lifetime);
            }

            completion.TrySetResult(data);
        }

        private async Task<byte[]> FetchCommitPhotoAsync(string owner, string repository, string commitHash, string authorEmail, CancellationToken cancellationToken)
        {
            if (TryConsumeRequest(true) == false)
            {
                return null;
            }

            string endpoint = $"https://api.github.com/repos/{owner}/{repository}/commits/{commitHash}";
            using JsonDocument response = await ReadApiAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return null;
            }

            JsonElement root = response.RootElement;
            if (ReadString(root, "sha", out string actualHash) == false)
            {
                return null;
            }

            if (actualHash.Equals(commitHash, StringComparison.OrdinalIgnoreCase) == false)
            {
                return null;
            }

            if (authorEmail.Length > 0)
            {
                if (root.TryGetProperty("commit", out JsonElement commit) == false)
                {
                    return null;
                }

                if (commit.TryGetProperty("author", out JsonElement gitAuthor) == false)
                {
                    return null;
                }

                if (ReadString(gitAuthor, "email", out string actualEmail) == false)
                {
                    return null;
                }

                if (actualEmail.Trim().Equals(authorEmail, StringComparison.OrdinalIgnoreCase) == false)
                {
                    return null;
                }
            }

            if (root.TryGetProperty("author", out JsonElement account) == false)
            {
                return null;
            }

            if (ReadString(account, "avatar_url", out string avatarUrl) == false)
            {
                return null;
            }

            return await DownloadAvatarAsync(avatarUrl, cancellationToken).ConfigureAwait(false);
        }

        private async Task<byte[]> FetchLinkedPhotoAsync(string userName, CancellationToken cancellationToken)
        {
            if (TryConsumeRequest(false) == false)
            {
                return null;
            }

            string endpoint = $"https://api.github.com/users/{userName}";
            using JsonDocument response = await ReadApiAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return null;
            }

            JsonElement root = response.RootElement;
            if (ReadString(root, "login", out string actualName) == false)
            {
                return null;
            }

            if (actualName.Equals(userName, StringComparison.OrdinalIgnoreCase) == false)
            {
                return null;
            }

            if (ReadString(root, "avatar_url", out string avatarUrl) == false)
            {
                return null;
            }

            return await DownloadAvatarAsync(avatarUrl, cancellationToken).ConfigureAwait(false);
        }

        private async Task<JsonDocument> ReadApiAsync(string endpoint, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.ParseAdd("Bough/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            byte[] data = await ReadLimitedAsync(response.Content, MaximumJsonBytes, cancellationToken).ConfigureAwait(false);
            if (data == null)
            {
                return null;
            }

            return JsonDocument.Parse(data);
        }

        private async Task<byte[]> DownloadAvatarAsync(string avatarUrl, CancellationToken cancellationToken)
        {
            if (TryCreateAvatarUri(avatarUrl, out Uri address) == false)
            {
                return null;
            }

            using HttpRequestMessage request = new(HttpMethod.Get, address);
            request.Headers.UserAgent.ParseAdd("Bough/1.0");
            request.Headers.Accept.ParseAdd("image/png, image/jpeg");
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            return await AuthorImageReader.ReadAsync(response.Content, cancellationToken).ConfigureAwait(false);
        }

        private static bool TryCreateAvatarUri(string avatarUrl, out Uri address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                return false;
            }

            if (avatarUrl.Length > 2048)
            {
                return false;
            }

            if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out Uri original) == false)
            {
                return false;
            }

            if (original.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            if (original.Host.Equals("avatars.githubusercontent.com", StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }

            if (original.Port != 443)
            {
                return false;
            }

            if (original.UserInfo.Length > 0)
            {
                return false;
            }

            if (original.Fragment.Length > 0)
            {
                return false;
            }

            string query = original.Query.TrimStart('?');
            if (query.Length > 0)
            {
                query += "&";
            }

            UriBuilder builder = new(original) { Query = query + "s=96" };
            address = builder.Uri;
            return true;
        }

        private static bool ReadString(JsonElement parent, string name, out string value)
        {
            value = null;
            if (parent.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (parent.TryGetProperty(name, out JsonElement property) == false)
            {
                return false;
            }

            if (property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString();
            return string.IsNullOrWhiteSpace(value) == false;
        }

        private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > maximumBytes)
            {
                return null;
            }

            await using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using MemoryStream output = new();
            byte[] buffer = new byte[8192];
            while (true)
            {
                int count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    return output.ToArray();
                }

                if (output.Length + count > maximumBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, count);
            }
        }

        private static bool IsCommitHash(string hash)
        {
            if (hash == null)
            {
                return false;
            }

            if (hash.Length != 40 && hash.Length != 64)
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

        private bool TryConsumeRequest(bool commit)
        {
            lock (_sync)
            {
                if (DateTimeOffset.UtcNow - _requestWindow >= TimeSpan.FromHours(1))
                {
                    _requestWindow = DateTimeOffset.UtcNow;
                    _commitRequests = 0;
                    _userRequests = 0;
                }

                if (commit == true)
                {
                    if (_commitRequests >= MaximumCommitRequestsPerHour)
                    {
                        return false;
                    }

                    _commitRequests++;
                    return true;
                }

                if (_userRequests >= MaximumUserRequestsPerHour)
                {
                    return false;
                }

                _userRequests++;
                return true;
            }
        }

        private void AddCached(string key, byte[] data, TimeSpan lifetime)
        {
            LinkedListNode<string> node = _recent.AddFirst(key);
            _cache.Add(key, new CacheEntry(data, DateTimeOffset.UtcNow.Add(lifetime), node));
            _cacheBytes += data?.Length ?? 0;
            while (_cache.Count > MaximumEntries || _cacheBytes > MaximumCacheBytes)
            {
                string oldest = _recent.Last.Value;
                RemoveCached(oldest, _cache[oldest]);
            }
        }

        private void RemoveCached(string key, CacheEntry cached)
        {
            _cache.Remove(key);
            _recent.Remove(cached.Node);
            _cacheBytes -= cached.Data?.Length ?? 0;
        }

        private class CacheEntry
        {
            public CacheEntry(byte[] data, DateTimeOffset expiresAt, LinkedListNode<string> node)
            {
                Data = data;
                ExpiresAt = expiresAt;
                Node = node;
            }

            public byte[] Data { get; }
            public DateTimeOffset ExpiresAt { get; }
            public LinkedListNode<string> Node { get; }
        }
    }
}
