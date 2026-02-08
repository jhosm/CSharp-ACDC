using System.Net.Http;
using CSharpAcdc.Exceptions;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Exceptions;

public class AcdcNetworkExceptionTests
{
    [Fact]
    public void Constructor_SetsNetworkErrorType()
    {
        var ex = new AcdcNetworkException("timeout", NetworkErrorType.Timeout);

        ex.NetworkErrorType.Should().Be(NetworkErrorType.Timeout);
        ex.Message.Should().Be("timeout");
    }

    [Fact]
    public void Constructor_PreservesRequestUrl()
    {
        var ex = new AcdcNetworkException(
            "failed",
            NetworkErrorType.ConnectionRefused,
            requestUrl: "https://api.example.com/***");

        ex.RequestUrl.Should().Be("https://api.example.com/***");
    }

    [Fact]
    public void Constructor_PreservesInnerException()
    {
        var inner = new Exception("inner");
        var ex = new AcdcNetworkException("failed", NetworkErrorType.Unknown, innerException: inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, NetworkErrorType.DnsResolutionFailed)]
    [InlineData(HttpRequestError.ConnectionError, NetworkErrorType.ConnectionRefused)]
    [InlineData(HttpRequestError.SecureConnectionError, NetworkErrorType.SslHandshakeFailed)]
    [InlineData(HttpRequestError.HttpProtocolError, NetworkErrorType.Unknown)]
    [InlineData(HttpRequestError.Unknown, NetworkErrorType.Unknown)]
    public void MapFromHttpRequestError_MapsCorrectly(
        HttpRequestError input,
        NetworkErrorType expected)
    {
        AcdcNetworkException.MapFromHttpRequestError(input).Should().Be(expected);
    }

    [Fact]
    public void ToMap_IncludesNetworkErrorType()
    {
        var ex = new AcdcNetworkException("dns", NetworkErrorType.DnsResolutionFailed);

        var map = ex.ToMap();

        map.Should().ContainKey("networkErrorType")
            .WhoseValue.Should().Be("DnsResolutionFailed");
        map["type"].Should().Be("AcdcNetworkException");
    }

    [Fact]
    public void StatusCode_IsNull()
    {
        var ex = new AcdcNetworkException("test", NetworkErrorType.Timeout);
        ex.StatusCode.Should().BeNull();
    }

    [Fact]
    public void IsAcdcException()
    {
        var ex = new AcdcNetworkException("test", NetworkErrorType.Timeout);
        (ex is AcdcException).Should().BeTrue();
    }
}
