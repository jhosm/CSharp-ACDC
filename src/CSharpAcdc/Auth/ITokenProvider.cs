namespace CSharpAcdc.Auth;

/// <summary>
/// Provides thread-safe storage and retrieval of OAuth access and refresh tokens.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Gets the current access token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The access token, or <c>null</c> if no token is stored.</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken ct);

    /// <summary>
    /// Gets the current refresh token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refresh token, or <c>null</c> if no token is stored.</returns>
    Task<string?> GetRefreshTokenAsync(CancellationToken ct);

    /// <summary>
    /// Saves a new set of tokens.
    /// </summary>
    /// <param name="accessToken">The access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="expiresAt">The absolute expiration time of the access token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveTokensAsync(string accessToken, string refreshToken, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Clears all stored tokens.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ClearTokensAsync(CancellationToken ct);

    /// <summary>
    /// Gets the expiration time of the current access token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The expiration time, or <c>null</c> if no token is stored.</returns>
    Task<DateTimeOffset?> GetTokenExpiryAsync(CancellationToken ct);
}
