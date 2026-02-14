namespace CSharpAcdc.Auth;

public interface ITokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct);
    Task<string?> GetRefreshTokenAsync(CancellationToken ct);
    Task SaveTokensAsync(string accessToken, string refreshToken, DateTimeOffset expiresAt, CancellationToken ct);
    Task ClearTokensAsync(CancellationToken ct);
    Task<DateTimeOffset?> GetTokenExpiryAsync(CancellationToken ct);
}
