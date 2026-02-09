using System.Collections.Concurrent;

namespace CSharpAcdc.Cancellation;

public sealed class ActiveRequestTracker
{
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _activeSources = new();

    public int ActiveCount => _activeSources.Count;

    public void Track(CancellationTokenSource cts)
    {
        _activeSources.TryAdd(cts, 0);
    }

    public void Untrack(CancellationTokenSource cts)
    {
        _activeSources.TryRemove(cts, out _);
    }

    public void CancelAll()
    {
        foreach (var cts in _activeSources.Keys)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS may have been disposed between enumeration and cancel
            }
        }

        _activeSources.Clear();
    }
}
