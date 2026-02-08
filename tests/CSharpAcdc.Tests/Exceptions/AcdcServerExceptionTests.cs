using System.Net;
using CSharpAcdc.Exceptions;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Exceptions;

public class AcdcServerExceptionTests
{
    [Fact]
    public void Constructor_500_SetsStatusCode()
    {
        var ex = new AcdcServerException(
            "Internal server error",
            HttpStatusCode.InternalServerError,
            "error body");

        ex.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.ResponseBody.Should().Be("error body");
    }

    [Fact]
    public void Constructor_503_SetsStatusCode()
    {
        var ex = new AcdcServerException(
            "Service unavailable",
            HttpStatusCode.ServiceUnavailable);

        ex.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public void IsAcdcException()
    {
        var ex = new AcdcServerException("test", HttpStatusCode.InternalServerError);
        (ex is AcdcException).Should().BeTrue();
        (ex is HttpRequestException).Should().BeTrue();
    }

    [Fact]
    public void ToMap_TypeIsAcdcServerException()
    {
        var ex = new AcdcServerException("error", HttpStatusCode.InternalServerError);
        var map = ex.ToMap();
        map["type"].Should().Be("AcdcServerException");
        map["statusCode"].Should().Be(500);
    }
}
