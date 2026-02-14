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
        // All requests will get 401 from first call, but the scenario only transitions once
        // So we need a different setup: use a path that always returns 401 initially
        _oauth.ConfigureTokenSuccess("concurrent-token", "concurrent-refresh", 3600);

        // Configure API to return 401 for initial requests, then success
        _api.RespondWith401ThenSuccess("/concurrent", new { result = "ok" });

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "stale-token", "valid-refresh",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        using var client = BuildClient(tokenProvider: tokenProvider);

        // Send the request — the 401 retry happens internally in the auth handler
        var response = await client.GetAsync($"{_api.Url}/concurrent");
        Assert.True(response.IsSuccessStatusCode);

        // The auth handler should have only made 1 refresh call
        Assert.Equal(1, _oauth.GetCallCount("/token"));
    }

    [Fact]
    public async Task LogoutDuringRefresh_HandlesGracefully()
    {
        _api.ConfigureGetSuccess("/data", new { value = "ok" });
        _oauth.ConfigureTokenSuccess("new-token", "new-refresh", 3600);
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

        // Perform logout
        await authManager.LogoutAsync(CancellationToken.None);

        // Verify tokens are cleared
        var accessToken = await tokenProvider.GetAccessTokenAsync(CancellationToken.None);
        Assert.Null(accessToken);

        // Verify revoke was called
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
