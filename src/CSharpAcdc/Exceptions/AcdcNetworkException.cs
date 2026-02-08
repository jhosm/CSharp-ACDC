using System.Net;
using System.Net.Http;

namespace CSharpAcdc.Exceptions;

public class AcdcNetworkException : AcdcException
{
    public NetworkErrorType NetworkErrorType { get; }

    public AcdcNetworkException(
        string message,
        NetworkErrorType networkErrorType,
        string? requestUrl = null,
        Exception? innerException = null)
        : base(message, statusCode: null, responseBody: null, requestUrl: requestUrl, innerException: innerException)
    {
        NetworkErrorType = networkErrorType;
    }

    public static NetworkErrorType MapFromHttpRequestError(HttpRequestError error) => error switch
    {
        HttpRequestError.NameResolutionError => NetworkErrorType.DnsResolutionFailed,
        HttpRequestError.ConnectionError => NetworkErrorType.ConnectionRefused,
        HttpRequestError.SecureConnectionError => NetworkErrorType.SslHandshakeFailed,
        _ => NetworkErrorType.Unknown,
    };

    public override Dictionary<string, object?> ToMap()
    {
        var map = base.ToMap();
        map["networkErrorType"] = NetworkErrorType.ToString();
        return map;
    }
}
