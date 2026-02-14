using CSharpAcdc.Auth;
using CSharpAcdc.Cache;
using CSharpAcdc.Cancellation;

namespace CSharpAcdc.Client;

public sealed class AcdcHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AcdcAuthManager? _authManager;
    private readonly IAcdcCacheManager? _cacheManager;
    private readonly ActiveRequestTracker? _requestTracker;
    private bool _disposed;

    internal AcdcHttpClient(
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

    public AcdcAuthManager? Auth => _authManager;
    public IAcdcCacheManager? Cache => _cacheManager;

    public void CancelAll()
    {
        if (_requestTracker is null)
            throw new InvalidOperationException(
                "CancelAll requires an ActiveRequestTracker. " +
                "Ensure the AcdcHttpClient was constructed via AddAcdcHttpClient() DI registration.");
        _requestTracker.CancelAll();
    }

    public Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken ct = default)
        => _httpClient.GetAsync(requestUri, ct);

    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, CancellationToken ct = default)
        => _httpClient.GetAsync(requestUri, ct);

    public Task<HttpResponseMessage> PostAsync(string? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PostAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> PostAsync(Uri? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PostAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> PutAsync(string? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PutAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> PutAsync(Uri? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PutAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> DeleteAsync(string? requestUri, CancellationToken ct = default)
        => _httpClient.DeleteAsync(requestUri, ct);

    public Task<HttpResponseMessage> DeleteAsync(Uri? requestUri, CancellationToken ct = default)
        => _httpClient.DeleteAsync(requestUri, ct);

    public Task<HttpResponseMessage> PatchAsync(string? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PatchAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> PatchAsync(Uri? requestUri, HttpContent? content, CancellationToken ct = default)
        => _httpClient.PatchAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default)
        => _httpClient.SendAsync(request, ct);

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken ct = default)
        => _httpClient.SendAsync(request, completionOption, ct);

    public Uri? BaseAddress => _httpClient.BaseAddress;
    public TimeSpan Timeout => _httpClient.Timeout;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Do NOT dispose _httpClient — it is managed by IHttpClientFactory
    }
}
