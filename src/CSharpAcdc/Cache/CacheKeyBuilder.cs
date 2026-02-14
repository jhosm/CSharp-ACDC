namespace CSharpAcdc.Cache;

/// <summary>
/// Determines how cache keys are generated for cached responses.
/// </summary>
public enum CacheKeyStrategy
{
    /// <summary>
    /// Cache entries are shared across all users.
    /// </summary>
    Shared,

    /// <summary>
    /// Cache entries are isolated per user using the user's identity.
    /// </summary>
    UserIsolated,

    /// <summary>
    /// Caching is disabled; no cache key is generated.
    /// </summary>
    NoCache,
}

/// <summary>
/// Builds cache keys based on the HTTP method, URL, strategy, and optional user identity.
/// </summary>
public static class CacheKeyBuilder
{
    /// <summary>
    /// Builds a cache key for the given request parameters.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The request URL.</param>
    /// <param name="strategy">The cache key strategy.</param>
    /// <param name="userId">The user ID for user-isolated caching.</param>
    /// <returns>The cache key, or <c>null</c> if caching is disabled.</returns>
    public static string? BuildKey(
        HttpMethod method,
        string url,
        CacheKeyStrategy strategy,
        string? userId = null)
    {
        return strategy switch
        {
            CacheKeyStrategy.Shared => $"{method}:{url}",
            CacheKeyStrategy.UserIsolated when userId is not null => $"{method}:{userId}:{url}",
            CacheKeyStrategy.UserIsolated => $"{method}:{url}",
            CacheKeyStrategy.NoCache => null,
            _ => null,
        };
    }
}
