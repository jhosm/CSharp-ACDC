namespace CSharpAcdc.Cache;

public interface IAcdcCacheManager
{
    Task ClearCacheAsync(CancellationToken ct = default);
    Task ClearCacheForUrlAsync(string url, CancellationToken ct = default);
    Task ClearCacheForUserAsync(string userId, CancellationToken ct = default);
    void TrackKey(string cacheKey, string baseUrl);
    Task InvalidateForBaseUrlAsync(string baseUrl, CancellationToken ct = default);
}
