namespace CSharpAcdc.Configuration;

/// <summary>
/// Top-level configuration options for the ACDC HTTP client, including optional auth, cache, and logging sub-options.
/// </summary>
public record AcdcClientOptions
{
    /// <summary>
    /// Gets or sets the base URL for all requests.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the request timeout.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the named HttpClient identifier. Defaults to <c>"acdc"</c>.
    /// </summary>
    public string ClientName { get; set; } = "acdc";

    /// <summary>
    /// Gets or sets the authentication options, or <c>null</c> if auth is not configured.
    /// </summary>
    public AcdcAuthOptions? Auth { get; set; }

    /// <summary>
    /// Gets or sets the cache options, or <c>null</c> if caching is not configured.
    /// </summary>
    public AcdcCacheOptions? Cache { get; set; }

    /// <summary>
    /// Gets or sets the logging options.
    /// </summary>
    public AcdcLoggingOptions Logging { get; set; } = new();

    /// <summary>
    /// Gets or sets the deduplication options.
    /// </summary>
    public AcdcDeduplicationOptions Deduplication { get; set; } = new();
}
