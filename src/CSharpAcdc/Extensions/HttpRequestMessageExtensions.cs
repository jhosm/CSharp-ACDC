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
