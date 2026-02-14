using System.Net;
using CSharpAcdc.Auth;
using CSharpAcdc.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RichardSzalay.MockHttp;
using Xunit;

namespace CSharpAcdc.Tests.Auth;

public class AcdcAuthManagerTests
{
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly ITokenRefreshStrategy _refreshStrategy = Substitute.For<ITokenRefreshStrategy>();
    private readonly BackoffManager _backoffManager = new();
    private readonly MockHttpMessageHandler _mockHttp = new();
    private readonly AcdcAuthOptions _authOptions = new()
    {
        RefreshEndpoint = "https://auth.example.com/token",
        ClientId = "test-client",
        RevocationEndpoint = "https://auth.example.com/revoke",
    };

    private AcdcAuthManager CreateManager()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("acdc-auth").Returns(_ => _mockHttp.ToHttpClient());
        return new AcdcAuthManager(
            _tokenProvider,
            _refreshStrategy,
            _backoffManager,
            factory,
            Options.Create(_authOptions),
            NullLogger<AcdcAuthManager>.Instance,
            new UserIdExtractor());
    }

    [Fact]
    public async Task LogoutAsync_ClearsTokens()
    {
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _mockHttp.When("https://auth.example.com/revoke")
            .Respond(HttpStatusCode.OK);

        var manager = CreateManager();
        await manager.LogoutAsync(CancellationToken.None);

        await _tokenProvider.Received(1).ClearTokensAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LogoutAsync_SendsRevocationRequest()
    {
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _mockHttp.Expect("https://auth.example.com/revoke")
            .WithFormData("token", "refresh-token")
            .WithFormData("token_type_hint", "refresh_token")
            .Respond(HttpStatusCode.OK);

        var manager = CreateManager();
        await manager.LogoutAsync(CancellationToken.None);

        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task LogoutAsync_RevocationFailure_DoesNotThrow()
    {
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _mockHttp.When("https://auth.example.com/revoke")
            .Throw(new HttpRequestException("Network error"));

        var manager = CreateManager();

        var act = () => manager.LogoutAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LogoutAsync_NoRevocationEndpoint_SkipsRevocation()
    {
        var optionsNoRevoke = new AcdcAuthOptions
        {
            RefreshEndpoint = "https://auth.example.com/token",
            ClientId = "test-client",
        };

        var factory = Substitute.For<IHttpClientFactory>();
        var manager = new AcdcAuthManager(
            _tokenProvider,
            _refreshStrategy,
            _backoffManager,
            factory,
            Options.Create(optionsNoRevoke),
            NullLogger<AcdcAuthManager>.Instance,
            new UserIdExtractor());

        await manager.LogoutAsync(CancellationToken.None);

        // Should not call CreateClient since no revocation needed
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task LogoutAsync_ResetsBackoff()
    {
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refresh-token");
        _mockHttp.When("https://auth.example.com/revoke")
            .Respond(HttpStatusCode.OK);

        await _backoffManager.RecordFailureAsync();
        var manager = CreateManager();
        await manager.LogoutAsync(CancellationToken.None);

        var attempt = await _backoffManager.GetAttemptAsync();
        attempt.Should().Be(0);
    }

    [Fact]
    public async Task ForceRefreshAsync_RefreshesTokens()
    {
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns("current-refresh");

        var result = new TokenRefreshResult("new-access", "new-refresh", DateTimeOffset.UtcNow.AddHours(1));
        _refreshStrategy.RefreshAsync("current-refresh", Arg.Any<CancellationToken>())
            .Returns(result);

        var manager = CreateManager();
        await manager.ForceRefreshAsync(CancellationToken.None);

        await _tokenProvider.Received(1).SaveTokensAsync(
            "new-access", "new-refresh", result.ExpiresAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForceRefreshAsync_NoRefreshToken_DoesNothing()
    {
        _tokenProvider.GetRefreshTokenAsync(Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var manager = CreateManager();
        await manager.ForceRefreshAsync(CancellationToken.None);

        await _refreshStrategy.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetUserId_DelegatesToExtractor()
    {
        var manager = CreateManager();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        // No claims, no JWT — should return null
        var userId = manager.GetUserId(request);
        userId.Should().BeNull();
    }
}
