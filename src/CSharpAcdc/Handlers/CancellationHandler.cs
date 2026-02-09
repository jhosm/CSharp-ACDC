using CSharpAcdc.Cancellation;

namespace CSharpAcdc.Handlers;

public class CancellationHandler : DelegatingHandler
{
    private readonly ActiveRequestTracker _tracker;

    public CancellationHandler(ActiveRequestTracker tracker)
    {
        _tracker = tracker;
    }

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
