using System.Net;
using CSharpAcdc.Configuration;
using CSharpAcdc.Extensions;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using Xunit;

namespace CSharpAcdc.Tests.Handlers;

public class LoggingHandlerTests
{
    private readonly FakeLogger<LoggingHandler> _logger = new();
    private readonly AcdcLoggingOptions _options = new();

    private HttpClient CreateClient(MockHttpMessageHandler mockHandler, AcdcLoggingOptions? options = null)
    {
        var opts = Options.Create(options ?? _options);
        var loggingHandler = new LoggingHandler(_logger, opts)
        {
            InnerHandler = mockHandler,
        };
        return new HttpClient(loggingHandler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };
    }

    [Fact]
    public async Task RequestLogging_LogsMethodAndUrl()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        await client.GetAsync("/test");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("GET") &&
            e.Message.Contains("https://api.example.com/test"));
    }

    [Fact]
    public async Task ResponseLogging_LogsStatusCodeAndTiming()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        await client.GetAsync("/test");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("200") &&
            e.Message.Contains("ms"));
    }

    [Fact]
    public async Task SensitiveHeaders_AreRedacted()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("Authorization", "Bearer secret-token-123");
        request.Headers.Add("X-Request-Id", "abc-123");

        await client.SendAsync(request);

        var requestLog = _logger.Entries.First(e =>
            e.Level == LogLevel.Information && e.Message.Contains("GET"));

        requestLog.Message.Should().Contain("[REDACTED]");
        requestLog.Message.Should().NotContain("secret-token-123");
        requestLog.Message.Should().Contain("abc-123");
    }

    [Theory]
    [InlineData("Authorization", "Bearer tok")]
    [InlineData("Cookie", "session=abc")]
    [InlineData("Set-Cookie", "id=xyz")]
    [InlineData("X-Api-Key", "key123")]
    public void SensitiveHeaders_DefaultFields_Redacted(string headerName, string headerValue)
    {
        var redactor = new CSharpAcdc.Logging.SensitiveDataRedactor(new AcdcLoggingOptions());
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            [headerName] = [headerValue],
        };

        var result = redactor.RedactHeaders(headers);

        result[headerName].Should().Be("[REDACTED]");
    }

    [Fact]
    public async Task CustomSensitiveField_IsRedacted()
    {
        var options = new AcdcLoggingOptions
        {
            SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Authorization",
                "X-Custom-Secret",
            },
        };

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler, options);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Custom-Secret", "my-secret");

        await client.SendAsync(request);

        var requestLog = _logger.Entries.First(e =>
            e.Level == LogLevel.Information && e.Message.Contains("GET"));

        requestLog.Message.Should().NotContain("my-secret");
    }

    [Fact]
    public async Task QueryParameters_SensitiveOnesRedacted()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        await client.GetAsync("/search?q=hello&token=secret-value&page=1");

        var requestLog = _logger.Entries.First(e =>
            e.Level == LogLevel.Information && e.Message.Contains("GET"));

        requestLog.Message.Should().Contain("q=hello");
        requestLog.Message.Should().Contain("page=1");
        requestLog.Message.Should().Contain("[REDACTED]");
        requestLog.Message.Should().NotContain("secret-value");
    }

    [Fact]
    public async Task SlowRequest_LogsWarning()
    {
        var options = new AcdcLoggingOptions { SlowRequestThreshold = TimeSpan.FromMilliseconds(1) };

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(async _ =>
        {
            await Task.Delay(50);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(mockHandler, options);
        await client.GetAsync("/slow");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Slow request"));
    }

    [Fact]
    public async Task FastRequest_DoesNotLogSlowWarning()
    {
        var options = new AcdcLoggingOptions { SlowRequestThreshold = TimeSpan.FromSeconds(60) };

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler, options);
        await client.GetAsync("/fast");

        _logger.Entries.Should().NotContain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Slow request"));
    }

    [Fact]
    public async Task LargeRequestBody_LogsWarning()
    {
        var options = new AcdcLoggingOptions { LargePayloadThreshold = 100 };

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler, options);
        using var content = new StringContent(new string('x', 200));
        await client.PostAsync("/upload", content);

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Large request body"));
    }

    [Fact]
    public async Task LargeResponseBody_LogsWarning()
    {
        var options = new AcdcLoggingOptions { LargePayloadThreshold = 100 };
        var largeBody = new string('x', 200);

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK, "text/plain", largeBody);

        using var client = CreateClient(mockHandler, options);
        await client.GetAsync("/large");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Large response body"));
    }

    [Fact]
    public async Task SmallPayload_DoesNotLogLargePayloadWarning()
    {
        var options = new AcdcLoggingOptions { LargePayloadThreshold = 1_048_576 };

        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK, "text/plain", "small");

        using var client = CreateClient(mockHandler, options);
        await client.GetAsync("/small");

        _logger.Entries.Should().NotContain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Large"));
    }

    [Fact]
    public async Task ReentrancyPrevention_NestedRequestSkipsLogging()
    {
        var nestedHandler = new NestedRequestHandler();
        var opts = Options.Create(_options);
        var loggingHandler = new LoggingHandler(_logger, opts)
        {
            InnerHandler = nestedHandler,
        };
        using var client = new HttpClient(loggingHandler)
        {
            BaseAddress = new Uri("https://api.example.com"),
        };

        await client.GetAsync("/outer");

        // Only the outer request should be logged, not the nested one
        var infoLogs = _logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("GET"))
            .ToList();

        infoLogs.Should().AllSatisfy(e => e.Message.Should().Contain("/outer"));
        infoLogs.Should().NotContain(e => e.Message.Contains("/inner"));

        // The nested handler's inner logger should also have no entries,
        // confirming the reentrancy guard suppressed logging entirely
        nestedHandler.InnerLogger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SkipLogging_OptionSet_NoLogsEmitted()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/silent");
        request.Options.Set(AcdcRequestOptions.SkipLogging, true);

        await client.SendAsync(request);

        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ErrorLogging_LogsErrorAndRethrows()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Throw(new HttpRequestException("Connection refused"));

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/failing");

        await act.Should().ThrowAsync<HttpRequestException>();

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("failed") &&
            e.Message.Contains("ms"));
    }

    [Fact]
    public async Task ErrorLogging_ContainsRedactedUrl()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Throw(new HttpRequestException("Connection refused"));

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/fail?token=secret");

        await act.Should().ThrowAsync<HttpRequestException>();

        var errorLog = _logger.Entries.First(e => e.Level == LogLevel.Error);
        errorLog.Message.Should().Contain("[REDACTED]");
        errorLog.Message.Should().NotContain("secret");
    }

    [Fact]
    public async Task CancellationLogging_LogsAtInformationLevel()
    {
        using var cts = new CancellationTokenSource();
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(async _ =>
        {
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(mockHandler);
        var act = () => client.GetAsync("/cancel", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        _logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("cancelled"));
    }

    [Fact]
    public async Task ResponseHeaders_SensitiveOnesRedacted()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("X-Request-Id", "visible-id");
            response.Headers.Add("X-Api-Key", "secret-api-key");
            return response;
        });

        using var client = CreateClient(mockHandler);
        await client.GetAsync("/test");

        var responseLogs = _logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("200"))
            .ToList();

        responseLogs.Should().ContainSingle();
        responseLogs[0].Message.Should().Contain("visible-id");
        responseLogs[0].Message.Should().Contain("[REDACTED]");
        responseLogs[0].Message.Should().NotContain("secret-api-key");
    }

    [Fact]
    public async Task AsyncLocal_ResetsAfterError()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("/fail").Throw(new HttpRequestException("Boom"));
        mockHandler.When("/success").Respond(HttpStatusCode.OK);

        using var client = CreateClient(mockHandler);

        // First request fails
        var act = () => client.GetAsync("/fail");
        await act.Should().ThrowAsync<HttpRequestException>();

        // Second request should still be logged (AsyncLocal reset to false)
        await client.GetAsync("/success");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("/success"));
    }

    [Fact]
    public async Task ConcurrentRequests_LogIndependently()
    {
        using var mockHandler = new MockHttpMessageHandler();
        mockHandler.When("*").Respond(async _ =>
        {
            await Task.Delay(10);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(mockHandler);

        var tasks = Enumerable.Range(0, 5)
            .Select(i => client.GetAsync($"/concurrent-{i}"))
            .ToList();

        await Task.WhenAll(tasks);

        // Each request should produce at least a request and response log
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            _logger.Entries.Should().Contain(e =>
                e.Level == LogLevel.Information &&
                e.Message.Contains($"/concurrent-{index}"));
        }
    }

    /// <summary>
    /// A handler that simulates a nested HTTP call within the same async context,
    /// testing the AsyncLocal reentrancy guard.
    /// </summary>
    private sealed class NestedRequestHandler : HttpMessageHandler
    {
        public FakeLogger<LoggingHandler> InnerLogger { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var innerMock = new MockHttpMessageHandler();
            innerMock.When("*").Respond(HttpStatusCode.OK);

            var innerOpts = Options.Create(new AcdcLoggingOptions());
            var innerLogging = new LoggingHandler(InnerLogger, innerOpts)
            {
                InnerHandler = innerMock,
            };

            using var innerClient = new HttpClient(innerLogging)
            {
                BaseAddress = new Uri("https://api.example.com"),
            };

            // Use await instead of .GetAwaiter().GetResult() to avoid deadlocks
            await innerClient.GetAsync("/inner", cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

/// <summary>
/// A simple fake logger that captures log entries for assertion.
/// </summary>
public sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];
    public IReadOnlyList<LogEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}

public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
