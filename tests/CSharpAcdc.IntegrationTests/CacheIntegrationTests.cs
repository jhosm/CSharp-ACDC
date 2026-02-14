using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CSharpAcdc.Auth;
using CSharpAcdc.Cache;
using CSharpAcdc.Client;
using CSharpAcdc.Extensions;
using CSharpAcdc.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CSharpAcdc.IntegrationTests;

public class CacheIntegrationTests : IDisposable
{
    private readonly FakeApiServer _api = new();
    private readonly FakeOAuthServer _oauth = new();

    [Fact]
    public async Task ETag_RoundTrip_Returns304WithCachedContent()
    {
        var etag = "abc123";
        _api.ConfigureGetWithETag("/etag-data", new { value = "cached" }, etag);
        _oauth.ConfigureTokenSuccess();

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "token", "refresh",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        using var client = BuildClient(
            tokenProvider: tokenProvider,
            cacheDuration: TimeSpan.FromMilliseconds(100), // Short cache so it expires
            etagEnabled: true);

        // First request: should get 200 with full body
        var response1 = await client.GetAsync($"{_api.Url}/etag-data");
        Assert.True(response1.IsSuccessStatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();
        Assert.Contains("cached", body1);

        // Wait for cache to expire
        await Task.Delay(200);

        // Second request: cache expired, should revalidate with If-None-Match and get 304
        var response2 = await client.GetAsync($"{_api.Url}/etag-data");
        Assert.True(response2.IsSuccessStatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();
        Assert.Contains("cached", body2);

        // Verify If-None-Match was sent
        var ifNoneMatchHeaders = _api.GetIfNoneMatchHeaders("/etag-data");
        Assert.True(ifNoneMatchHeaders.Count >= 2, "Expected at least 2 requests");
        // The second request should have If-None-Match
        Assert.NotNull(ifNoneMatchHeaders[1]);
        Assert.Contains(etag, ifNoneMatchHeaders[1]!);
    }

    [Fact]
    public async Task StaleWhileRevalidate_ReturnsStaleDataWhileRefreshing()
    {
        // Configure a slow endpoint
        _api.ConfigureGetWithDelay("/slow", new { value = "fresh" }, TimeSpan.FromSeconds(5));
        _oauth.ConfigureTokenSuccess();

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "token", "refresh",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        using var client = BuildClient(
            tokenProvider: tokenProvider,
            cacheDuration: TimeSpan.FromMilliseconds(100),
            etagEnabled: false,
            failSafeMaxDuration: TimeSpan.FromMinutes(5),
            factorySoftTimeout: TimeSpan.FromMilliseconds(50));

        // Pre-populate cache by making initial request (need a fast response first)
        _api.Reset();
        _api.ConfigureGetSuccess("/slow", new { value = "stale" });

        var response1 = await client.GetAsync($"{_api.Url}/slow");
        Assert.True(response1.IsSuccessStatusCode);

        // Now configure a slow response
        _api.Reset();
        _api.ConfigureGetWithDelay("/slow", new { value = "fresh" }, TimeSpan.FromSeconds(5));

        // Wait for cache to expire
        await Task.Delay(200);

        // Second request: should return stale data quickly via SWR
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var response2 = await client.GetAsync($"{_api.Url}/slow", cts.Token);
        Assert.True(response2.IsSuccessStatusCode);

        // The response should come back quickly (stale from cache)
        var body = await response2.Content.ReadAsStringAsync();
        Assert.Contains("stale", body);
    }

    [Fact]
    public async Task MutationInvalidation_PostInvalidatesCachedGet()
    {
        _api.ConfigureGetSuccess("/items", new { items = new[] { "a", "b" } });
        _api.ConfigurePost("/items", 201, new { id = 1 });
        _oauth.ConfigureTokenSuccess();

        var tokenProvider = new InMemoryTokenProvider();
        await tokenProvider.SaveTokensAsync(
            "token", "refresh",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        using var client = BuildClient(
            tokenProvider: tokenProvider,
            cacheDuration: TimeSpan.FromMinutes(5),
            etagEnabled: false);

        // GET to populate cache
        var response1 = await client.GetAsync($"{_api.Url}/items");
        Assert.True(response1.IsSuccessStatusCode);
        Assert.Equal(1, _api.GetCallCount("GET", "/items"));

        // POST to invalidate cache
        await client.PostAsync($"{_api.Url}/items", new StringContent("{}", Encoding.UTF8, "application/json"));

        // GET again — should hit the server since cache was invalidated
        var response2 = await client.GetAsync($"{_api.Url}/items");
        Assert.True(response2.IsSuccessStatusCode);
        Assert.Equal(2, _api.GetCallCount("GET", "/items"));
    }

    [Fact]
    public async Task UserIsolation_DifferentUsersGetDifferentCacheEntries()
    {
        _api.ConfigureGetSuccess("/user-data", new { data = "shared" });
        _oauth.ConfigureTokenSuccess();

        // Create JWTs for two different users
        var user1Token = CreateJwt("user-1");
        var user2Token = CreateJwt("user-2");

        var tokenProvider1 = new InMemoryTokenProvider();
        await tokenProvider1.SaveTokensAsync(
            user1Token, "refresh-1",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        using var client1 = BuildClient(
            tokenProvider: tokenProvider1,
            cacheDuration: TimeSpan.FromMinutes(5),
            etagEnabled: false,
            cacheKeyStrategy: CacheKeyStrategy.UserIsolated);

        // User 1 makes a request
        var response1 = await client1.GetAsync($"{_api.Url}/user-data");
        Assert.True(response1.IsSuccessStatusCode);

        // User 1 again — should be cached
        var response2 = await client1.GetAsync($"{_api.Url}/user-data");
        Assert.True(response2.IsSuccessStatusCode);

        // Should only have hit the server once for user 1
        Assert.Equal(1, _api.GetCallCount("/user-data"));

        // Now user 2 — different cache key due to UserIsolated strategy
        var tokenProvider2 = new InMemoryTokenProvider();
        await tokenProvider2.SaveTokensAsync(
            user2Token, "refresh-2",
            DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None);

        using var client2 = BuildClient(
            tokenProvider: tokenProvider2,
            cacheDuration: TimeSpan.FromMinutes(5),
            etagEnabled: false,
            cacheKeyStrategy: CacheKeyStrategy.UserIsolated,
            clientName: "acdc-user2");

        var response3 = await client2.GetAsync($"{_api.Url}/user-data");
        Assert.True(response3.IsSuccessStatusCode);

        // Server should have been hit again for user 2 since it's a different cache
        Assert.Equal(2, _api.GetCallCount("/user-data"));
    }

    private static string CreateJwt(string userId)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("super-secret-key-for-testing-only-at-least-32-bytes"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: [new Claim("sub", userId)],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private AcdcHttpClient BuildClient(
        InMemoryTokenProvider? tokenProvider = null,
        TimeSpan? cacheDuration = null,
        bool etagEnabled = true,
        TimeSpan? failSafeMaxDuration = null,
        TimeSpan? factorySoftTimeout = null,
        CacheKeyStrategy cacheKeyStrategy = CacheKeyStrategy.Shared,
        string clientName = "acdc")
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAcdcHttpClient(clientName, b =>
        {
            b = b.WithAuth(auth =>
            {
                auth.RefreshEndpoint = _oauth.TokenEndpoint;
                auth.ClientId = "test-client";
            });

            b = b.WithCache(cache =>
            {
                cache.Duration = cacheDuration ?? TimeSpan.FromMinutes(5);
                cache.ETagEnabled = etagEnabled;
                cache.CacheKeyStrategy = cacheKeyStrategy;
                if (failSafeMaxDuration.HasValue)
                    cache.FailSafeMaxDuration = failSafeMaxDuration.Value;
                if (factorySoftTimeout.HasValue)
                    cache.FactorySoftTimeout = factorySoftTimeout.Value;
            });

            return b.WithClientName(clientName);
        });

        if (tokenProvider is not null)
        {
            services.AddKeyedSingleton<ITokenProvider>(clientName, tokenProvider);
        }

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredKeyedService<AcdcHttpClient>(clientName);
    }

    public void Dispose()
    {
        _api.Dispose();
        _oauth.Dispose();
    }
}
