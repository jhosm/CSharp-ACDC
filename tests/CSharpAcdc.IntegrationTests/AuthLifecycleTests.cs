using CSharpAcdc.Auth;
using CSharpAcdc.Client;
using CSharpAcdc.Extensions;
using CSharpAcdc.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CSharpAcdc.IntegrationTests;

public class AuthLifecycleTests : IDisposable
{
    private readonly FakeApiServer _api = new();
    private readonly FakeOAuthServer _oauth = new();

    [Fact]
    public async Task ProactiveRefresh_RefreshesTokenBeforeExpiry()
    {
        // Token expires in 30 seconds, threshold is 60 seconds => should trigger proactive refresh
        _api.ConfigureGetSuccess("/data", new { value = "ok" });
        _oauth.ConfigureTokenSuccess("refreshed-token", "new-refresh", 3600);

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "old-token", "old-refresh",
            DateTimeOffset.UtcNow.AddSeconds(30), // Within threshold
            CancellationToken.None);

        using var client = BuildClient(
            tokenProvider: tokenProvider,
            refreshThreshold: TimeSpan.FromSeconds(60));

        var response = await client.GetAsync($"{_api.Url}/data");

        Assert.True(response.IsSuccessStatusCode);

        // Give time for the fire-and-forget proactive refresh to complete
        await Task.Delay(500);

        // Verify token refresh was called
        Assert.True(_oauth.GetCallCount("/token") >= 1);
    }

    [Fact]
    public async Task ReactiveRefresh_RetriesRequestAfter401()
    {
        _api.RespondWith401ThenSuccess("/protected", new { result = "success" });
        _oauth.ConfigureTokenSuccess("fresh-token", "fresh-refresh", 3600);

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "expired-token", "valid-refresh",
            DateTimeOffset.UtcNow.AddHours(1), // Not expired from provider's view
            CancellationToken.None);

        using var client = BuildClient(tokenProvider: tokenProvider);

        var response = await client.GetAsync($"{_api.Url}/protected");

        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("success", body);

        // API server should have been called twice (first 401, then 200)
        Assert.Equal(2, _api.GetCallCount("/protected"));

        // OAuth server should have received exactly 1 token refresh
        Assert.Equal(1, _oauth.GetCallCount("/token"));
    }

    [Fact]
    public async Task ConcurrentRefreshQueue_OnlyOneRefreshCall()
    {
        // Configure a slow token refresh so concurrent requests pile up
        _oauth.ConfigureTokenSuccessWithDelay(
            TimeSpan.FromMilliseconds(500),
            "concurrent-token", "concurrent-refresh", 3600);

        // Configure API to always return 401 for initial token, then 200 after refresh
        _api.ConfigureError("/concurrent", 401);

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "stale-token", "valid-refresh",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        using var client = BuildClient(tokenProvider: tokenProvider);

        // Send N concurrent requests — all will hit 401 and trigger refresh
        const int concurrentRequests = 8;
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => client.GetAsync($"{_api.Url}/concurrent"))
            .ToArray();

        // Reconfigure API to return 200 after the refresh completes (new token will be used)
        await Task.Delay(100); // Let requests start
        _api.Reset();
        _api.ConfigureGetSuccess("/concurrent", new { result = "ok" });

        var responses = await Task.WhenAll(tasks);

        // The leader/follower pattern should coalesce all refreshes into 1 call
        Assert.Equal(1, _oauth.GetCallCount("/token"));
    }

    [Fact]
    public async Task LogoutDuringRefresh_HandlesGracefully()
    {
        // Configure API to return 401 so the auth handler triggers a token refresh
        _api.ConfigureError("/data", 401);
        // Slow token refresh — gives us time to call LogoutAsync while it's in flight
        _oauth.ConfigureTokenSuccessWithDelay(
            TimeSpan.FromSeconds(2), "new-token", "new-refresh", 3600);
        _oauth.ConfigureRevokeSuccess();

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "current-token", "current-refresh",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAcdcHttpClient(b => b
            .WithAuth(auth =>
            {
                auth.RefreshEndpoint = _oauth.TokenEndpoint;
                auth.ClientId = "test-client";
                auth.RevocationEndpoint = _oauth.RevokeEndpoint;
            }));

        services.AddKeyedSingleton<ITokenProvider>("acdc", tokenProvider);

        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<AcdcHttpClient>();
        var authManager = sp.GetRequiredKeyedService<AcdcAuthManager>("acdc");

        // Start a request that will hit 401 and trigger a slow token refresh
        var requestTask = client.GetAsync($"{_api.Url}/data");

        // Wait briefly for the refresh to begin, then call LogoutAsync concurrently
        await Task.Delay(200);
        await authManager.LogoutAsync(CancellationToken.None);

        // Wait for the request to complete (it may succeed or fail — graceful handling is the goal)
        try { await requestTask; } catch { /* expected — 401 retry may fail after logout */ }

        // The critical assertion: no deadlock occurred, and revoke was called.
        // Token state after a race between refresh-save and logout-clear is non-deterministic,
        // so we only verify that the system handled the concurrent logout gracefully.
        Assert.Equal(1, _oauth.GetCallCount("/revoke"));
    }

    private AcdcHttpClient BuildClient(
        InMemoryTokenProvider? tokenProvider = null,
        TimeSpan? refreshThreshold = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAcdcHttpClient(b => b
            .WithAuth(auth =>
            {
                auth.RefreshEndpoint = _oauth.TokenEndpoint;
                auth.ClientId = "test-client";
                auth.RevocationEndpoint = _oauth.RevokeEndpoint;
                if (refreshThreshold.HasValue)
                    auth.RefreshThreshold = refreshThreshold.Value;
            }));

        if (tokenProvider is not null)
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
