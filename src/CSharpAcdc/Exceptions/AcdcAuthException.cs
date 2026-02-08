using System.Net;

namespace CSharpAcdc.Exceptions;

public class AcdcAuthException : AcdcException
{
    public AcdcAuthException(
        string message,
        HttpStatusCode? statusCode = null,
        string? responseBody = null,
        string? requestUrl = null,
        Exception? innerException = null)
        : base(message, statusCode, responseBody, requestUrl, innerException)
    {
    }

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
