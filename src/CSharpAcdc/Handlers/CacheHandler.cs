using System.Collections.Concurrent;
using System.Net;
using CSharpAcdc.Cache;
using CSharpAcdc.Configuration;
using CSharpAcdc.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CSharpAcdc.Handlers;

/// <summary>
/// Caches GET/HEAD responses using FusionCache with ETag revalidation, stale-while-revalidate,
/// and automatic invalidation on mutation requests.
/// </summary>
public class CacheHandler : DelegatingHandler
{
    private readonly IFusionCache _cache;
    private readonly AcdcCacheOptions _options;
    private readonly ILogger<CacheHandler> _logger;
    private readonly Func<HttpRequestMessage, string?>? _userIdProvider;
    private readonly IAcdcCacheManager? _cacheManager;

    // ETags and last responses survive cache expiration for 304 revalidation.
    // Bounded: cleared during invalidation and trimmed when exceeding MaxETagStoreSize.
    private readonly ConcurrentDictionary<string, (string ETag, CachedResponse Response)> _etagStore = new();
    private const int MaxETagStoreSize = 1000;

    /// <summary>
    /// Initializes a new instance of <see cref="CacheHandler"/>.
    /// </summary>
    /// <param name="cache">The FusionCache instance for storing responses.</param>
    /// <param name="options">Cache configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="userIdProvider">Optional delegate to extract user ID for cache key isolation.</param>
    /// <param name="cacheManager">Optional cache manager for key tracking and invalidation.</param>
    public CacheHandler(
        IFusionCache cache,
        IOptions<AcdcCacheOptions> options,
        ILogger<CacheHandler> logger,
        Func<HttpRequestMessage, string?>? userIdProvider = null,
        IAcdcCacheManager? cacheManager = null)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _userIdProvider = userIdProvider;
        _cacheManager = cacheManager;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!IsCacheableMethod(request.Method))
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await InvalidateRelatedCacheEntriesAsync(request, cancellationToken).ConfigureAwait(false);
            return response;
        }

        if (request.Options.TryGetValue(AcdcRequestOptions.SkipCache, out var skip) && skip)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var url = request.RequestUri?.ToString() ?? string.Empty;
        var userId = _userIdProvider?.Invoke(request);
        var cacheKey = CacheKeyBuilder.BuildKey(request.Method, url, _options.CacheKeyStrategy, userId);

        if (cacheKey is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var baseUrl = GetBaseUrl(request.RequestUri);
        _cacheManager?.TrackKey(cacheKey, baseUrl);

        var perRequestDuration = request.Options.TryGetValue(AcdcRequestOptions.CacheMaxAge, out var maxAge)
            ? maxAge
            : (TimeSpan?)null;

        var (cachedResponse, fromCache) = await GetOrFetchAsync(
            cacheKey, request, perRequestDuration, cancellationToken).ConfigureAwait(false);

        return ToHttpResponseMessage(cachedResponse, fromCache);
    }

    private async Task<(CachedResponse Response, bool FromCache)> GetOrFetchAsync(
        string cacheKey,
        HttpRequestMessage request,
        TimeSpan? perRequestDuration,
        CancellationToken cancellationToken)
    {
        // Look up stored ETag + last known response (survives cache expiration)
        _etagStore.TryGetValue(cacheKey, out var etagEntry);
        var storedETag = _options.ETagEnabled ? etagEntry.ETag : null;
        var lastKnownResponse = _options.ETagEnabled ? etagEntry.Response : null;

        var fromCache = true;

        var entryOptions = BuildEntryOptions(perRequestDuration);

        var result = await _cache.GetOrSetAsync<CachedResponse>(
            cacheKey,
            async (ctx, ct) =>
            {
                fromCache = false;
                return await FetchAndCacheAsync(cacheKey, request, storedETag, lastKnownResponse, ct).ConfigureAwait(false);
            },
            entryOptions,
            token: cancellationToken).ConfigureAwait(false);

        return (result!, fromCache);
    }

    private async Task<CachedResponse> FetchAndCacheAsync(
        string cacheKey,
        HttpRequestMessage request,
        string? storedETag,
        CachedResponse? lastKnownResponse,
        CancellationToken cancellationToken)
    {
        // Clone the request to avoid mutating the caller's instance.
        // With AllowTimedOutFactoryBackgroundCompletion, the factory may continue
        // after the soft timeout returns stale data, creating a race on the original request.
        using var clonedRequest = CloneRequest(request);

        // Add If-None-Match header if we have a stored ETag
        if (_options.ETagEnabled && storedETag is not null)
        {
            clonedRequest.Headers.IfNoneMatch.Clear();
            clonedRequest.Headers.IfNoneMatch.Add(
                new System.Net.Http.Headers.EntityTagHeaderValue($"\"{storedETag}\""));
        }

        using var response = await base.SendAsync(clonedRequest, cancellationToken).ConfigureAwait(false);

        // On 304 Not Modified, return last known cached content
        if (response.StatusCode == HttpStatusCode.NotModified && lastKnownResponse is not null)
        {
            _logger.LogDebug("304 Not Modified for {Url}, returning cached content", request.RequestUri);
            return lastKnownResponse;
        }

        var cachedResponse = await ToCachedResponseAsync(response, cancellationToken).ConfigureAwait(false);

        // Track ETag and response for future revalidation (survives cache expiration)
        if (cachedResponse.ETag is not null)
        {
            _etagStore[cacheKey] = (cachedResponse.ETag, cachedResponse);
            TrimETagStoreIfNeeded();
        }

        return cachedResponse;
    }

    private FusionCacheEntryOptions BuildEntryOptions(TimeSpan? perRequestDuration)
    {
        var entryOptions = new FusionCacheEntryOptions
        {
            Duration = perRequestDuration ?? _options.Duration,
            AllowTimedOutFactoryBackgroundCompletion = _options.AllowTimedOutFactoryBackgroundCompletion,
        };

        if (_options.FailSafeMaxDuration.HasValue)
        {
            entryOptions.IsFailSafeEnabled = true;
            entryOptions.FailSafeMaxDuration = _options.FailSafeMaxDuration.Value;
        }

        if (_options.FactorySoftTimeout.HasValue)
        {
            entryOptions.FactorySoftTimeout = _options.FactorySoftTimeout.Value;
        }

        return entryOptions;
    }

    private async Task InvalidateRelatedCacheEntriesAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_cacheManager is null)
            return;

        var baseUrl = GetBaseUrl(request.RequestUri);

        try
        {
            await _cacheManager.InvalidateForBaseUrlAsync(baseUrl, cancellationToken).ConfigureAwait(false);
            ClearETagsForBaseUrl(baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache entries for base URL {BaseUrl}", baseUrl);
        }
    }

    private void ClearETagsForBaseUrl(string baseUrl)
    {
        var keysToRemove = _etagStore.Keys
            .Where(key => key.Contains(baseUrl, StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _etagStore.TryRemove(key, out _);
        }
    }

    private void TrimETagStoreIfNeeded()
    {
        if (_etagStore.Count > MaxETagStoreSize)
        {
            _etagStore.Clear();
        }
    }

    private static bool IsCacheableMethod(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head;

    private static string GetBaseUrl(Uri? uri)
    {
        if (uri is null)
            return string.Empty;

        // Use scheme + authority + path (without query string) as base URL
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var cloneOptions = (IDictionary<string, object?>)clone.Options;
        foreach (var option in (IDictionary<string, object?>)original.Options)
        {
            cloneOptions[option.Key] = option.Value;
        }

        return clone;
    }

    private static async Task<CachedResponse> ToCachedResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = response.Content is not null
            ? await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)
            : Array.Empty<byte>();

        var headers = new Dictionary<string, string[]>();
        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }
        }

        var etag = response.Headers.ETag?.Tag?.Trim('"');

        return new CachedResponse(content, headers, (int)response.StatusCode, etag);
    }

    private static HttpResponseMessage ToHttpResponseMessage(CachedResponse cached, bool fromCache)
    {
        var response = new HttpResponseMessage((HttpStatusCode)cached.StatusCode)
        {
            Content = new ByteArrayContent(cached.Content),
        };

        foreach (var header in cached.Headers)
        {
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (fromCache)
        {
            response.Headers.TryAddWithoutValidation("X-ACDC-From-Cache", "true");
        }

        return response;
    }
}
