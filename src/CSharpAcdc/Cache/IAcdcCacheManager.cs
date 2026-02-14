namespace CSharpAcdc.Cache;

/// <summary>
/// Manages cache key tracking and provides programmatic cache invalidation.
/// </summary>
public interface IAcdcCacheManager
{
    /// <summary>
    /// Clears all tracked cache entries.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ClearCacheAsync(CancellationToken ct = default);

    /// <summary>
    /// Clears cache entries associated with a specific URL.
    /// </summary>
    /// <param name="url">The URL whose cache entries should be cleared.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearCacheForUrlAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Clears cache entries associated with a specific user.
    /// </summary>
    /// <param name="userId">The user ID whose cache entries should be cleared.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearCacheForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Registers a cache key for tracking, associated with a base URL.
    /// </summary>
    /// <param name="cacheKey">The cache key to track.</param>
    /// <param name="baseUrl">The base URL associated with the key.</param>
    void TrackKey(string cacheKey, string baseUrl);

    /// <summary>
    /// Invalidates all cache entries for a given base URL (used on mutation requests).
    /// </summary>
    /// <param name="baseUrl">The base URL whose entries should be invalidated.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateForBaseUrlAsync(string baseUrl, CancellationToken ct = default);
}
