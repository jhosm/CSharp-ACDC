using CSharpAcdc.Auth;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Auth;

public class InMemoryTokenProviderTests
{
    private readonly InMemoryTokenProvider _provider = new();

    [Fact]
    public async Task GetAccessToken_Initially_ReturnsNull()
    {
        var token = await _provider.GetAccessTokenAsync(CancellationToken.None);
        token.Should().BeNull();
    }

    [Fact]
    public async Task GetRefreshToken_Initially_ReturnsNull()
    {
        var token = await _provider.GetRefreshTokenAsync(CancellationToken.None);
        token.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenExpiry_Initially_ReturnsNull()
    {
        var expiry = await _provider.GetTokenExpiryAsync(CancellationToken.None);
        expiry.Should().BeNull();
    }

    [Fact]
    public async Task SaveTokens_ThenGet_ReturnsStoredValues()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        await _provider.SaveTokensAsync("access", "refresh", expiresAt, CancellationToken.None);

        var accessToken = await _provider.GetAccessTokenAsync(CancellationToken.None);
        var refreshToken = await _provider.GetRefreshTokenAsync(CancellationToken.None);
        var expiry = await _provider.GetTokenExpiryAsync(CancellationToken.None);

        accessToken.Should().Be("access");
        refreshToken.Should().Be("refresh");
        expiry.Should().Be(expiresAt);
    }

    [Fact]
    public async Task ClearTokens_RemovesAllValues()
    {
        await _provider.SaveTokensAsync("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);
        await _provider.ClearTokensAsync(CancellationToken.None);

        var accessToken = await _provider.GetAccessTokenAsync(CancellationToken.None);
        var refreshToken = await _provider.GetRefreshTokenAsync(CancellationToken.None);
        var expiry = await _provider.GetTokenExpiryAsync(CancellationToken.None);

        accessToken.Should().BeNull();
        refreshToken.Should().BeNull();
        expiry.Should().BeNull();
    }

    [Fact]
    public async Task SaveTokens_Overwrites_PreviousValues()
    {
        await _provider.SaveTokensAsync("first", "refresh1", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);
        await _provider.SaveTokensAsync("second", "refresh2", DateTimeOffset.UtcNow.AddHours(2), CancellationToken.None);

        var accessToken = await _provider.GetAccessTokenAsync(CancellationToken.None);
        accessToken.Should().Be("second");
    }

    [Fact]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        var tasks = new List<Task>();
        for (var i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await _provider.SaveTokensAsync(
                    $"access-{index}",
                    $"refresh-{index}",
                    DateTimeOffset.UtcNow.AddHours(1),
                    CancellationToken.None);
                await _provider.GetAccessTokenAsync(CancellationToken.None);
                await _provider.GetRefreshTokenAsync(CancellationToken.None);
                await _provider.GetTokenExpiryAsync(CancellationToken.None);
            }));
        }

        // Should complete without deadlocks or exceptions
        await Task.WhenAll(tasks);

        // Final state should be one of the values
        var finalToken = await _provider.GetAccessTokenAsync(CancellationToken.None);
        finalToken.Should().StartWith("access-");
    }
}
