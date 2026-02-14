using System.Net;
using CSharpAcdc.Cache;
using CSharpAcdc.Configuration;
using CSharpAcdc.Extensions;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace CSharpAcdc.Tests.Handlers;

public class CacheHandlerTests : IDisposable
{
    private readonly FusionCache _fusionCache = new(new FusionCacheOptions());
    private readonly AcdcCacheOptions _cacheOptions = new();
    private readonly AcdcCacheManager _cacheManager;
    private readonly List<IDisposable> _disposables = [];

    public CacheHandlerTests()
    {
        _cacheManager = new AcdcCacheManager(_fusionCache, NullLogger<AcdcCacheManager>.Instance);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
        _fusionCache.Dispose();
        GC.SuppressFinalize(this);
    }

    private (CacheHandler handler, HttpClient client) CreatePipeline(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory,
        AcdcCacheOptions? options = null,
        Func<HttpRequestMessage, string?>? userIdProvider = null)
    {
        var opts = Options.Create(options ?? _cacheOptions);
        var handler = new CacheHandler(
            _fusionCache, opts, NullLogger<CacheHandler>.Instance,
            userIdProvider, _cacheManager)
        {
            InnerHandler = new StubHandler(responseFactory),
        };
        var client = new HttpClient(handler);
        _disposables.Add(handler);
        _disposables.Add(client);
        return (handler, client);
    }

    [Fact]
    public async Task Get_CacheMiss_CallsDownstream()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello"),
            });
        });

        using var response = await client.GetAsync("https://api.example.com/data");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Get_CacheHit_DoesNotCallDownstreamAgain()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello"),
            });
        });

        (await client.GetAsync("https://api.example.com/data")).Dispose();
        using var response = await client.GetAsync("https://api.example.com/data");

        callCount.Should().Be(1);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("hello");
    }

    [Fact]
    public async Task Get_CacheHit_AddsFromCacheHeader()
    {
        var (_, client) = CreatePipeline((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            }));

        (await client.GetAsync("https://api.example.com/data")).Dispose();
        using var response = await client.GetAsync("https://api.example.com/data");

        response.Headers.Contains("X-ACDC-From-Cache").Should().BeTrue();
        response.Headers.GetValues("X-ACDC-From-Cache").Should().Contain("true");
    }

    [Fact]
    public async Task Get_CacheMiss_NoFromCacheHeader()
    {
        var (_, client) = CreatePipeline((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            }));

        using var response = await client.GetAsync("https://api.example.com/data");

        response.Headers.Contains("X-ACDC-From-Cache").Should().BeFalse();
    }

    [Fact]
    public async Task Post_BypassesCache()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            });
        });

        (await client.PostAsync("https://api.example.com/data", new StringContent("body"))).Dispose();
        (await client.PostAsync("https://api.example.com/data", new StringContent("body"))).Dispose();

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Put_BypassesCache()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        (await client.PutAsync("https://api.example.com/data", new StringContent("body"))).Dispose();
        (await client.PutAsync("https://api.example.com/data", new StringContent("body"))).Dispose();

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Delete_BypassesCache()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        (await client.DeleteAsync("https://api.example.com/data")).Dispose();
        (await client.DeleteAsync("https://api.example.com/data")).Dispose();

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Get_SkipCacheOption_BypassesCache()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("fresh"),
            });
        });

        // First call populates the cache
        (await client.GetAsync("https://api.example.com/data")).Dispose();

        // Second call with SkipCache should call downstream again
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Options.Set(AcdcRequestOptions.SkipCache, true);
        (await client.SendAsync(request)).Dispose();

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Get_NoCacheStrategy_BypassesCache()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            });
        }, new AcdcCacheOptions { CacheKeyStrategy = CacheKeyStrategy.NoCache });

        (await client.GetAsync("https://api.example.com/data")).Dispose();
        (await client.GetAsync("https://api.example.com/data")).Dispose();

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Get_UserIsolatedStrategy_DifferentUsersGetDifferentCache()
    {
        var callCount = 0;
        string? currentUser = "user-1";
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"data-for-call-{callCount}"),
            });
        },
        new AcdcCacheOptions { CacheKeyStrategy = CacheKeyStrategy.UserIsolated },
        userIdProvider: _ => currentUser);

        (await client.GetAsync("https://api.example.com/profile")).Dispose();

        currentUser = "user-2";
        (await client.GetAsync("https://api.example.com/profile")).Dispose();

        // Both users should trigger separate downstream calls
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Get_UserIsolatedStrategy_SameUserGetsCachedResponse()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("user-data"),
            });
        },
        new AcdcCacheOptions { CacheKeyStrategy = CacheKeyStrategy.UserIsolated },
        userIdProvider: _ => "user-1");

        (await client.GetAsync("https://api.example.com/profile")).Dispose();
        (await client.GetAsync("https://api.example.com/profile")).Dispose();

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Post_InvalidatesRelatedGetCache()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((req, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"response-{callCount}"),
            });
        });

        // Populate cache with GET
        (await client.GetAsync("https://api.example.com/items")).Dispose();
        callCount.Should().Be(1);

        // POST should invalidate GET cache for the same URL
        (await client.PostAsync("https://api.example.com/items", new StringContent("new item"))).Dispose();
        callCount.Should().Be(2);

        // Next GET should call downstream (cache was invalidated)
        (await client.GetAsync("https://api.example.com/items")).Dispose();
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task Get_CacheMaxAge_OverridesDuration()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            });
        }, new AcdcCacheOptions { Duration = TimeSpan.FromHours(1) });

        // Set per-request cache max age
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Options.Set(AcdcRequestOptions.CacheMaxAge, TimeSpan.FromMilliseconds(1));
        (await client.SendAsync(request)).Dispose();

        // Wait for the short TTL to expire
        await Task.Delay(50);

        // Should need a fresh call
        (await client.GetAsync("https://api.example.com/data")).Dispose();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Head_IsCached()
    {
        var callCount = 0;
        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var request1 = new HttpRequestMessage(HttpMethod.Head, "https://api.example.com/health");
        (await client.SendAsync(request1)).Dispose();

        using var request2 = new HttpRequestMessage(HttpMethod.Head, "https://api.example.com/health");
        (await client.SendAsync(request2)).Dispose();

        callCount.Should().Be(1);
    }

    private sealed class StubHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
