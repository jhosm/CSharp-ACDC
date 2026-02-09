using System.Diagnostics;
using CSharpAcdc.Configuration;
using CSharpAcdc.Extensions;
using CSharpAcdc.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSharpAcdc.Handlers;

public class LoggingHandler : DelegatingHandler
{
    private static readonly AsyncLocal<bool> IsLogging = new();

    private readonly ILogger<LoggingHandler> _logger;
    private readonly AcdcLoggingOptions _options;
    private readonly SensitiveDataRedactor _redactor;

    public LoggingHandler(ILogger<LoggingHandler> logger, IOptions<AcdcLoggingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _redactor = new SensitiveDataRedactor(_options);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (ShouldSkip(request))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        IsLogging.Value = true;
        try
        {
            return await SendWithLoggingAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsLogging.Value = false;
        }
    }

    private async Task<HttpResponseMessage> SendWithLoggingAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var redactedUrl = _redactor.RedactUrl(request.RequestUri);
        var redactedHeaders = _redactor.RedactHeaders(request.Headers);

        _logger.LogInformation(
            "HTTP {Method} {Url} Headers: {Headers}",
            request.Method,
            redactedUrl,
            redactedHeaders);

        WarnIfLargeRequestBody(request, redactedUrl);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "HTTP {Method} {Url} failed after {ElapsedMs}ms",
                request.Method,
                redactedUrl,
                stopwatch.ElapsedMilliseconds);
            throw;
        }

        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        var redactedResponseHeaders = _redactor.RedactHeaders(response.Headers);

        _logger.LogInformation(
            "HTTP {Method} {Url} responded {StatusCode} in {ElapsedMs}ms Headers: {Headers}",
            request.Method,
            redactedUrl,
            (int)response.StatusCode,
            elapsedMs,
            redactedResponseHeaders);

        if (stopwatch.Elapsed >= _options.SlowRequestThreshold)
        {
            _logger.LogWarning(
                "Slow request: {Method} {Url} took {ElapsedMs}ms (threshold: {ThresholdMs}ms)",
                request.Method,
                redactedUrl,
                elapsedMs,
                (long)_options.SlowRequestThreshold.TotalMilliseconds);
        }

        WarnIfLargeResponseBody(response, request.Method, redactedUrl);

        return response;
    }

    private void WarnIfLargeRequestBody(HttpRequestMessage request, string redactedUrl)
    {
        var contentLength = request.Content?.Headers.ContentLength;
        if (contentLength > _options.LargePayloadThreshold)
        {
            _logger.LogWarning(
                "Large request body: {Method} {Url} has {ContentLength} bytes (threshold: {Threshold} bytes)",
                request.Method,
                redactedUrl,
                contentLength,
                _options.LargePayloadThreshold);
        }
    }

    private void WarnIfLargeResponseBody(HttpResponseMessage response, HttpMethod method, string redactedUrl)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > _options.LargePayloadThreshold)
        {
            _logger.LogWarning(
                "Large response body: {Method} {Url} has {ContentLength} bytes (threshold: {Threshold} bytes)",
                method,
                redactedUrl,
                contentLength,
                _options.LargePayloadThreshold);
        }
    }

    private bool ShouldSkip(HttpRequestMessage request)
    {
        if (IsLogging.Value)
            return true;

        if (request.Options.TryGetValue(AcdcRequestOptions.SkipLogging, out var skip) && skip)
            return true;

        return false;
    }
}
