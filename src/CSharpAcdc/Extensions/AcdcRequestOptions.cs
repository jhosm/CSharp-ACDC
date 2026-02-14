namespace CSharpAcdc.Extensions;

/// <summary>
/// Per-request option keys for overriding ACDC handler behavior via <see cref="HttpRequestMessage.Options"/>.
/// </summary>
public static class AcdcRequestOptions
{
    /// <summary>
    /// When set to <c>true</c>, bypasses the cache handler for this request.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> SkipCache = new("Acdc-SkipCache");

    /// <summary>
    /// When set to <c>true</c>, skips Bearer token injection for this request.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> SkipAuth = new("Acdc-SkipAuth");

    /// <summary>
    /// When set to <c>true</c>, skips request/response logging for this request.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> SkipLogging = new("Acdc-SkipLogging");

    /// <summary>
    /// Overrides the cache duration for this specific request.
    /// </summary>
    public static readonly HttpRequestOptionsKey<TimeSpan> CacheMaxAge = new("Acdc-CacheMaxAge");

    /// <summary>
    /// Sets the retry count for this request.
    /// </summary>
    public static readonly HttpRequestOptionsKey<int> RetryCount = new("Acdc-RetryCount");

    /// <summary>
    /// When set to <c>false</c>, disables deduplication for this request.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> Deduplicate = new("Acdc-Deduplicate");
}
