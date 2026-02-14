namespace CSharpAcdc.Auth;

public sealed class BackoffManager
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private int _attempt;

    public async Task WaitIfNeededAsync(CancellationToken ct)
    {
        var attempt = Volatile.Read(ref _attempt);
        if (attempt == 0)
            return;

        var rawMs = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var clampedMs = Math.Min(rawMs, MaxDelay.TotalMilliseconds);

        // +/-10% jitter
        var jitterFactor = 0.9 + (Random.Shared.NextDouble() * 0.2);
        var delay = TimeSpan.FromMilliseconds(clampedMs * jitterFactor);

        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    public Task RecordFailureAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _attempt);
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _attempt, 0);
        return Task.CompletedTask;
    }

    // Expose for testing
    internal Task<int> GetAttemptAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _attempt));
    }
}
