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
    public TimeSpan? FailSafeMaxDuration { get; set; }

    /// <summary>
    /// Gets or sets the soft timeout before returning stale data (stale-while-revalidate).
    /// The factory continues in the background after the timeout.
    /// </summary>
    public TimeSpan? FactorySoftTimeout { get; set; }

    /// <summary>
    /// Gets or sets whether the factory continues in the background after a soft timeout. Defaults to <c>true</c>.
    /// </summary>
    public bool AllowTimedOutFactoryBackgroundCompletion { get; set; } = true;

    /// <summary>
    /// Gets or sets the cache key strategy. Defaults to <see cref="CacheKeyStrategy.Shared"/>.
    /// </summary>
    public CacheKeyStrategy CacheKeyStrategy { get; set; } = CacheKeyStrategy.Shared;

    /// <summary>
    /// Gets or sets whether ETag/If-None-Match revalidation is enabled. Defaults to <c>true</c>.
    /// </summary>
    public bool ETagEnabled { get; set; } = true;
}
