namespace CSharpAcdc.Extensions;

public static class AcdcRequestOptions
{
    public static readonly HttpRequestOptionsKey<bool> SkipCache = new("Acdc-SkipCache");
    public static readonly HttpRequestOptionsKey<bool> SkipAuth = new("Acdc-SkipAuth");
    public static readonly HttpRequestOptionsKey<bool> SkipLogging = new("Acdc-SkipLogging");
    public static readonly HttpRequestOptionsKey<TimeSpan> CacheMaxAge = new("Acdc-CacheMaxAge");
    public static readonly HttpRequestOptionsKey<int> RetryCount = new("Acdc-RetryCount");
    public static readonly HttpRequestOptionsKey<bool> Deduplicate = new("Acdc-Deduplicate");
}
