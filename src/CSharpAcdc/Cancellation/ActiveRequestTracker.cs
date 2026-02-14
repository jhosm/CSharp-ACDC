using System.Collections.Concurrent;

namespace CSharpAcdc.Cancellation;

public sealed class ActiveRequestTracker
{
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _activeSources = new();

    public int ActiveCount => _activeSources.Count;

    public void Track(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        _activeSources.TryAdd(cts, 0);
    }

    public void Untrack(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        _activeSources.TryRemove(cts, out _);
    }

    public void CancelAll()
    {
        // Remove-then-cancel pattern: avoids the race where a CTS added between
        // enumeration and Clear() would be silently dropped without being canceled.
        while (!_activeSources.IsEmpty)
        {
            foreach (var cts in _activeSources.Keys)
            {
                if (!_activeSources.TryRemove(cts, out _))
                    continue;

                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // CTS may have been disposed between removal and cancel.
                }
            }
        }
    }
}
