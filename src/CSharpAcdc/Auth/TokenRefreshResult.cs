namespace CSharpAcdc.Auth;

public sealed record TokenRefreshResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);
