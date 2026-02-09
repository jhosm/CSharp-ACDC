namespace CSharpAcdc.Cache;

public enum CacheKeyStrategy
{
    Shared,
    UserIsolated,
    NoCache,
}

public static class CacheKeyBuilder
{
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
