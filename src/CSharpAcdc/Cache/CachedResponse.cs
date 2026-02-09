namespace CSharpAcdc.Cache;

public record CachedResponse(
    byte[] Content,
    Dictionary<string, string[]> Headers,
    int StatusCode,
    string? ETag);
