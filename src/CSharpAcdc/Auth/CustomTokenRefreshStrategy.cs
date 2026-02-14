namespace CSharpAcdc.Auth;

/// <summary>
/// Token refresh strategy that delegates to a user-provided function.
/// </summary>
public sealed class CustomTokenRefreshStrategy : ITokenRefreshStrategy
{
    private readonly Func<string, CancellationToken, Task<TokenRefreshResult>> _refreshFunc;

    /// <summary>
    /// Initializes a new instance of <see cref="CustomTokenRefreshStrategy"/>.
    /// </summary>
    /// <param name="refreshFunc">The function to invoke for token refresh.</param>
    public CustomTokenRefreshStrategy(
        Func<string, CancellationToken, Task<TokenRefreshResult>> refreshFunc)
    {
        _refreshFunc = refreshFunc ?? throw new ArgumentNullException(nameof(refreshFunc));
    }

    /// <inheritdoc />
    public Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken ct)
        => _refreshFunc(refreshToken, ct);
}
