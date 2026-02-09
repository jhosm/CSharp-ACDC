using System.Net;
using CSharpAcdc.Extensions;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Handlers;

public class DeduplicationHandlerTests
{
    [Fact]
    public async Task ConcurrentGets_SameUrl_DeduplicatedToOneRequest()
    {
        var callCount = 0;
        var requestStarted = new TaskCompletionSource();
        var allowResponse = new TaskCompletionSource();

        var innerHandler = new DelegateHandler(async (req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            requestStarted.TrySetResult();
            await allowResponse.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("response-body"),
            };
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var task1 = client.GetAsync("/data");
        await requestStarted.Task;

        var task2 = client.GetAsync("/data");
        var task3 = client.GetAsync("/data");

        allowResponse.SetResult();

        var responses = await Task.WhenAll(task1, task2, task3);

        callCount.Should().Be(1);
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Be("response-body");
        }
    }

    [Fact]
    public async Task Post_BypassesDeduplication()
    {
        var callCount = 0;

        var innerHandler = new DelegateHandler((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        await client.PostAsync("/data", new StringContent("body1"));
        await client.PostAsync("/data", new StringContent("body2"));

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Put_BypassesDeduplication()
    {
        var callCount = 0;

        var innerHandler = new DelegateHandler((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        await client.PutAsync("/data", new StringContent("body1"));
        await client.PutAsync("/data", new StringContent("body2"));

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Delete_BypassesDeduplication()
    {
        var callCount = 0;

        var innerHandler = new DelegateHandler((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        await client.DeleteAsync("/data");
        await client.DeleteAsync("/data");

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Head_IsDeduplicatable()
    {
        var callCount = 0;
        var requestStarted = new TaskCompletionSource();
        var allowResponse = new TaskCompletionSource();

        var innerHandler = new DelegateHandler(async (req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            requestStarted.TrySetResult();
            await allowResponse.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var request1 = new HttpRequestMessage(HttpMethod.Head, "/data");
        var request2 = new HttpRequestMessage(HttpMethod.Head, "/data");

        var task1 = client.SendAsync(request1);
        await requestStarted.Task;
        var task2 = client.SendAsync(request2);

        allowResponse.SetResult();
        await Task.WhenAll(task1, task2);

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task OptOut_SkipsDeduplication()
    {
        var callCount = 0;

        var innerHandler = new DelegateHandler((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            });
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "/data");
        request.Options.Set(AcdcRequestOptions.Deduplicate, false);
        await client.SendAsync(request);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/data");
        request2.Options.Set(AcdcRequestOptions.Deduplicate, false);
        await client.SendAsync(request2);

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task DifferentUrls_NotDeduplicated()
    {
        var callCount = 0;

        var innerHandler = new DelegateHandler((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            });
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        await client.GetAsync("/data1");
        await client.GetAsync("/data2");

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task DifferentHeaders_NotDeduplicated()
    {
        var callCount = 0;
        var requestStarted = new TaskCompletionSource();
        var allowResponse = new TaskCompletionSource();

        var innerHandler = new DelegateHandler(async (req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            requestStarted.TrySetResult();
            await allowResponse.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            };
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/data");
        request1.Headers.Add("Authorization", "Bearer token-a");

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/data");
        request2.Headers.Add("Authorization", "Bearer token-b");

        var task1 = client.SendAsync(request1);
        await requestStarted.Task;
        var task2 = client.SendAsync(request2);

        allowResponse.SetResult();
        await Task.WhenAll(task1, task2);

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ResponseCloning_ProducesIndependentResponses()
    {
        var innerHandler = new DelegateHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("shared-body"),
            }));

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var response1 = await client.GetAsync("/data");
        var response2 = await client.GetAsync("/data");

        var body1 = await response1.Content.ReadAsStringAsync();
        var body2 = await response2.Content.ReadAsStringAsync();

        body1.Should().Be("shared-body");
        body2.Should().Be("shared-body");

        // Disposing one should not affect the other
        response1.Dispose();
        var bodyAfterDispose = await response2.Content.ReadAsStringAsync();
        bodyAfterDispose.Should().Be("shared-body");
    }

    [Fact]
    public async Task CleanupAfterCompletion_AllowsNewWave()
    {
        var callCount = 0;

        var innerHandler = new DelegateHandler((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"response-{callCount}"),
            });
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        // First wave
        await client.GetAsync("/data");

        // Second wave (after cleanup) should make a new request
        await client.GetAsync("/data");

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task FailedRequest_PropagatesExceptionToAllSubscribers()
    {
        var requestStarted = new TaskCompletionSource();
        var allowResponse = new TaskCompletionSource();

        var innerHandler = new DelegateHandler(async (req, ct) =>
        {
            requestStarted.TrySetResult();
            await allowResponse.Task.WaitAsync(ct);
            throw new HttpRequestException("Server down");
        });

        var handler = new DeduplicationHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        var task1 = client.GetAsync("/data");
        await requestStarted.Task;
        var task2 = client.GetAsync("/data");

        allowResponse.SetResult();

        var act1 = () => task1;
        var act2 = () => task2;

        await act1.Should().ThrowAsync<HttpRequestException>();
        await act2.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void BuildDeduplicationKey_IsDeterministic()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request1.Headers.Add("X-Custom", "value");

        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request2.Headers.Add("X-Custom", "value");

        var key1 = DeduplicationHandler.BuildDeduplicationKey(request1);
        var key2 = DeduplicationHandler.BuildDeduplicationKey(request2);

        key1.Should().Be(key2);
    }

    [Fact]
    public void BuildDeduplicationKey_DifferentMethods_ProduceDifferentKeys()
    {
        var get = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var head = new HttpRequestMessage(HttpMethod.Head, "https://api.example.com/test");

        var key1 = DeduplicationHandler.BuildDeduplicationKey(get);
        var key2 = DeduplicationHandler.BuildDeduplicationKey(head);

        key1.Should().NotBe(key2);
    }

    /// <summary>
    /// A simple handler that forwards to a delegate for testing.
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
