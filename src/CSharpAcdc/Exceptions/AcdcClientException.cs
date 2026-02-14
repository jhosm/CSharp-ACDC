using System.Net;

namespace CSharpAcdc.Exceptions;

/// <summary>
/// Exception thrown for HTTP 4xx client errors, with optional Retry-After support.
/// </summary>
public class AcdcClientException : AcdcException
{
    /// <summary>
    /// Gets the suggested retry delay from the Retry-After response header, if present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AcdcClientException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="responseBody">The truncated response body.</param>
    /// <param name="requestUrl">The redacted request URL.</param>
    /// <param name="retryAfter">The suggested retry delay from the Retry-After header.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public AcdcClientException(
        string message,
        HttpStatusCode? statusCode = null,
        string? responseBody = null,
        string? requestUrl = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, statusCode, responseBody, requestUrl, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <inheritdoc />
    public override Dictionary<string, object?> ToMap()
    {
        var map = base.ToMap();
        if (RetryAfter.HasValue)
            map["retryAfter"] = RetryAfter.Value.TotalSeconds;
        return map;
    }
}
