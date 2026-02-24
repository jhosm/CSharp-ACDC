using CSharpAcdc.Auth;
using CSharpAcdc.Cache;
using CSharpAcdc.Cancellation;

namespace CSharpAcdc.Client;

/// <summary>
/// ACDC HTTP client wrapper that provides access to auth management, cache management,
/// and bulk request cancellation alongside standard HTTP operations.
/// </summary>
public sealed class AcdcHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AcdcAuthManager? _authManager;
    private readonly IAcdcCacheManager? _cacheManager;
    private readonly ActiveRequestTracker? _requestTracker;
    private int _disposed;

    public AcdcHttpClient(
        HttpClient httpClient,
        AcdcAuthManager? authManager = null,
        IAcdcCacheManager? cacheManager = null,
        ActiveRequestTracker? requestTracker = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _authManager = authManager;
        _cacheManager = cacheManager;
        _requestTracker = requestTracker;
    }

    /// <summary>
    /// Gets the auth manager for logout and force-refresh operations, or <c>null</c> if auth is not configured.
    /// </summary>
    public AcdcAuthManager? Auth => _authManager;

    /// <summary>
    /// Gets the cache manager for programmatic cache invalidation, or <c>null</c> if caching is not configured.
    /// </summary>
    public IAcdcCacheManager? Cache => _cacheManager;

    /// <summary>
    /// Cancels all currently active requests made through this client.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the client was not constructed via DI registration.</exception>
    public void CancelAll()
    {
        if (_requestTracker is null)
            throw new InvalidOperationException(
                "CancelAll requires an ActiveRequestTracker. " +
                "Ensure the AcdcHttpClient was constructed via AddAcdcHttpClient() DI registration.");
        _requestTracker.CancelAll();
    }

    /// <summary>
    /// Sends a GET request to the specified URI.
    /// </summary>
    /// <param name="requestUri">The request URI as a string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken ct = default)
        => _httpClient.GetAsync(requestUri, ct);

    /// <summary>
    /// Sends a GET request to the specified URI.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, CancellationToken ct = default)
        => _httpClient.GetAsync(requestUri, ct);

    /// <summary>
    /// Sends a POST request with the specified content.
    /// </summary>
    /// <param name="requestUri">The request URI as a string.</param>
    /// <param name="content">The request body content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> PostAsync(string? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PostAsync(requestUri, content, ct);

    /// <summary>
    /// Sends a POST request with the specified content.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="content">The request body content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> PostAsync(Uri? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PostAsync(requestUri, content, ct);

    /// <summary>
    /// Sends a PUT request with the specified content.
    /// </summary>
    /// <param name="requestUri">The request URI as a string.</param>
    /// <param name="content">The request body content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> PutAsync(string? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PutAsync(requestUri, content, ct);

    /// <summary>
    /// Sends a PUT request with the specified content.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="content">The request body content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> PutAsync(Uri? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PutAsync(requestUri, content, ct);

    /// <summary>
    /// Sends a DELETE request to the specified URI.
    /// </summary>
    /// <param name="requestUri">The request URI as a string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> DeleteAsync(string? requestUri, CancellationToken ct = default)
        => _httpClient.DeleteAsync(requestUri, ct);

    /// <summary>
    /// Sends a DELETE request to the specified URI.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> DeleteAsync(Uri? requestUri, CancellationToken ct = default)
        => _httpClient.DeleteAsync(requestUri, ct);

    /// <summary>
    /// Sends a PATCH request with the specified content.
    /// </summary>
    /// <param name="requestUri">The request URI as a string.</param>
    /// <param name="content">The request body content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> PatchAsync(string? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PatchAsync(requestUri, content, ct);

    /// <summary>
    /// Sends a PATCH request with the specified content.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="content">The request body content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> PatchAsync(Uri? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PatchAsync(requestUri, content, ct);

    /// <summary>
    /// Sends an HTTP request.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default)
        => _httpClient.SendAsync(request, ct);

    /// <summary>
    /// Sends an HTTP request with the specified completion option.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="completionOption">When the operation should complete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken ct = default)
        => _httpClient.SendAsync(request, completionOption, ct);

    /// <summary>
    /// Gets the base address of the underlying HTTP client.
    /// </summary>
    public Uri? BaseAddress => _httpClient.BaseAddress;

    /// <summary>
    /// Gets the timeout of the underlying HTTP client.
    /// </summary>
    public TimeSpan Timeout => _httpClient.Timeout;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Do NOT dispose _httpClient — it is managed by IHttpClientFactory
    }
}
