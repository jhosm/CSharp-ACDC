using System.Net;
using CSharpAcdc.Auth;
using CSharpAcdc.Configuration;
using CSharpAcdc.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RichardSzalay.MockHttp;
using Xunit;

namespace CSharpAcdc.Tests.Auth;

public class OAuthTokenRefreshStrategyTests
{
    private readonly MockHttpMessageHandler _mockHttp = new();
    private readonly AcdcAuthOptions _authOptions = new()
    {
        RefreshEndpoint = "https://auth.example.com/token",
        ClientId = "test-client",
        ClientSecret = "test-secret",
    };

    private OAuthTokenRefreshStrategy CreateStrategy()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("acdc-auth").Returns(_ => _mockHttp.ToHttpClient());
        return new OAuthTokenRefreshStrategy(factory, Options.Create(_authOptions));
    }

    [Fact]
    public async Task RefreshAsync_Success_ReturnsTokens()
    {
        _mockHttp.When("https://auth.example.com/token")
            .Respond("application/json", """
            {
                "access_token": "new-access",
                "refresh_token": "new-refresh",
                "expires_in": 3600
            }
            """);

        var strategy = CreateStrategy();
        var result = await strategy.RefreshAsync("old-refresh", CancellationToken.None);

        result.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().Be("new-refresh");
        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RefreshAsync_SendsCorrectFormData()
    {
        _mockHttp.Expect("https://auth.example.com/token")
            .WithFormData("grant_type", "refresh_token")
            .WithFormData("refresh_token", "my-token")
            .WithFormData("client_id", "test-client")
            .WithFormData("client_secret", "test-secret")
            .Respond("application/json", """
            {
                "access_token": "a",
                "refresh_token": "r",
                "expires_in": 60
            }
            """);

        var strategy = CreateStrategy();
        await strategy.RefreshAsync("my-token", CancellationToken.None);

        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task RefreshAsync_InvalidGrant_ThrowsAcdcAuthException()
    {
        _mockHttp.When("https://auth.example.com/token")
            .Respond(HttpStatusCode.BadRequest, "application/json", """
            {"error": "invalid_grant", "error_description": "Token expired"}
            """);

        var strategy = CreateStrategy();
        var act = () => strategy.RefreshAsync("bad-token", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AcdcAuthException>();
        ex.Which.Message.Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task RefreshAsync_InvalidClient_ThrowsAcdcAuthException()
    {
        _mockHttp.When("https://auth.example.com/token")
            .Respond(HttpStatusCode.Unauthorized, "application/json", """
            {"error": "invalid_client"}
            """);

        var strategy = CreateStrategy();
        var act = () => strategy.RefreshAsync("token", CancellationToken.None);

        await act.Should().ThrowAsync<AcdcAuthException>();
    }

    [Fact]
    public async Task RefreshAsync_ServerError_ThrowsHttpRequestException()
    {
        _mockHttp.When("https://auth.example.com/token")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "Server error");

        var strategy = CreateStrategy();
        var act = () => strategy.RefreshAsync("token", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Should().NotBeOfType<AcdcAuthException>();
    }

    [Fact]
    public async Task RefreshAsync_Rfc1123DateFormat_ParsesCorrectly()
    {
        var futureDate = DateTimeOffset.UtcNow.AddHours(2);
        var rfc1123 = futureDate.ToString("R");

        _mockHttp.When("https://auth.example.com/token")
            .Respond("application/json", $$"""
            {
                "access_token": "new-access",
                "refresh_token": "new-refresh",
                "expires_in": "{{rfc1123}}"
            }
            """);

        var strategy = CreateStrategy();
        var result = await strategy.RefreshAsync("token", CancellationToken.None);

        result.ExpiresAt.Should().BeCloseTo(futureDate, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RefreshAsync_NoExpiresIn_DefaultsToOneHour()
    {
        _mockHttp.When("https://auth.example.com/token")
            .Respond("application/json", """
            {
                "access_token": "new-access",
                "refresh_token": "new-refresh"
            }
            """);

        var strategy = CreateStrategy();
        var result = await strategy.RefreshAsync("token", CancellationToken.None);

        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RefreshAsync_WithoutClientSecret_OmitsItFromRequest()
    {
        var optionsNoSecret = new AcdcAuthOptions
        {
            RefreshEndpoint = "https://auth.example.com/token",
            ClientId = "public-client",
        };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("acdc-auth").Returns(_ => _mockHttp.ToHttpClient());
        var strategy = new OAuthTokenRefreshStrategy(factory, Options.Create(optionsNoSecret));

        _mockHttp.Expect("https://auth.example.com/token")
            .WithFormData("grant_type", "refresh_token")
            .WithFormData("client_id", "public-client")
            .Respond("application/json", """
            {
                "access_token": "a",
                "refresh_token": "r",
                "expires_in": 3600
            }
            """);

        var result = await strategy.RefreshAsync("token", CancellationToken.None);
        result.AccessToken.Should().Be("a");
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task RefreshAsync_RefreshTokenOmitted_FallsBackToInputToken()
    {
        _mockHttp.When("https://auth.example.com/token")
            .Respond("application/json", """
            {
                "access_token": "new-access",
                "expires_in": 3600
            }
            """);

        var strategy = CreateStrategy();
        var result = await strategy.RefreshAsync("original-refresh", CancellationToken.None);

        result.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().Be("original-refresh");
    }

    [Fact]
    public async Task RefreshAsync_NumericStringExpiresIn_ParsesCorrectly()
    {
        _mockHttp.When("https://auth.example.com/token")
            .Respond("application/json", """
            {
                "access_token": "new-access",
                "refresh_token": "new-refresh",
                "expires_in": "7200"
            }
            """);

        var strategy = CreateStrategy();
        var result = await strategy.RefreshAsync("token", CancellationToken.None);

        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(2), TimeSpan.FromSeconds(5));
    }
}
