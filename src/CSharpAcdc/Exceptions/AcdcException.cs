using System.Net;

namespace CSharpAcdc.Exceptions;

public class AcdcException : HttpRequestException
{
    public string? ResponseBody { get; }
    public string? RequestUrl { get; }

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

    public virtual Dictionary<string, object?> ToMap() => new()
    {
        ["type"] = GetType().Name,
        ["message"] = Message,
        ["statusCode"] = StatusCode.HasValue ? (int)StatusCode.Value : null,
        ["requestUrl"] = RequestUrl,
        ["responseBody"] = ResponseBody,
        ["originalError"] = InnerException?.Message,
    };

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

    public static string? TruncateResponseBody(string? body, int maxLength = 500)
    {
        if (body is null || body.Length <= maxLength)
            return body;

        return string.Concat(body.AsSpan(0, maxLength), "[truncated]");
    }
}
