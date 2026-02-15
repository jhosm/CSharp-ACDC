namespace CSharpAcdc.Extensions;

/// <summary>
/// Extension methods for <see cref="HttpRequestMessage"/>.
/// </summary>
public static class HttpRequestMessageExtensions
{
    /// <summary>
    /// Creates a deep clone of the request including headers, content, and options.
    /// The original request's content is replaced with a replayable <see cref="ByteArrayContent"/>.
    /// </summary>
    /// <param name="request">The request to clone.</param>
    /// <returns>A cloned request that can be sent independently.</returns>
    /// <summary>
    /// Bypasses the cache handler for this request.
    /// </summary>
    /// <returns>The same request instance for fluent chaining.</returns>
    public static HttpRequestMessage SkipCache(this HttpRequestMessage request)
    {
        request.Options.Set(AcdcRequestOptions.SkipCache, true);
        return request;
    }

    /// <summary>
    /// Skips Bearer token injection for this request.
    /// </summary>
    /// <returns>The same request instance for fluent chaining.</returns>
    public static HttpRequestMessage SkipAuth(this HttpRequestMessage request)
    {
        request.Options.Set(AcdcRequestOptions.SkipAuth, true);
        return request;
    }

    /// <summary>
    /// Skips request/response logging for this request.
    /// </summary>
    /// <returns>The same request instance for fluent chaining.</returns>
    public static HttpRequestMessage SkipLogging(this HttpRequestMessage request)
    {
        request.Options.Set(AcdcRequestOptions.SkipLogging, true);
        return request;
    }

    /// <summary>
    /// Disables deduplication for this request.
    /// </summary>
    /// <returns>The same request instance for fluent chaining.</returns>
    public static HttpRequestMessage SkipDeduplication(this HttpRequestMessage request)
    {
        request.Options.Set(AcdcRequestOptions.Deduplicate, false);
        return request;
    }

    /// <summary>
    /// Overrides the cache duration for this specific request.
    /// </summary>
    /// <param name="request">The request to configure.</param>
    /// <param name="maxAge">The cache duration override.</param>
    /// <returns>The same request instance for fluent chaining.</returns>
    public static HttpRequestMessage WithCacheMaxAge(this HttpRequestMessage request, TimeSpan maxAge)
    {
        request.Options.Set(AcdcRequestOptions.CacheMaxAge, maxAge);
        return request;
    }

    /// <summary>
    /// Creates a deep clone of the request including headers, content, and options.
    /// The original request's content is replaced with a replayable <see cref="ByteArrayContent"/>.
    /// </summary>
    /// <param name="request">The request to clone.</param>
    /// <returns>A cloned request that can be sent independently.</returns>
    public static async Task<HttpRequestMessage> CloneAsync(this HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            // Replace the original content with a replayable ByteArrayContent so the
            // source request remains sendable after cloning (ReadAsByteArrayAsync may
            // consume a forward-only stream).
            var originalContent = new ByteArrayContent(contentBytes);
            var clonedContent = new ByteArrayContent(contentBytes);

            foreach (var header in request.Content.Headers)
            {
                originalContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                clonedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Content.Dispose();
            request.Content = originalContent;
            clone.Content = clonedContent;
        }

        foreach (var option in request.Options)
        {
            ((IDictionary<string, object?>)clone.Options).Add(option.Key, option.Value);
        }

        return clone;
    }
}
