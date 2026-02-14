namespace CSharpAcdc.Auth;

public interface ITokenRefreshStrategy
{
    Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken ct);
}
