using CSharpAcdc.Cache;

namespace CSharpAcdc.Configuration;

/// <summary>
/// Configuration options for HTTP response caching via FusionCache.
/// </summary>
public record AcdcCacheOptions
{
    /// <summary>
    /// Gets or sets the cache entry lifetime. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the maximum stale data lifetime for fail-safe mode.
    /// When set, enables fail-safe so stale data is returned if the factory fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fail-safe</b> means: if a fresh fetch fails (timeout, 5xx, exception), FusionCache
    /// returns the last cached value instead of throwing — as long as that value is younger than
    /// this duration. Set to <c>null</c> (default) to disable fail-safe entirely.
    /// </para>
    /// <para>
    /// Example: with <see cref="Duration"/> = 5 min and <c>MaxStaleAge</c> = 1 hour,
    /// a cached response is "fresh" for 5 minutes. For the next 55 minutes it is "stale but usable":
    /// FusionCache will try to refresh, but returns the stale value if the refresh fails.
    /// After 1 hour the entry is evicted completely.
    /// </para>
    /// </remarks>
    public TimeSpan? MaxStaleAge { get; set; }

    /// <summary>
    /// Gets or sets the soft timeout before returning stale data (stale-while-revalidate).
    /// The factory continues in the background after the timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This implements the <b>stale-while-revalidate</b> pattern. When a cache entry is stale
    /// and a refresh is needed, FusionCache waits up to this duration for the fresh response.
    /// If the refresh doesn't complete in time, the stale value is returned immediately and
    /// the refresh continues in the background (see <see cref="BackgroundRefreshOnTimeout"/>).
    /// </para>
    /// <para>
    /// Example: with <c>StaleWhileRevalidateTimeout</c> = 1 second, if the downstream API takes 3 seconds
    /// to respond, the caller gets the stale cached value after 1 second while the fresh value
    /// is fetched and cached in the background.
    /// </para>
    /// <para>
    /// Requires <see cref="MaxStaleAge"/> to be set (fail-safe must be enabled).
    /// </para>
    /// </remarks>
    public TimeSpan? StaleWhileRevalidateTimeout { get; set; }

    /// <summary>
    /// Gets or sets whether the factory continues in the background after a soft timeout. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <c>true</c> (default) and <see cref="StaleWhileRevalidateTimeout"/> fires, the HTTP request
    /// keeps running in the background. When it eventually completes, the cache is updated with
    /// the fresh value so the next caller gets it immediately.
    /// </para>
    /// <para>
    /// Set to <c>false</c> to cancel the background fetch after the soft timeout. This saves
    /// resources but means the next request will also need to fetch from the server.
    /// </para>
    /// </remarks>
    public bool BackgroundRefreshOnTimeout { get; set; } = true;

    /// <summary>
    /// Gets or sets the cache key strategy. Defaults to <see cref="CacheKeyStrategy.Shared"/>.
    /// </summary>
    public CacheKeyStrategy CacheKeyStrategy { get; set; } = CacheKeyStrategy.Shared;

    /// <summary>
    /// Gets or sets whether ETag/If-None-Match revalidation is enabled. Defaults to <c>true</c>.
    /// </summary>
    public bool ETagEnabled { get; set; } = true;
}
