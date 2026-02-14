namespace CSharpAcdc.Auth;

/// <summary>
/// The result of a token refresh operation.
/// </summary>
/// <param name="AccessToken">The new access token.</param>
/// <param name="RefreshToken">The new or existing refresh token.</param>
/// <param name="ExpiresAt">The absolute expiration time of the access token.</param>
public sealed record TokenRefreshResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);
