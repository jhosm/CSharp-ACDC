using System.Net;
using System.Net.Http.Headers;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Handlers;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Xunit;

namespace CSharpAcdc.Tests.Handlers;

public class ErrorHandlerTests
{
    private static HttpClient CreateClient(MockHttpMessageHandler mockHandler)
    {
        var errorHandler = new ErrorHandler
        {
            InnerHandler = mockHandler,
        };
        return new HttpClient(errorHandler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };
    }

    [Fact]
    public async Task SuccessfulResponse_PassesThrough()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK, "application/json", """{"ok":true}""");

        using var client = CreateClient(mockHandler);
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Status401_ThrowsAcdcAuthException()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.Unauthorized, "text/plain", "Unauthorized");

        using var client = CreateClient(mockHandler);

        var act = () => client.GetAsync("/users/123?token=secret");

        var ex = await act.Should().ThrowAsync<AcdcAuthException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.Which.Message.Should().Contain("Authentication failed");
        ex.Which.ResponseBody.Should().Be("Unauthorized");
        ex.Which.RequestUrl.Should().NotContain("token=secret");
        ex.Which.RequestUrl.Should().Be("https://api.example.com/***");
    }

    [Fact]
    public async Task Status403_ThrowsAcdcAuthException()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.Forbidden, "text/plain", "Forbidden");

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/admin");

        var ex = await act.Should().ThrowAsync<AcdcAuthException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ex.Which.Message.Should().Contain("Authorization failed");
    }

    [Fact]
    public async Task Status404_ThrowsAcdcClientException()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.NotFound, "text/plain", "Not Found");

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/missing");

        var ex = await act.Should().ThrowAsync<AcdcClientException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task Status429_WithRetryAfter_ThrowsAcdcClientException()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("Rate limited"),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return response;
        });

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/rate-limited");

        var ex = await act.Should().ThrowAsync<AcdcClientException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Status500_ThrowsAcdcServerException()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.InternalServerError, "text/plain", "Server Error");

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/failing");

        var ex = await act.Should().ThrowAsync<AcdcServerException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.Which.ResponseBody.Should().Be("Server Error");
    }

    [Fact]
    public async Task Status503_ThrowsAcdcServerException()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.ServiceUnavailable, "text/plain", "Unavailable");

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/unavailable");

        var ex = await act.Should().ThrowAsync<AcdcServerException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task NetworkError_ThrowsAcdcNetworkException()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Throw(new HttpRequestException(
            HttpRequestError.NameResolutionError,
            "Name resolution failed"));

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/dns-fail");

        var ex = await act.Should().ThrowAsync<AcdcNetworkException>();
        ex.Which.NetworkErrorType.Should().Be(NetworkErrorType.DnsResolutionFailed);
        ex.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Timeout_NotUserCancelled_ThrowsNetworkExceptionTimeout()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Throw(new TaskCanceledException("The operation timed out"));

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/slow");

        var ex = await act.Should().ThrowAsync<AcdcNetworkException>();
        ex.Which.NetworkErrorType.Should().Be(NetworkErrorType.Timeout);
    }

    [Fact]
    public async Task UserCancellation_RethrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Throw(new OperationCanceledException(cts.Token));

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/cancelled", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExistingAcdcException_Passthrough()
    {
        var original = AcdcAuthException.FromStatusCode(HttpStatusCode.Unauthorized, "body", "url");

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Throw(original);

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/auth-fail");

        var ex = await act.Should().ThrowAsync<AcdcAuthException>();
        ex.Which.Should().BeSameAs(original);
    }

    [Fact]
    public async Task LargeResponseBody_IsTruncated()
    {
        var largeBody = new string('x', 1000);

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(
            HttpStatusCode.InternalServerError,
            "text/plain",
            largeBody);

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/large-error");

        var ex = await act.Should().ThrowAsync<AcdcServerException>();
        ex.Which.ResponseBody.Should().HaveLength(500 + "[truncated]".Length);
        ex.Which.ResponseBody.Should().EndWith("[truncated]");
    }

    [Fact]
    public async Task RequestUrl_IsRedacted()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.NotFound, "text/plain", "nope");

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/users/12345?apikey=secret");

        var ex = await act.Should().ThrowAsync<AcdcClientException>();
        ex.Which.RequestUrl.Should().Be("https://api.example.com/***");
        ex.Which.RequestUrl.Should().NotContain("apikey");
        ex.Which.RequestUrl.Should().NotContain("12345");
    }

    [Fact]
    public async Task RequestHeaders_NotModified()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*")
            .With(req =>
            {
                // Verify original header is present
                return req.Headers.Contains("X-Custom");
            })
            .Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Custom", "value");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
