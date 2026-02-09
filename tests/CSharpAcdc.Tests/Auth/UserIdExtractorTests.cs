using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using CSharpAcdc.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace CSharpAcdc.Tests.Auth;

public class UserIdExtractorTests
{
    [Fact]
    public void ExtractUserId_WithSubClaim_ReturnsSubValue()
    {
        var accessor = CreateHttpContextAccessor(new Claim("sub", "user-123"));
        var extractor = new UserIdExtractor(accessor);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var userId = extractor.ExtractUserId(request);

        userId.Should().Be("user-123");
    }

    [Fact]
    public void ExtractUserId_WithUserIdClaim_ReturnsUserIdValue()
    {
        var accessor = CreateHttpContextAccessor(new Claim("user_id", "user-456"));
        var extractor = new UserIdExtractor(accessor);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var userId = extractor.ExtractUserId(request);

        userId.Should().Be("user-456");
    }

    [Fact]
    public void ExtractUserId_WithUidClaim_ReturnsUidValue()
    {
        var accessor = CreateHttpContextAccessor(new Claim("uid", "user-789"));
        var extractor = new UserIdExtractor(accessor);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var userId = extractor.ExtractUserId(request);

        userId.Should().Be("user-789");
    }

    [Fact]
    public void ExtractUserId_ClaimPriority_SubTakesPrecedence()
    {
        var accessor = CreateHttpContextAccessor(
            new Claim("uid", "uid-value"),
            new Claim("sub", "sub-value"),
            new Claim("user_id", "user_id-value"));
        var extractor = new UserIdExtractor(accessor);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var userId = extractor.ExtractUserId(request);

        userId.Should().Be("sub-value");
    }

    [Fact]
    public void ExtractUserId_NoClaims_FallsBackToJwt()
    {
        var accessor = CreateHttpContextAccessor(); // No claims
        var extractor = new UserIdExtractor(accessor);

        var jwt = CreateJwtToken(new Claim("sub", "jwt-user"));
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var userId = extractor.ExtractUserId(request);

        userId.Should().Be("jwt-user");
    }

    [Fact]
    public void ExtractUserId_NoAccessor_FallsBackToJwt()
    {
        var extractor = new UserIdExtractor(); // No IHttpContextAccessor

        var jwt = CreateJwtToken(new Claim("sub", "jwt-user-2"));
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var userId = extractor.ExtractUserId(request);

        userId.Should().Be("jwt-user-2");
    }

    [Fact]
    public void ExtractUserId_NoClaimsNoJwt_ReturnsNull()
    {
        var extractor = new UserIdExtractor();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var userId = extractor.ExtractUserId(request);

        userId.Should().BeNull();
    }

    [Fact]
    public void ExtractUserId_InvalidJwt_ReturnsNull()
    {
        var extractor = new UserIdExtractor();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");

        var userId = extractor.ExtractUserId(request);

        userId.Should().BeNull();
    }

    [Fact]
    public void ExtractUserId_NonBearerScheme_ReturnsNull()
    {
        var extractor = new UserIdExtractor();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

        var userId = extractor.ExtractUserId(request);

        userId.Should().BeNull();
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(params Claim[] claims)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        if (claims.Length > 0)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    private static string CreateJwtToken(params Claim[] claims)
    {
        var key = new SymmetricSecurityKey("super-secret-key-that-is-at-least-32-bytes-long!"u8.ToArray());
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "test",
            audience: "test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
