using System.Diagnostics;
using CSharpAcdc.Configuration;
using CSharpAcdc.Extensions;
using CSharpAcdc.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSharpAcdc.Handlers;

/// <summary>
/// Logs HTTP request and response details with sensitive data redaction, slow-request warnings,
/// and large-payload warnings.
/// </summary>
public sealed class LoggingHandler : DelegatingHandler
{
    private static readonly AsyncLocal<bool> _isLogging = new();

    private readonly ILogger<LoggingHandler> _logger;
    private readonly AcdcLoggingOptions _options;
    private readonly SensitiveDataRedactor _redactor;

    /// <summary>
    /// Initializes a new instance of <see cref="LoggingHandler"/>.
    /// </summary>
    /// <param name="logger">The logger instance for structured output.</param>
    /// <param name="options">The logging configuration options.</param>
    public LoggingHandler(ILogger<LoggingHandler> logger, IOptions<AcdcLoggingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _options = options.Value;
        _redactor = new SensitiveDataRedactor(_options);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (ShouldSkip(request))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _isLogging.Value = true;
        try
        {
            return await SendWithLoggingAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _isLogging.Value = false;
        }
    }

    private async Task<HttpResponseMessage> SendWithLoggingAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Compute redacted URL once, before any try/catch, so it's available for error logging
        string redactedUrl;
        try
        {
            redactedUrl = _redactor.RedactUrl(request.RequestUri);
        }
        catch
        {
            redactedUrl = request.RequestUri?.AbsolutePath ?? "[unknown]";
        }

        try
        {
            var redactedHeaders = _redactor.RedactHeaders(request.Headers);

            _logger.LogInformation(
                "HTTP {Method} {Url} Headers: {Headers}",
                request.Method,
                redactedUrl,
                redactedHeaders);

            WarnIfLargeRequestBody(request, redactedUrl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to log request details");
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                ex,
                "HTTP {Method} {Url} cancelled after {ElapsedMs}ms",
                request.Method,
                redactedUrl,
                stopwatch.ElapsedMilliseconds);
            throw;
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

        try
        {
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
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to log response details");
        }

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
        var contentLength = response.Content?.Headers.ContentLength;
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
        if (_isLogging.Value)
            return true;

        if (request.Options.TryGetValue(AcdcRequestOptions.SkipLogging, out var skip) && skip)
            return true;

        return false;
    }
}
