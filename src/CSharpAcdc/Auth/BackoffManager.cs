namespace CSharpAcdc.Auth;

public sealed class BackoffManager
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _attempt;

    public async Task WaitIfNeededAsync(CancellationToken ct)
    {
        TimeSpan delay;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_attempt == 0)
                return;

            var rawMs = BaseDelay.TotalMilliseconds * Math.Pow(2, _attempt - 1);
            var clampedMs = Math.Min(rawMs, MaxDelay.TotalMilliseconds);

            // +/-10% jitter
            var jitterFactor = 0.9 + (Random.Shared.NextDouble() * 0.2);
            delay = TimeSpan.FromMilliseconds(clampedMs * jitterFactor);
        }
        finally
        {
            _semaphore.Release();
        }

        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _attempt++;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _attempt = 0;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // Expose for testing
    internal async Task<int> GetAttemptAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _attempt;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
