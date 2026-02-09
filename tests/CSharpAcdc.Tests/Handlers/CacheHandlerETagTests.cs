using System.Net;
using System.Net.Http.Headers;
using CSharpAcdc.Cache;
using CSharpAcdc.Configuration;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace CSharpAcdc.Tests.Handlers;

public class CacheHandlerETagTests : IDisposable
{
    private readonly FusionCache _fusionCache = new(new FusionCacheOptions());
    private readonly AcdcCacheManager _cacheManager;

    public CacheHandlerETagTests()
    {
        _cacheManager = new AcdcCacheManager(_fusionCache, NullLogger<AcdcCacheManager>.Instance);
    }

    public void Dispose()
    {
        _fusionCache.Dispose();
        GC.SuppressFinalize(this);
    }

    private (CacheHandler handler, HttpClient client) CreatePipeline(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory,
        AcdcCacheOptions? options = null)
    {
        var opts = Options.Create(options ?? new AcdcCacheOptions());
        var handler = new CacheHandler(
            _fusionCache, opts, NullLogger<CacheHandler>.Instance,
            cacheManager: _cacheManager)
        {
            InnerHandler = new StubHandler(responseFactory),
        };
        var client = new HttpClient(handler);
        return (handler, client);
    }

    [Fact]
    public async Task Get_ResponseWithETag_StoresETag()
    {
        var (_, client) = CreatePipeline((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        });

        await client.GetAsync("https://api.example.com/data");

        // Verify ETag was stored in cache
        var cached = await _fusionCache.TryGetAsync<CachedResponse>("GET:https://api.example.com/data");
        cached.HasValue.Should().BeTrue();
        cached.Value.ETag.Should().Be("v1");
    }

    [Fact]
    public async Task Get_CachedWithETag_SendsIfNoneMatch()
    {
        string? receivedIfNoneMatch = null;
        var callCount = 0;

        var (_, client) = CreatePipeline((req, _) =>
        {
            Interlocked.Increment(ref callCount);
            receivedIfNoneMatch = req.Headers.IfNoneMatch.FirstOrDefault()?.Tag;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }, new AcdcCacheOptions { Duration = TimeSpan.FromMilliseconds(1) });

        // First call — no If-None-Match
        await client.GetAsync("https://api.example.com/data");
        receivedIfNoneMatch.Should().BeNull();

        // Wait for cache to expire so factory runs again
        await Task.Delay(50);

        // Second call — should send If-None-Match with cached ETag
        await client.GetAsync("https://api.example.com/data");
        callCount.Should().Be(2);
        receivedIfNoneMatch.Should().Be("\"v1\"");
    }

    [Fact]
    public async Task Get_304NotModified_ReturnsCachedContent()
    {
        var callCount = 0;

        var (_, client) = CreatePipeline((req, _) =>
        {
            Interlocked.Increment(ref callCount);
            if (req.Headers.IfNoneMatch.Any())
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("original-data"),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }, new AcdcCacheOptions { Duration = TimeSpan.FromMilliseconds(1) });

        // First call — populates cache
        var response1 = await client.GetAsync("https://api.example.com/data");
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Be("original-data");

        // Wait for cache to expire
        await Task.Delay(50);

        // Second call — server returns 304, should get cached content
        var response2 = await client.GetAsync("https://api.example.com/data");
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Be("original-data");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Get_ETagDisabled_DoesNotSendIfNoneMatch()
    {
        var sentIfNoneMatch = false;

        var (_, client) = CreatePipeline((req, _) =>
        {
            sentIfNoneMatch = req.Headers.IfNoneMatch.Any();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }, new AcdcCacheOptions { ETagEnabled = false, Duration = TimeSpan.FromMilliseconds(1) });

        await client.GetAsync("https://api.example.com/data");
        await Task.Delay(50);
        await client.GetAsync("https://api.example.com/data");

        sentIfNoneMatch.Should().BeFalse();
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
