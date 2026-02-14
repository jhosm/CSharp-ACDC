namespace CSharpAcdc.Cache;

/// <summary>
/// Represents a cached HTTP response stored by the cache handler.
/// </summary>
/// <param name="Content">The response body as a byte array.</param>
/// <param name="Headers">The response headers.</param>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="ETag">The ETag value for revalidation, or <c>null</c> if not present.</param>
public record CachedResponse(
    byte[] Content,
    Dictionary<string, string[]> Headers,
    int StatusCode,
    string? ETag);
