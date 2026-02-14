using System.Net;

namespace CSharpAcdc.Exceptions;

/// <summary>
/// Exception thrown for authentication and authorization failures (HTTP 401, 403).
/// </summary>
public class AcdcAuthException : AcdcException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AcdcAuthException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="responseBody">The truncated response body.</param>
    /// <param name="requestUrl">The redacted request URL.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public AcdcAuthException(
        string message,
        HttpStatusCode? statusCode = null,
        string? responseBody = null,
        string? requestUrl = null,
        Exception? innerException = null)
        : base(message, statusCode, responseBody, requestUrl, innerException)
    {
    }

    /// <summary>
    /// Creates an <see cref="AcdcAuthException"/> with a message derived from the HTTP status code.
    /// </summary>
    /// <param name="statusCode">The HTTP status code (typically 401 or 403).</param>
    /// <param name="responseBody">The truncated response body.</param>
    /// <param name="requestUrl">The redacted request URL.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    /// <returns>A new <see cref="AcdcAuthException"/> with an appropriate message.</returns>
    public static AcdcAuthException FromStatusCode(
        HttpStatusCode statusCode,
        string? responseBody = null,
        string? requestUrl = null,
        Exception? innerException = null)
    {
        var message = statusCode switch
        {
            HttpStatusCode.Unauthorized => "Authentication failed: Invalid or expired token",
            HttpStatusCode.Forbidden => "Authorization failed: Insufficient permissions",
            _ => $"Authentication error: {(int)statusCode} {statusCode}",
        };

        return new AcdcAuthException(message, statusCode, responseBody, requestUrl, innerException);
    }
}
