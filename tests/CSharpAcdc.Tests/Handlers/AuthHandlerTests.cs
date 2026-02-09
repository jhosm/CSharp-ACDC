using System.Net;
using System.Net.Http.Headers;
using CSharpAcdc.Auth;
using CSharpAcdc.Configuration;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Extensions;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RichardSzalay.MockHttp;
using Xunit;

namespace CSharpAcdc.Tests.Handlers;

public class AuthHandlerTests
{
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly ITokenRefreshStrategy _refreshStrategy = Substitute.For<ITokenRefreshStrategy>();
    private readonly BackoffManager _backoffManager = new();
    private readonly AcdcAuthOptions _authOptions = new()
    {
        RefreshEndpoint = "https://auth.example.com/token",
        ClientId = "test-client",
        RefreshThreshold = TimeSpan.FromSeconds(60),
        QueueTimeout = TimeSpan.FromSeconds(30),
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
    public async Task SendAsync_InjectsAccessToken()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("my-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1)); // Not near expiry

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*")
            .With(req => req.Headers.Authorization?.Parameter == "my-token")
            .Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_NoToken_DoesNotSetAuthHeader()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns((string?)null);

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*")
            .With(req => req.Headers.Authorization is null)
            .Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_SkipAuth_BypassesTokenInjection()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("should-not-be-used");

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*")
            .With(req => req.Headers.Authorization is null)
            .Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Options.Set(AcdcRequestOptions.SkipAuth, true);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_On401_RetriesWithNewToken()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token", "new-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        var refreshResult = new TokenRefreshResult("new-token", "new-refresh", DateTimeOffset.UtcNow.AddHours(1));
        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .Returns(refreshResult);

        var callCount = 0;
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_On401_NoRefreshToken_Returns401()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.Unauthorized);

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendAsync_On401_RefreshFails_Returns401()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AcdcAuthException("Token refresh failed: invalid_grant"));

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.Unauthorized);

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _tokenProvider.Received(1).ClearTokensAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_On401_TransientRefreshFailure_RecordsBackoff()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.Unauthorized);

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var attempt = await _backoffManager.GetAttemptAsync();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_SuccessfulResponse_PassesThroughDirectly()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("my-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK, "application/json", """{"ok": true}""");

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_On401_RetryAlso401_Returns401WithoutFurtherRetry()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token", "new-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        var refreshResult = new TokenRefreshResult("new-token", "new-refresh", DateTimeOffset.UtcNow.AddHours(1));
        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .Returns(refreshResult);

        var callCount = 0;
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        // Should have made exactly 2 calls (original + 1 retry), not 3
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_RequestWithContent_ClonesCorrectly()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("old-token", "new-token");
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _tokenProvider.GetTokenExpiryAsync(Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1));

        var refreshResult = new TokenRefreshResult("new-token", "new-refresh", DateTimeOffset.UtcNow.AddHours(1));
        _refreshStrategy.RefreshAsync("refresh-token", Arg.Any<CancellationToken>())
            .Returns(refreshResult);

        string? capturedBody = null;
        var callCount = 0;
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(async req =>
        {
            callCount++;
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(mockHandler);
        var request = new HttpRequestMessage(HttpMethod.Post, "/test")
        {
            Content = new StringContent("""{"data": "test"}""", System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedBody.Should().Be("""{"data": "test"}""");
    }
}
