using System.Net;

namespace CSharpAcdc.Exceptions;

/// <summary>
/// Exception thrown for HTTP 5xx server errors.
/// </summary>
public class AcdcServerException : AcdcException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AcdcServerException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="responseBody">The truncated response body.</param>
    /// <param name="requestUrl">The redacted request URL.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public AcdcServerException(
        string message,
        HttpStatusCode? statusCode = null,
        string? responseBody = null,
        string? requestUrl = null,
        Exception? innerException = null)
        : base(message, statusCode, responseBody, requestUrl, innerException)
    {
    }
}
