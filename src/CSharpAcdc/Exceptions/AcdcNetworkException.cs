using System.Net;
using System.Net.Http;

namespace CSharpAcdc.Exceptions;

/// <summary>
/// Exception thrown for network-level errors such as timeouts, DNS failures, and connection refusals.
/// </summary>
public class AcdcNetworkException : AcdcException
{
    /// <summary>
    /// Gets the type of network error that occurred.
    /// </summary>
    public NetworkErrorType NetworkErrorType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AcdcNetworkException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="networkErrorType">The type of network error.</param>
    /// <param name="requestUrl">The redacted request URL.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public AcdcNetworkException(
        string message,
        NetworkErrorType networkErrorType,
        string? requestUrl = null,
        Exception? innerException = null)
        : base(message, statusCode: null, responseBody: null, requestUrl: requestUrl, innerException: innerException)
    {
        NetworkErrorType = networkErrorType;
    }

    /// <summary>
    /// Maps an <see cref="HttpRequestError"/> to the corresponding <see cref="NetworkErrorType"/>.
    /// </summary>
    /// <param name="error">The HTTP request error to map.</param>
    /// <returns>The corresponding <see cref="NetworkErrorType"/>.</returns>
    public static NetworkErrorType MapFromHttpRequestError(HttpRequestError error) => error switch
    {
        HttpRequestError.NameResolutionError => NetworkErrorType.DnsResolutionFailed,
        HttpRequestError.ConnectionError => NetworkErrorType.ConnectionRefused,
        HttpRequestError.SecureConnectionError => NetworkErrorType.SslHandshakeFailed,
        _ => NetworkErrorType.Unknown,
    };

    /// <inheritdoc />
    public override Dictionary<string, object?> ToMap()
    {
        var map = base.ToMap();
        map["networkErrorType"] = NetworkErrorType.ToString();
        return map;
    }
}
