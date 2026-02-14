using System.Globalization;
using System.Net;
using System.Text.Json;
using CSharpAcdc.Configuration;
using CSharpAcdc.Exceptions;
using Microsoft.Extensions.Options;

namespace CSharpAcdc.Auth;

/// <summary>
/// Refreshes tokens using the OAuth 2.1 refresh_token grant type.
/// </summary>
public sealed class OAuthTokenRefreshStrategy : ITokenRefreshStrategy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AcdcAuthOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="OAuthTokenRefreshStrategy"/>.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating the HTTP client used for token requests.</param>
    /// <param name="options">Authentication configuration options.</param>
    public OAuthTokenRefreshStrategy(
        IHttpClientFactory httpClientFactory,
        IOptions<AcdcAuthOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <inheritdoc />
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

        return ParseSuccessResponse(responseBody, refreshToken);
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

    private static TokenRefreshResult ParseSuccessResponse(string responseBody, string inputRefreshToken)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Missing access_token in refresh response");

        // Many OAuth providers omit refresh_token when rotation is disabled —
        // fall back to the input token so the caller can keep using it.
        var newRefreshToken = root.TryGetProperty("refresh_token", out var rtElement)
            ? rtElement.GetString() ?? inputRefreshToken
            : inputRefreshToken;

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

            // String value — try numeric string first (e.g., "3600" from some providers),
            // then RFC 1123 date format (fixes Dart bug).
            var expiresInStr = expiresInElement.GetString();
            if (expiresInStr is not null)
            {
                if (int.TryParse(expiresInStr, NumberStyles.None, CultureInfo.InvariantCulture, out var numericSeconds))
                    return DateTimeOffset.UtcNow.AddSeconds(numericSeconds);

                if (DateTimeOffset.TryParseExact(
                        expiresInStr,
                        "R",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedDate))
                    return parsedDate;
            }
        }

        // Default to 1 hour if no expiry provided
        return DateTimeOffset.UtcNow.AddHours(1);
    }
}
