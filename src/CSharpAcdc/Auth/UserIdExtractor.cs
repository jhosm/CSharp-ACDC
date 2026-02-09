using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace CSharpAcdc.Auth;

public sealed class UserIdExtractor
{
    private static readonly string[] ClaimPriority = ["sub", "user_id", "uid"];
    private static readonly JwtSecurityTokenHandler JwtHandler = new();

    private readonly IHttpContextAccessor? _httpContextAccessor;

    public UserIdExtractor(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? ExtractUserId(HttpRequestMessage request)
    {
        // First try HttpContext.User.Claims if accessor available
        if (_httpContextAccessor?.HttpContext?.User is { } principal)
        {
            var userId = ExtractFromClaims(principal.Claims);
            if (userId is not null)
                return userId;
        }

        // Fallback: parse JWT from Authorization header
        var authHeader = request.Headers.Authorization;
        if (authHeader?.Scheme?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrEmpty(authHeader.Parameter))
        {
            return ExtractFromJwt(authHeader.Parameter);
        }

        return null;
    }

    private static string? ExtractFromClaims(IEnumerable<Claim> claims)
    {
        var claimsList = claims as IList<Claim> ?? claims.ToList();
        foreach (var claimType in ClaimPriority)
        {
            var claim = claimsList.FirstOrDefault(c =>
                string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase));
            if (claim is not null)
                return claim.Value;
        }

        return null;
    }

    private static string? ExtractFromJwt(string token)
    {
        try
        {
            if (!JwtHandler.CanReadToken(token))
                return null;

            var jwt = JwtHandler.ReadJwtToken(token);
            return ExtractFromClaims(jwt.Claims);
        }
        catch
        {
            return null;
        }
    }
}
