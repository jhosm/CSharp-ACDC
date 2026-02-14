using System.Net;

namespace CSharpAcdc.Exceptions;

/// <summary>
/// Base exception for all ACDC HTTP client errors. Provides URL redaction and response body truncation.
/// </summary>
public class AcdcException : HttpRequestException
{
    /// <summary>
    /// Gets the truncated response body, if available.
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Gets the redacted request URL, if available.
    /// </summary>
    public string? RequestUrl { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AcdcException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code, if applicable.</param>
    /// <param name="responseBody">The truncated response body.</param>
    /// <param name="requestUrl">The redacted request URL.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public AcdcException(
        string message,
        HttpStatusCode? statusCode = null,
        string? responseBody = null,
        string? requestUrl = null,
        Exception? innerException = null)
        : base(message, innerException, statusCode)
    {
        ResponseBody = responseBody;
        RequestUrl = requestUrl;
    }

    /// <summary>
    /// Returns a dictionary representation of the exception for structured logging.
    /// </summary>
    /// <returns>A dictionary containing the exception details.</returns>
    public virtual Dictionary<string, object?> ToMap() => new()
    {
        ["type"] = GetType().Name,
        ["message"] = Message,
        ["statusCode"] = StatusCode.HasValue ? (int)StatusCode.Value : null,
        ["requestUrl"] = RequestUrl,
        ["responseBody"] = ResponseBody,
        ["originalError"] = InnerException?.Message,
    };

    /// <summary>
    /// Redacts the path portion of a URL, preserving only the scheme and authority.
    /// </summary>
    /// <param name="url">The URL to redact.</param>
    /// <returns>The redacted URL, or the original value if empty or null.</returns>
    public static string? RedactUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "[redacted]";

        // If there's a non-empty path beyond "/", mask it
        var hasPath = uri.AbsolutePath.Length > 1;
        return hasPath
            ? $"{uri.Scheme}://{uri.Authority}/***"
            : $"{uri.Scheme}://{uri.Authority}";
    }

    /// <summary>
    /// Truncates a response body to the specified maximum length.
    /// </summary>
    /// <param name="body">The response body to truncate.</param>
    /// <param name="maxLength">The maximum length before truncation.</param>
    /// <returns>The original or truncated body with a "[truncated]" suffix.</returns>
    public static string? TruncateResponseBody(string? body, int maxLength = 500)
    {
        if (body is null || body.Length <= maxLength)
            return body;

        return string.Concat(body.AsSpan(0, maxLength), "[truncated]");
    }
}
