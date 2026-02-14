using System.Net;
using CSharpAcdc.Auth;
using CSharpAcdc.Configuration;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RichardSzalay.MockHttp;
using Xunit;

namespace CSharpAcdc.Tests.Handlers;

public class AuthHandlerConcurrencyTests
{
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly ITokenRefreshStrategy _refreshStrategy = Substitute.For<ITokenRefreshStrategy>();
    private readonly BackoffManager _backoffManager = new();
    private readonly AcdcAuthOptions _authOptions = new()
    {
        RefreshEndpoint = "https://auth.example.com/token",
        ClientId = "test-client",
        RefreshThreshold = TimeSpan.FromSeconds(60),
        QueueTimeout = TimeSpan.FromSeconds(5),
    };

    private HttpClient CreateClient(MockHttpMessageHandler mockHandler)
    {
        var authHandler = new AuthHandler(
            _tokenProvider,
            _refreshStrategy,
            _backoffManager,
            Options.Create(_authOptions),
            NullLogger<AuthHandler>.Instance)
        {
            InnerHandler = mockHandler,
        };
        return new HttpClient(authHandler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };
    }

    [Fact]
    public async Task ConcurrentRequests_On401_OnlyOneRefreshOccurs()
    {
        var refreshCount = 0;
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token", "new-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                Interlocked.Increment(ref refreshCount);
                await Task.Delay(200); // Simulate network delay
                return new TokenRefreshResult("new-token", "new-refresh", DateTimeOffset.UtcNow.AddHours(1));
            });

        var callCount = 0;
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(_ =>
        {
            var count = Interlocked.Increment(ref callCount);
            // First 3 calls return 401, subsequent calls return 200
            return count <= 3
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(mockHandler);

        // Send 3 concurrent requests that all get 401
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => client.GetAsync($"/test/{Guid.NewGuid()}"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Only one refresh should have occurred (the leader does it, followers wait)
        refreshCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentRefresh_QueueTimeout_ThrowsAcdcAuthException()
    {
        var shortTimeoutOptions = new AcdcAuthOptions
        {
            RefreshEndpoint = "https://auth.example.com/token",
            ClientId = "test-client",
            RefreshThreshold = TimeSpan.FromSeconds(60),
            QueueTimeout = TimeSpan.FromMilliseconds(100), // Very short timeout
        };

        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        // Refresh takes longer than the queue timeout
        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(5000); // 5s, much longer than 100ms timeout
                return new TokenRefreshResult("new", "new-refresh", DateTimeOffset.UtcNow.AddHours(1));
            });

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.Unauthorized);

        var authHandler = new AuthHandler(
            _tokenProvider,
            _refreshStrategy,
            _backoffManager,
            Options.Create(shortTimeoutOptions),
            NullLogger<AuthHandler>.Instance)
        {
            InnerHandler = mockHandler,
        };

        using var client = new HttpClient(authHandler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        // First request: will acquire semaphore and start slow refresh
        var leaderTask = client.GetAsync("/leader");

        // Small delay to ensure leader has acquired semaphore
        await Task.Delay(50);

        // Second request: will try to wait on TCS but timeout
        // The follower should throw AcdcAuthException due to queue timeout
        var followerAct = () => client.GetAsync("/follower");
        var ex = await followerAct.Should().ThrowAsync<AcdcAuthException>();
        ex.Which.Message.Should().Contain("timed out");

        // Await the leader to prevent background task leak
        try { await leaderTask; } catch { /* Leader may also fail — we only care about follower behavior */ }
    }

    [Fact]
    public async Task MultipleSequential401s_EachTriggersRefresh()
    {
        var refreshCount = 0;
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token", "token-1", "token-2");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref refreshCount);
                return Task.FromResult(new TokenRefreshResult(
                    $"token-{count}", "new-refresh", DateTimeOffset.UtcNow.AddHours(1)));
            });

        var requestIndex = 0;
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(_ =>
        {
            var idx = Interlocked.Increment(ref requestIndex);
            // Odd requests: 401, Even requests: 200
            return idx % 2 == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(mockHandler);

        // First request gets 401, refreshes, retries with 200
        var response1 = await client.GetAsync("/first");
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request gets 401, refreshes, retries with 200
        var response2 = await client.GetAsync("/second");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        refreshCount.Should().Be(2);
    }
}
