using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Internals;

namespace Bough.App.Controls
{
    public class AuthorPhotoService
    {
        private const int MaximumEntries = 128;
        private const int MaximumCacheBytes = 8 * 1024 * 1024;
        private const int MaximumImageBytes = 256 * 1024;
        private const int MaximumPending = 64;

        private static readonly HttpClient _defaultClient = new(new HttpClientHandler { AllowAutoRedirect = false });

        private readonly HttpClient _client;
        private readonly GitHubAuthorPhotoService _gitHubPhotos;
        private readonly GitHubAuthorLinkSettings _gitHubLinkSettings;
        private readonly SemaphoreSlim _slots = new(3);
        private readonly object _sync = new();
        private readonly Dictionary<string, CacheEntry> _cache = [];
        private readonly Dictionary<string, Task<byte[]>> _pending = [];
        private readonly LinkedList<string> _recent = new();
        private int _cacheBytes;

        public AuthorPhotoService()
            : this(_defaultClient)
        {
        }

        public AuthorPhotoService(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _gitHubPhotos = new GitHubAuthorPhotoService(client);
            _gitHubLinkSettings = new GitHubAuthorLinkSettings();
        }

        public async Task<AuthorPhotoResult> GetAuthorPhotoAsync(string email, string commitHash, string gitHubRemoteUrl, bool externalPhotosEnabled)
        {
            if (externalPhotosEnabled == false)
            {
                return new AuthorPhotoResult(null, AuthorPhotoSource.LocalIdenticon);
            }

            byte[] photo = await _gitHubPhotos.GetCommitPhotoAsync(gitHubRemoteUrl, commitHash, email).ConfigureAwait(false);
            if (photo != null)
            {
                return new AuthorPhotoResult(photo, AuthorPhotoSource.GitHubCommit);
            }

            if (string.IsNullOrWhiteSpace(email) == true)
            {
                return new AuthorPhotoResult(null, AuthorPhotoSource.LocalIdenticon);
            }

            GitHubAuthorLink link = _gitHubLinkSettings.Load();
            if (email.Trim().Equals(link.AuthorEmail, StringComparison.OrdinalIgnoreCase) == true)
            {
                photo = await _gitHubPhotos.GetLinkedPhotoAsync(link.UserName, email).ConfigureAwait(false);
                if (photo != null)
                {
                    return new AuthorPhotoResult(photo, AuthorPhotoSource.GitHubLinkedAccount);
                }
            }

            photo = await GetPhotoAsync(email).ConfigureAwait(false);
            if (photo != null)
            {
                return new AuthorPhotoResult(photo, AuthorPhotoSource.Gravatar);
            }

            return new AuthorPhotoResult(null, AuthorPhotoSource.LocalIdenticon);
        }

        public Task<byte[]> GetPhotoAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return Task.FromResult<byte[]>(null);
            }

            string normalized = email.Trim().ToLowerInvariant();
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            lock (_sync)
            {
                if (_cache.TryGetValue(hash, out CacheEntry cached))
                {
                    if (cached.ExpiresAt > DateTimeOffset.UtcNow)
                    {
                        _recent.Remove(cached.Node);
                        _recent.AddFirst(cached.Node);
                        return Task.FromResult(cached.Data);
                    }

                    RemoveCached(hash, cached);
                }

                if (_pending.TryGetValue(hash, out Task<byte[]> pending))
                {
                    return pending;
                }

                if (_pending.Count >= MaximumPending)
                {
                    return Task.FromResult<byte[]>(null);
                }

                TaskCompletionSource<byte[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(hash, completion.Task);
                _ = FetchAndCacheAsync(hash, completion);
                return completion.Task;
            }
        }

        private async Task FetchAndCacheAsync(string hash, TaskCompletionSource<byte[]> completion)
        {
            byte[] data = null;
            TimeSpan lifetime = TimeSpan.FromMinutes(1);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(4));
            bool entered = false;
            try
            {
                await _slots.WaitAsync(timeout.Token).ConfigureAwait(false);
                entered = true;
                string url = $"https://gravatar.com/avatar/{hash}?s=96&d=404";
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    lifetime = TimeSpan.FromHours(1);
                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        data = await ReadImageAsync(response.Content, timeout.Token).ConfigureAwait(false);
                        if (data != null)
                        {
                            lifetime = TimeSpan.FromHours(1);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Keep the local identicon on network failure. Do not log the request or email.
            }
            finally
            {
                if (entered)
                {
                    _slots.Release();
                }
            }

            lock (_sync)
            {
                _pending.Remove(hash);
                AddCached(hash, data, lifetime);
            }
            completion.TrySetResult(data);
        }

        private static async Task<byte[]> ReadImageAsync(HttpContent content, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > MaximumImageBytes)
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

                if (output.Length + count > MaximumImageBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, count);
            }
        }

        private void AddCached(string hash, byte[] data, TimeSpan lifetime)
        {
            LinkedListNode<string> node = _recent.AddFirst(hash);
            _cache.Add(hash, new CacheEntry(data, DateTimeOffset.UtcNow.Add(lifetime), node));
            _cacheBytes += data?.Length ?? 0;
            while (_cache.Count > MaximumEntries || _cacheBytes > MaximumCacheBytes)
            {
                string oldest = _recent.Last.Value;
                RemoveCached(oldest, _cache[oldest]);
            }
        }

        private void RemoveCached(string hash, CacheEntry cached)
        {
            _cache.Remove(hash);
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
