using System.Net;
using CSharpAcdc.Cache;
using CSharpAcdc.Configuration;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace CSharpAcdc.Tests.Handlers;

public class CacheHandlerSwrTests : IDisposable
{
    private readonly FusionCache _fusionCache = new(new FusionCacheOptions());
    private readonly AcdcCacheManager _cacheManager;
    private readonly List<IDisposable> _disposables = [];

    public CacheHandlerSwrTests()
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
        _disposables.Add(handler);
        _disposables.Add(client);
        return (handler, client);
    }

    [Fact]
    public async Task Get_FactorySoftTimeout_ReturnsStaleData()
    {
        var callCount = 0;

        var (_, client) = CreatePipeline(async (_, ct) =>
        {
            Interlocked.Increment(ref callCount);
            if (callCount > 1)
            {
                // Simulate slow factory exceeding FactorySoftTimeout
                await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"data-{callCount}"),
            };
        }, new AcdcCacheOptions
        {
            Duration = TimeSpan.FromMilliseconds(1),
            FactorySoftTimeout = TimeSpan.FromMilliseconds(50),
            FailSafeMaxDuration = TimeSpan.FromHours(1),
            AllowTimedOutFactoryBackgroundCompletion = true,
        });

        // First call -- populate cache
        using var response1 = await client.GetAsync("https://api.example.com/data");
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Be("data-1");

        // Wait for cache to expire
        await Task.Delay(50);

        // Second call -- factory is slow, should get stale data via fail-safe
        using var response2 = await client.GetAsync("https://api.example.com/data");
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Be("data-1"); // Stale data returned quickly
    }

    [Fact]
    public async Task Get_FailSafe_ReturnsStaleDataOnError()
    {
        var callCount = 0;

        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            if (callCount > 1)
            {
                throw new HttpRequestException("Downstream is down");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("original-data"),
            });
        }, new AcdcCacheOptions
        {
            Duration = TimeSpan.FromMilliseconds(1),
            FailSafeMaxDuration = TimeSpan.FromHours(1),
        });

        // First call -- populate cache
        using var response1 = await client.GetAsync("https://api.example.com/data");
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Be("original-data");

        // Wait for cache to expire
        await Task.Delay(50);

        // Second call -- factory throws, should get stale data via fail-safe
        using var response2 = await client.GetAsync("https://api.example.com/data");
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Be("original-data");
    }

    [Fact]
    public async Task Get_NoFailSafe_ErrorPropagates()
    {
        var callCount = 0;

        var (_, client) = CreatePipeline((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            if (callCount > 1)
            {
                throw new HttpRequestException("Downstream is down");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data"),
            });
        }, new AcdcCacheOptions
        {
            Duration = TimeSpan.FromMilliseconds(1),
            // FailSafeMaxDuration is null -- fail-safe is disabled
        });

        // First call -- populate cache
        (await client.GetAsync("https://api.example.com/data")).Dispose();

        // Wait for cache to expire
        await Task.Delay(50);

        // Second call -- factory throws and, with fail-safe disabled, the exception should propagate to the caller.
        var act = () => client.GetAsync("https://api.example.com/data");
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Get_FailSafeEnabled_ConfiguredCorrectly()
    {
        var options = new AcdcCacheOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            FailSafeMaxDuration = TimeSpan.FromHours(24),
            FactorySoftTimeout = TimeSpan.FromMilliseconds(100),
            AllowTimedOutFactoryBackgroundCompletion = true,
        };

        options.Duration.Should().Be(TimeSpan.FromMinutes(5));
        options.FailSafeMaxDuration.Should().Be(TimeSpan.FromHours(24));
        options.FactorySoftTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
        options.AllowTimedOutFactoryBackgroundCompletion.Should().BeTrue();
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
