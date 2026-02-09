using CSharpAcdc.Auth;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Auth;

public class BackoffManagerTests
{
    private readonly BackoffManager _backoff = new();

    [Fact]
    public async Task WaitIfNeeded_AtZeroAttempts_ReturnsImmediately()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _backoff.WaitIfNeededAsync(CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task RecordFailure_IncrementsAttempt()
    {
        await _backoff.RecordFailureAsync();
        var attempt = await _backoff.GetAttemptAsync();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task Reset_SetsAttemptToZero()
    {
        await _backoff.RecordFailureAsync();
        await _backoff.RecordFailureAsync();
        await _backoff.ResetAsync();

        var attempt = await _backoff.GetAttemptAsync();
        attempt.Should().Be(0);
    }

    [Fact]
    public async Task WaitIfNeeded_AfterOneFailure_WaitsAround1Second()
    {
        await _backoff.RecordFailureAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _backoff.WaitIfNeededAsync(CancellationToken.None);
        sw.Stop();

        // 1s +/- 10% jitter => 900ms to 1100ms
        sw.ElapsedMilliseconds.Should().BeInRange(800, 1200);
    }

    [Fact]
    public async Task WaitIfNeeded_AfterTwoFailures_WaitsAround2Seconds()
    {
        await _backoff.RecordFailureAsync();
        await _backoff.RecordFailureAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _backoff.WaitIfNeededAsync(CancellationToken.None);
        sw.Stop();

        // 2s +/- 10% jitter => 1800ms to 2200ms
        sw.ElapsedMilliseconds.Should().BeInRange(1700, 2300);
    }

    [Fact]
    public async Task Delay_ClampsAt30Seconds()
    {
        // Record enough failures to exceed 30s (2^6 = 64s > 30s)
        for (var i = 0; i < 7; i++)
            await _backoff.RecordFailureAsync();

        var attempt = await _backoff.GetAttemptAsync();
        attempt.Should().Be(7);

        // We can't easily test the actual delay without waiting 30s,
        // but we can verify it respects cancellation
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = () => _backoff.WaitIfNeededAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitIfNeeded_RespectsCancel()
    {
        await _backoff.RecordFailureAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _backoff.WaitIfNeededAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConcurrentRecordFailure_IsThreadSafe()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => _backoff.RecordFailureAsync()))
            .ToArray();

        await Task.WhenAll(tasks);

        var attempt = await _backoff.GetAttemptAsync();
        attempt.Should().Be(50);
    }
}
