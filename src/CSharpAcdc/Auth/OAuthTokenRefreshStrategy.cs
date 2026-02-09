using System.Globalization;
using System.Net;
using System.Text.Json;
using CSharpAcdc.Configuration;
using CSharpAcdc.Exceptions;
using Microsoft.Extensions.Options;

namespace CSharpAcdc.Auth;

public sealed class OAuthTokenRefreshStrategy : ITokenRefreshStrategy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AcdcAuthOptions _options;

    public OAuthTokenRefreshStrategy(
        IHttpClientFactory httpClientFactory,
        IOptions<AcdcAuthOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("acdc-auth");

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ClientId,
        };

        if (!string.IsNullOrEmpty(_options.ClientSecret))
            parameters["client_secret"] = _options.ClientSecret;

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await client.PostAsync(_options.RefreshEndpoint, content, ct)
            .ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            HandleErrorResponse(response.StatusCode, responseBody);
        }

        return ParseSuccessResponse(responseBody);
    }

    private static void HandleErrorResponse(HttpStatusCode statusCode, string responseBody)
    {
        string? errorCode = null;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorElement))
                errorCode = errorElement.GetString();
        }
        catch (JsonException)
        {
            // Ignore parse failures
        }

        if (errorCode is "invalid_grant" or "invalid_client")
        {
            throw new AcdcAuthException(
                $"Token refresh failed: {errorCode}",
                statusCode,
                AcdcException.TruncateResponseBody(responseBody));
        }

        // Non-auth error (transient) — throw standard HttpRequestException
        throw new HttpRequestException(
            $"Token refresh failed with status {(int)statusCode}: {AcdcException.TruncateResponseBody(responseBody)}",
            inner: null,
            statusCode);
    }

    private static TokenRefreshResult ParseSuccessResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Missing access_token in refresh response");

        var newRefreshToken = root.GetProperty("refresh_token").GetString()
            ?? throw new InvalidOperationException("Missing refresh_token in refresh response");

        var expiresAt = ParseExpiry(root);

        return new TokenRefreshResult(accessToken, newRefreshToken, expiresAt);
    }

    private static DateTimeOffset ParseExpiry(JsonElement root)
    {
        if (root.TryGetProperty("expires_in", out var expiresInElement))
        {
            if (expiresInElement.ValueKind == JsonValueKind.Number)
            {
                var seconds = expiresInElement.GetInt32();
                return DateTimeOffset.UtcNow.AddSeconds(seconds);
            }

            // Try RFC 1123 date format (fixes Dart bug)
            var expiresInStr = expiresInElement.GetString();
            if (expiresInStr is not null &&
                DateTimeOffset.TryParseExact(
                    expiresInStr,
                    "R",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                return parsedDate;
            }
        }

        // Default to 1 hour if no expiry provided
        return DateTimeOffset.UtcNow.AddHours(1);
    }
}
