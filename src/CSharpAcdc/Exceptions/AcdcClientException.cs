using System.Net;

namespace CSharpAcdc.Exceptions;

public class AcdcClientException : AcdcException
{
    public TimeSpan? RetryAfter { get; }

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

    public override Dictionary<string, object?> ToMap()
    {
        var map = base.ToMap();
        if (RetryAfter.HasValue)
            map["retryAfter"] = RetryAfter.Value.TotalSeconds;
        return map;
    }
}
