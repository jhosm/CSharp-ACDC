using CSharpAcdc.Auth;
using CSharpAcdc.Builder;
using CSharpAcdc.Client;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Extensions;
using CSharpAcdc.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CSharpAcdc.IntegrationTests;

public class CompleteClientIntegrationTests : IDisposable
{
    private readonly FakeApiServer _api = new();
    private readonly FakeOAuthServer _oauth = new();

    [Fact]
    public async Task FullPipeline_GetRequest_FlowsThroughAllHandlers()
    {
        _api.ConfigureGetSuccess("/data", new { value = "hello" });
        _oauth.ConfigureTokenSuccess();

        using var client = BuildClient(withAuth: true, withCache: true);

        var response = await client.GetAsync($"{_api.Url}/data");

        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("hello", body);

        // Verify API server received the request
        Assert.Equal(1, _api.GetCallCount("/data"));
    }

    [Fact]
    public async Task AuthenticatedRequest_InjectsBearerToken()
    {
        _api.ConfigureGetSuccess("/secure", new { result = "ok" });
        _oauth.ConfigureTokenSuccess();

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "test-access-token", "test-refresh-token",
            DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);

        using var client = BuildClient(withAuth: true, tokenProvider: tokenProvider);

        var response = await client.GetAsync($"{_api.Url}/secure");

        Assert.True(response.IsSuccessStatusCode);

        var authHeaders = _api.GetAuthorizationHeaders("/secure");
        Assert.Single(authHeaders);
        Assert.Equal("Bearer test-access-token", authHeaders[0]);
    }

    [Fact]
    public async Task ErrorConversion_401_ThrowsAcdcAuthException()
    {
        _api.ConfigureError("/auth-fail", 401);

        using var client = BuildClient(withAuth: false);

        var ex = await Assert.ThrowsAsync<AcdcAuthException>(
            () => client.GetAsync($"{_api.Url}/auth-fail"));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task ErrorConversion_403_ThrowsAcdcAuthException()
    {
        _api.ConfigureError("/forbidden", 403);

        using var client = BuildClient(withAuth: false);

        var ex = await Assert.ThrowsAsync<AcdcAuthException>(
            () => client.GetAsync($"{_api.Url}/forbidden"));

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task ErrorConversion_4xx_ThrowsAcdcClientException()
    {
        _api.ConfigureError("/not-found", 404);

        using var client = BuildClient(withAuth: false);

        var ex = await Assert.ThrowsAsync<AcdcClientException>(
            () => client.GetAsync($"{_api.Url}/not-found"));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task ErrorConversion_5xx_ThrowsAcdcServerException()
    {
        _api.ConfigureError("/server-error", 500);

        using var client = BuildClient(withAuth: false);

        var ex = await Assert.ThrowsAsync<AcdcServerException>(
            () => client.GetAsync($"{_api.Url}/server-error"));

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task Timeout_ThroughFullPipeline_ThrowsTaskCanceledException()
    {
        // Configure a very slow endpoint
        _api.ConfigureGetWithDelay("/timeout", new { value = "late" }, TimeSpan.FromSeconds(30));

        // Build client with 1-second timeout
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(b =>
            b.WithTimeout(TimeSpan.FromSeconds(1)));
        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<AcdcHttpClient>();

        // HttpClient.Timeout fires ABOVE the handler pipeline, so the ErrorHandler
        // never gets a chance to convert it. The raw TaskCanceledException propagates.
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.GetAsync($"{_api.Url}/timeout"));

        // The inner exception chain contains TimeoutException, confirming it was a timeout
        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    private AcdcHttpClient BuildClient(
        bool withAuth = false,
        bool withCache = false,
        InMemoryTokenProvider? tokenProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpBuilder = services.AddAcdcHttpClient(b =>
        {
            if (withAuth)
            {
                b = b.WithAuth(auth =>
                {
                    auth.RefreshEndpoint = _oauth.TokenEndpoint;
                    auth.ClientId = "test-client";
                    auth.RevocationEndpoint = _oauth.RevokeEndpoint;
                });
            }

            if (withCache)
            {
                b = b.WithCache(cache =>
                {
                    cache.Duration = TimeSpan.FromMinutes(5);
                    cache.ETagEnabled = true;
                });
            }

            return b;
        });

        if (withAuth && tokenProvider is not null)
        {
            services.AddKeyedSingleton<ITokenProvider>("acdc", tokenProvider);
        }

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<AcdcHttpClient>();
    }

    public void Dispose()
    {
        _api.Dispose();
        _oauth.Dispose();
    }
}
