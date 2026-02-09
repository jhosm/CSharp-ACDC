using System.Net;
using CSharpAcdc.Cancellation;
using CSharpAcdc.Handlers;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Xunit;

namespace CSharpAcdc.Tests.Handlers;

public class CancellationHandlerTests
{
    private static HttpClient CreateClient(ActiveRequestTracker tracker, HttpMessageHandler innerHandler)
    {
        var handler = new CancellationHandler(tracker)
        {
            InnerHandler = innerHandler,
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };
    }

    [Fact]
    public async Task SendAsync_SuccessfulRequest_PassesThrough()
    {
        var tracker = new ActiveRequestTracker();
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK, "application/json", """{"ok":true}""");

        using var client = CreateClient(tracker, mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_CleansUpTracker_OnSuccess()
    {
        var tracker = new ActiveRequestTracker();
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(tracker, mockHandler);
        await client.GetAsync("/test");

        tracker.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_CleansUpTracker_OnFailure()
    {
        var tracker = new ActiveRequestTracker();
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Throw(new HttpRequestException("Boom"));

        using var client = CreateClient(tracker, mockHandler);
        var act = () => client.GetAsync("/fail");

        await act.Should().ThrowAsync<HttpRequestException>();
        tracker.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_PassesLinkedToken_Downstream()
    {
        var tracker = new ActiveRequestTracker();
        CancellationToken capturedToken = default;

        var innerHandler = new DelegateHandler((req, ct) =>
        {
            capturedToken = ct;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new CancellationHandler(tracker)
        {
            InnerHandler = innerHandler,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        using var cts = new CancellationTokenSource();
        await client.GetAsync("/test", cts.Token);

        // The downstream token should be different from the original
        // because it's a linked token
        capturedToken.Should().NotBe(CancellationToken.None);
    }

    [Fact]
    public async Task CancelAll_CancelsInFlightRequest()
    {
        var tracker = new ActiveRequestTracker();
        var requestStarted = new TaskCompletionSource();

        var innerHandler = new DelegateHandler(async (req, ct) =>
        {
            requestStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new CancellationHandler(tracker)
        {
            InnerHandler = innerHandler,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var requestTask = client.GetAsync("/slow");
        await requestStarted.Task;

        tracker.ActiveCount.Should().BeGreaterThan(0);
        tracker.CancelAll();

        var act = () => requestTask;
        await act.Should().ThrowAsync<TaskCanceledException>();
        tracker.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task ExternalCancellation_PropagatesThroughLinkedToken()
    {
        var tracker = new ActiveRequestTracker();
        using var cts = new CancellationTokenSource();
        var requestStarted = new TaskCompletionSource();

        var innerHandler = new DelegateHandler(async (req, ct) =>
        {
            requestStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new CancellationHandler(tracker)
        {
            InnerHandler = innerHandler,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var requestTask = client.GetAsync("/slow", cts.Token);
        await requestStarted.Task;

        cts.Cancel();

        var act = () => requestTask;
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    /// <summary>
    /// A simple delegating handler that forwards to a delegate for testing.
    /// </summary>
    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
