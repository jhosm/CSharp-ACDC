namespace CSharpAcdc.Exceptions;

/// <summary>
/// Classifies the type of network error encountered during an HTTP request.
/// </summary>
public enum NetworkErrorType
{
    /// <summary>
    /// The remote host actively refused the connection.
    /// </summary>
    ConnectionRefused,

    /// <summary>
    /// DNS resolution failed for the target host.
    /// </summary>
    DnsResolutionFailed,

    /// <summary>
    /// The request exceeded the configured timeout.
    /// </summary>
    Timeout,

    /// <summary>
    /// The TLS/SSL handshake failed.
    /// </summary>
    SslHandshakeFailed,

    /// <summary>
    /// The connection was reset by the remote host.
    /// </summary>
    ConnectionReset,

    /// <summary>
    /// An unclassified network error occurred.
    /// </summary>
    Unknown,
}
