namespace CSharpAcdc.Auth;

public sealed class InMemoryTokenProvider : ITokenProvider
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset? _expiresAt;

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _accessToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string?> GetRefreshTokenAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _refreshToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveTokensAsync(
        string accessToken,
        string refreshToken,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _accessToken = accessToken;
            _refreshToken = refreshToken;
            _expiresAt = expiresAt;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ClearTokensAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _accessToken = null;
            _refreshToken = null;
            _expiresAt = null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<DateTimeOffset?> GetTokenExpiryAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _expiresAt;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
