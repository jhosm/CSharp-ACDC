using System.Net;
using CSharpAcdc.Exceptions;

namespace CSharpAcdc.Handlers;

/// <summary>
/// Converts non-success HTTP responses and transport exceptions into typed ACDC exceptions.
/// </summary>
public class ErrorHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex is AcdcException)
        {
            throw; // passthrough already-typed ACDC exceptions
        }
        catch (HttpRequestException ex)
        {
            var errorType = AcdcNetworkException.MapFromHttpRequestError(ex.HttpRequestError);
            var redactedUrl = AcdcException.RedactUrl(request.RequestUri?.ToString());
            throw new AcdcNetworkException(
                ex.Message,
                errorType,
                requestUrl: redactedUrl,
                innerException: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var redactedUrl = AcdcException.RedactUrl(request.RequestUri?.ToString());
            throw new AcdcNetworkException(
                "The request timed out",
                NetworkErrorType.Timeout,
                requestUrl: redactedUrl,
                innerException: ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        var statusCode = response.StatusCode;
        string? body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            body = null;
        }

        var truncatedBody = AcdcException.TruncateResponseBody(body);
        var requestUrl = AcdcException.RedactUrl(request.RequestUri?.ToString());

        throw statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                AcdcAuthException.FromStatusCode(statusCode, truncatedBody, requestUrl),

            HttpStatusCode.Forbidden =>
                AcdcAuthException.FromStatusCode(statusCode, truncatedBody, requestUrl),

            >= HttpStatusCode.InternalServerError =>
                new AcdcServerException(
                    $"Server error: {(int)statusCode} {statusCode}",
                    statusCode,
                    truncatedBody,
                    requestUrl),

            >= HttpStatusCode.BadRequest =>
                new AcdcClientException(
                    $"Client error: {(int)statusCode} {statusCode}",
                    statusCode,
                    truncatedBody,
                    requestUrl,
                    ParseRetryAfter(response)),

            _ =>
                new AcdcException(
                    $"Unexpected error: {(int)statusCode} {statusCode}",
                    statusCode,
                    truncatedBody,
                    requestUrl),
        };
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;

        if (retryAfter.Delta.HasValue)
            return retryAfter.Delta.Value;

        if (retryAfter.Date.HasValue)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
