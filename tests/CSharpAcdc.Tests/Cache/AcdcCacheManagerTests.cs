using CSharpAcdc.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace CSharpAcdc.Tests.Cache;

public class AcdcCacheManagerTests
{
    private readonly FusionCache _fusionCache = new(new FusionCacheOptions());
    private readonly AcdcCacheManager _manager;

    public AcdcCacheManagerTests()
    {
        _manager = new AcdcCacheManager(_fusionCache, NullLogger<AcdcCacheManager>.Instance);
    }

    [Fact]
    public async Task ClearCacheAsync_RemovesAllTrackedEntries()
    {
        // Arrange
        await _fusionCache.SetAsync("GET:https://api.example.com/a", new CachedResponse([], new(), 200, null));
        await _fusionCache.SetAsync("GET:https://api.example.com/b", new CachedResponse([], new(), 200, null));
        _manager.TrackKey("GET:https://api.example.com/a", "https://api.example.com/a");
        _manager.TrackKey("GET:https://api.example.com/b", "https://api.example.com/b");

        // Act
        await _manager.ClearCacheAsync();

        // Assert
        var a = await _fusionCache.TryGetAsync<CachedResponse>("GET:https://api.example.com/a");
        var b = await _fusionCache.TryGetAsync<CachedResponse>("GET:https://api.example.com/b");
        a.HasValue.Should().BeFalse();
        b.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task ClearCacheForUrlAsync_RemovesOnlyMatchingUrl()
    {
        // Arrange
        await _fusionCache.SetAsync("GET:https://api.example.com/a", new CachedResponse([], new(), 200, null));
        await _fusionCache.SetAsync("GET:https://api.example.com/b", new CachedResponse([], new(), 200, null));
        _manager.TrackKey("GET:https://api.example.com/a", "https://api.example.com/a");
        _manager.TrackKey("GET:https://api.example.com/b", "https://api.example.com/b");

        // Act
        await _manager.ClearCacheForUrlAsync("https://api.example.com/a");

        // Assert
        var a = await _fusionCache.TryGetAsync<CachedResponse>("GET:https://api.example.com/a");
        var b = await _fusionCache.TryGetAsync<CachedResponse>("GET:https://api.example.com/b");
        a.HasValue.Should().BeFalse();
        b.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task ClearCacheForUserAsync_RemovesOnlyUserKeys()
    {
        // Arrange
        await _fusionCache.SetAsync("GET:user-42:https://api.example.com/profile",
            new CachedResponse([], new(), 200, null));
        await _fusionCache.SetAsync("GET:user-99:https://api.example.com/profile",
            new CachedResponse([], new(), 200, null));
        await _fusionCache.SetAsync("GET:https://api.example.com/shared",
            new CachedResponse([], new(), 200, null));

        _manager.TrackKey("GET:user-42:https://api.example.com/profile", "https://api.example.com/profile");
        _manager.TrackKey("GET:user-99:https://api.example.com/profile", "https://api.example.com/profile");
        _manager.TrackKey("GET:https://api.example.com/shared", "https://api.example.com/shared");

        // Act
        await _manager.ClearCacheForUserAsync("user-42");

        // Assert
        var user42 = await _fusionCache.TryGetAsync<CachedResponse>("GET:user-42:https://api.example.com/profile");
        var user99 = await _fusionCache.TryGetAsync<CachedResponse>("GET:user-99:https://api.example.com/profile");
        var shared = await _fusionCache.TryGetAsync<CachedResponse>("GET:https://api.example.com/shared");

        user42.HasValue.Should().BeFalse();
        user99.HasValue.Should().BeTrue();
        shared.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateForBaseUrlAsync_RemovesAllKeysForBaseUrl()
    {
        // Arrange
        await _fusionCache.SetAsync("GET:https://api.example.com/items",
            new CachedResponse([], new(), 200, null));
        await _fusionCache.SetAsync("GET:user-1:https://api.example.com/items",
            new CachedResponse([], new(), 200, null));

        _manager.TrackKey("GET:https://api.example.com/items", "https://api.example.com/items");
        _manager.TrackKey("GET:user-1:https://api.example.com/items", "https://api.example.com/items");

        // Act
        await _manager.InvalidateForBaseUrlAsync("https://api.example.com/items");

        // Assert
        var shared = await _fusionCache.TryGetAsync<CachedResponse>("GET:https://api.example.com/items");
        var user = await _fusionCache.TryGetAsync<CachedResponse>("GET:user-1:https://api.example.com/items");
        shared.HasValue.Should().BeFalse();
        user.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task ClearCacheForUrlAsync_NonExistentUrl_DoesNotThrow()
    {
        await _manager.Invoking(m => m.ClearCacheForUrlAsync("https://nonexistent.com/path"))
            .Should().NotThrowAsync();
    }
}
