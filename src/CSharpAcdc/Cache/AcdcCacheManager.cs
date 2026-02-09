using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace CSharpAcdc.Cache;

public class AcdcCacheManager : IAcdcCacheManager
{
    private readonly IFusionCache _cache;
    private readonly ILogger<AcdcCacheManager> _logger;

    // Maps base URL → set of known cache keys (including user-prefixed variants)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _trackedKeys = new();

    public AcdcCacheManager(IFusionCache cache, ILogger<AcdcCacheManager> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public void TrackKey(string cacheKey, string baseUrl)
    {
        var keys = _trackedKeys.GetOrAdd(baseUrl, _ => new ConcurrentDictionary<string, byte>());
        keys.TryAdd(cacheKey, 0);
    }

    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Clearing all tracked cache entries");

        foreach (var kvp in _trackedKeys)
        {
            foreach (var key in kvp.Value.Keys)
            {
                await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
            }

            kvp.Value.Clear();
        }

        _trackedKeys.Clear();
    }

    public async Task ClearCacheForUrlAsync(string url, CancellationToken ct = default)
    {
        _logger.LogDebug("Clearing cache entries for URL: {Url}", url);

        if (_trackedKeys.TryGetValue(url, out var keys))
        {
            foreach (var key in keys.Keys)
            {
                await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
            }

            keys.Clear();
        }
    }

    public async Task ClearCacheForUserAsync(string userId, CancellationToken ct = default)
    {
        _logger.LogDebug("Clearing cache entries for user: {UserId}", userId);

        foreach (var kvp in _trackedKeys)
        {
            foreach (var key in kvp.Value.Keys)
            {
                if (key.Contains($":{userId}:", StringComparison.Ordinal))
                {
                    await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
                    kvp.Value.TryRemove(key, out _);
                }
            }
        }
    }

    public async Task InvalidateForBaseUrlAsync(string baseUrl, CancellationToken ct = default)
    {
        _logger.LogDebug("Invalidating cache entries for base URL: {BaseUrl}", baseUrl);

        if (_trackedKeys.TryGetValue(baseUrl, out var keys))
        {
            foreach (var key in keys.Keys)
            {
                await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
            }

            keys.Clear();
        }
    }
}
