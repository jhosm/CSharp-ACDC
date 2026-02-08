namespace CSharpAcdc.Exceptions;

public enum NetworkErrorType
{
    ConnectionRefused,
    DnsResolutionFailed,
    Timeout,
    SslHandshakeFailed,
    ConnectionReset,
    Unknown,
}
