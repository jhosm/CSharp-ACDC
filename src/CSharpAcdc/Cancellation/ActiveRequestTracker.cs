using System.Collections.Concurrent;

namespace CSharpAcdc.Cancellation;

/// <summary>
/// Tracks active HTTP requests and supports bulk cancellation via <see cref="CancelAll"/>.
/// </summary>
public sealed class ActiveRequestTracker
{
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _activeSources = new();

    /// <summary>
    /// Gets the number of currently tracked active requests.
    /// </summary>
    public int ActiveCount => _activeSources.Count;

    /// <summary>
    /// Begins tracking a request's cancellation token source.
    /// </summary>
    /// <param name="cts">The cancellation token source to track.</param>
    public void Track(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        _activeSources.TryAdd(cts, 0);
    }

    /// <summary>
    /// Stops tracking a request's cancellation token source.
    /// </summary>
    /// <param name="cts">The cancellation token source to untrack.</param>
    public void Untrack(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        _activeSources.TryRemove(cts, out _);
    }

    /// <summary>
    /// Cancels all currently tracked requests.
    /// </summary>
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
