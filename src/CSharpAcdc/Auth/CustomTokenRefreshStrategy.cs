namespace CSharpAcdc.Auth;

public sealed class CustomTokenRefreshStrategy : ITokenRefreshStrategy
{
    private readonly Func<string, CancellationToken, Task<TokenRefreshResult>> _refreshFunc;

    public CustomTokenRefreshStrategy(
        Func<string, CancellationToken, Task<TokenRefreshResult>> refreshFunc)
    {
        _refreshFunc = refreshFunc ?? throw new ArgumentNullException(nameof(refreshFunc));
    }

    public Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken ct)
        => _refreshFunc(refreshToken, ct);
}
