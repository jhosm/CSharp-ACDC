namespace CSharpAcdc.Auth;

/// <summary>
/// Defines a strategy for refreshing expired OAuth tokens.
/// </summary>
public interface ITokenRefreshStrategy
{
    /// <summary>
    /// Refreshes the access token using the provided refresh token.
    /// </summary>
    /// <param name="refreshToken">The current refresh token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing new access token, refresh token, and expiration.</returns>
    Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken ct);
}
