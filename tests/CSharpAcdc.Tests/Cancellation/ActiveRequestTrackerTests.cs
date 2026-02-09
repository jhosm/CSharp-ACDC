using CSharpAcdc.Cancellation;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Cancellation;

public class ActiveRequestTrackerTests
{
    [Fact]
    public void Track_IncrementsActiveCount()
    {
        var tracker = new ActiveRequestTracker();
        using var cts = new CancellationTokenSource();

        tracker.Track(cts);

        tracker.ActiveCount.Should().Be(1);
    }

    [Fact]
    public void Untrack_DecrementsActiveCount()
    {
        var tracker = new ActiveRequestTracker();
        using var cts = new CancellationTokenSource();

        tracker.Track(cts);
        tracker.Untrack(cts);

        tracker.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Untrack_UnknownSource_IsNoOp()
    {
        var tracker = new ActiveRequestTracker();
        using var cts = new CancellationTokenSource();

        tracker.Untrack(cts);

        tracker.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void CancelAll_CancelsAllTrackedSources()
    {
        var tracker = new ActiveRequestTracker();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        tracker.Track(cts1);
        tracker.Track(cts2);

        tracker.CancelAll();

        cts1.IsCancellationRequested.Should().BeTrue();
        cts2.IsCancellationRequested.Should().BeTrue();
        tracker.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void CancelAll_WithNoTrackedSources_DoesNotThrow()
    {
        var tracker = new ActiveRequestTracker();

        var act = () => tracker.CancelAll();

        act.Should().NotThrow();
    }

    [Fact]
    public void CancelAll_HandlesDisposedSources()
    {
        var tracker = new ActiveRequestTracker();
        var cts = new CancellationTokenSource();
        tracker.Track(cts);
        cts.Dispose();

        var act = () => tracker.CancelAll();

        act.Should().NotThrow();
    }

    [Fact]
    public void TrackMultiple_TracksAllIndependently()
    {
        var tracker = new ActiveRequestTracker();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        tracker.Track(cts1);
        tracker.Track(cts2);
        tracker.Track(cts3);

        tracker.ActiveCount.Should().Be(3);

        tracker.Untrack(cts2);
        tracker.ActiveCount.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentTrackAndUntrack_IsThreadSafe()
    {
        var tracker = new ActiveRequestTracker();
        const int iterations = 1000;

        var tasks = Enumerable.Range(0, iterations).Select(_ => Task.Run(() =>
        {
            var cts = new CancellationTokenSource();
            tracker.Track(cts);
            tracker.Untrack(cts);
            cts.Dispose();
        }));

        await Task.WhenAll(tasks);

        tracker.ActiveCount.Should().Be(0);
    }
}
