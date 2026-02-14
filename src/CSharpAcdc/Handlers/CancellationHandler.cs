using CSharpAcdc.Cancellation;

namespace CSharpAcdc.Handlers;

/// <summary>
/// Tracks active requests via <see cref="ActiveRequestTracker"/> to enable bulk cancellation.
/// </summary>
public sealed class CancellationHandler : DelegatingHandler
{
    private readonly ActiveRequestTracker _tracker;

    /// <summary>
    /// Initializes a new instance of <see cref="CancellationHandler"/>.
    /// </summary>
    /// <param name="tracker">The tracker used to register and cancel active requests.</param>
    public CancellationHandler(ActiveRequestTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _tracker.Track(linkedCts);
        try
        {
            return await base.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _tracker.Untrack(linkedCts);
        }
    }
}
