using CSharpAcdc.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSharpAcdc.Auth;

public sealed class AcdcAuthManager
{
    private readonly ITokenProvider _tokenProvider;
    private readonly ITokenRefreshStrategy _refreshStrategy;
    private readonly BackoffManager _backoffManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AcdcAuthOptions _options;
    private readonly ILogger<AcdcAuthManager> _logger;
    private readonly UserIdExtractor _userIdExtractor;

    private volatile bool _logoutRequested;

    public AcdcAuthManager(
        ITokenProvider tokenProvider,
        ITokenRefreshStrategy refreshStrategy,
        BackoffManager backoffManager,
        IHttpClientFactory httpClientFactory,
        IOptions<AcdcAuthOptions> options,
        ILogger<AcdcAuthManager> logger,
        UserIdExtractor userIdExtractor)
    {
        _tokenProvider = tokenProvider;
        _refreshStrategy = refreshStrategy;
        _backoffManager = backoffManager;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _userIdExtractor = userIdExtractor;
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        _logoutRequested = true;

        try
        {
            // Capture refresh token BEFORE clearing — we need it for the revocation request.
            var refreshToken = await _tokenProvider.GetRefreshTokenAsync(ct).ConfigureAwait(false);

            // Revoke on the server first, then clear locally. This order ensures the
            // server invalidates the token even if the local clear fails.
            if (!string.IsNullOrEmpty(_options.RevocationEndpoint))
            {
                await TryRevokeTokenAsync(refreshToken, ct).ConfigureAwait(false);
            }

            await _tokenProvider.ClearTokensAsync(ct).ConfigureAwait(false);
            await _backoffManager.ResetAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("Logout completed successfully");
        }
        finally
        {
            _logoutRequested = false;
        }
    }

    public async Task ForceRefreshAsync(CancellationToken ct)
    {
        var refreshToken = await _tokenProvider.GetRefreshTokenAsync(ct).ConfigureAwait(false);
        if (refreshToken is null)
        {
            _logger.LogWarning("No refresh token available for force refresh");
            return;
        }

        if (_logoutRequested)
        {
            _logger.LogDebug("Force refresh cancelled — logout in progress");
            return;
        }

        var result = await _refreshStrategy.RefreshAsync(refreshToken, ct).ConfigureAwait(false);
        await _tokenProvider.SaveTokensAsync(
            result.AccessToken, result.RefreshToken, result.ExpiresAt, ct).ConfigureAwait(false);
        await _backoffManager.ResetAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Force token refresh completed, expires at {ExpiresAt}", result.ExpiresAt);
    }

    public string? GetUserId(HttpRequestMessage request) => _userIdExtractor.ExtractUserId(request);

    private async Task TryRevokeTokenAsync(string? refreshToken, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("acdc-auth");

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
            };

            // RFC 7009: include the token and type hint so the server can revoke it.
            if (refreshToken is not null)
            {
                parameters["token"] = refreshToken;
                parameters["token_type_hint"] = "refresh_token";
            }

            using var content = new FormUrlEncodedContent(parameters);
            await client.PostAsync(_options.RevocationEndpoint, content, ct).ConfigureAwait(false);
            _logger.LogDebug("Token revocation request sent");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token revocation failed");
        }
    }
}
