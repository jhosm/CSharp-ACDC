namespace CSharpAcdc.Configuration;

public record AcdcClientOptions
{
    public Uri? BaseAddress { get; set; }

    public TimeSpan? Timeout { get; set; }

    public string ClientName { get; set; } = "acdc";

    public AcdcAuthOptions? Auth { get; set; }

    public AcdcCacheOptions? Cache { get; set; }

    public AcdcLoggingOptions Logging { get; set; } = new();

    public AcdcDeduplicationOptions Deduplication { get; set; } = new();
}
