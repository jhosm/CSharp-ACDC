using System.Net;

namespace CSharpAcdc.Exceptions;

public class AcdcServerException : AcdcException
{
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
