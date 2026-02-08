using System.Net;
using CSharpAcdc.Exceptions;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Exceptions;

public class AcdcAuthExceptionTests
{
    [Fact]
    public void FromStatusCode_401_HasAuthenticationFailedMessage()
    {
        var ex = AcdcAuthException.FromStatusCode(HttpStatusCode.Unauthorized, "body", "url");

        ex.Message.Should().Contain("Authentication failed");
        ex.Message.Should().Contain("Invalid or expired token");
        ex.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.ResponseBody.Should().Be("body");
        ex.RequestUrl.Should().Be("url");
    }

    [Fact]
    public void FromStatusCode_403_HasAuthorizationFailedMessage()
    {
        var ex = AcdcAuthException.FromStatusCode(HttpStatusCode.Forbidden);

        ex.Message.Should().Contain("Authorization failed");
        ex.Message.Should().Contain("Insufficient permissions");
        ex.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void FromStatusCode_PreservesInnerException()
    {
        var inner = new Exception("inner");
        var ex = AcdcAuthException.FromStatusCode(HttpStatusCode.Unauthorized, innerException: inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void CustomMessage_OverridesDefaultMessage()
    {
        var ex = new AcdcAuthException("Custom auth error", HttpStatusCode.Unauthorized);

        ex.Message.Should().Be("Custom auth error");
        ex.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void IsAcdcException()
    {
        var ex = AcdcAuthException.FromStatusCode(HttpStatusCode.Unauthorized);
        (ex is AcdcException).Should().BeTrue();
        (ex is HttpRequestException).Should().BeTrue();
    }

    [Fact]
    public void ToMap_TypeIsAcdcAuthException()
    {
        var ex = AcdcAuthException.FromStatusCode(HttpStatusCode.Unauthorized);
        var map = ex.ToMap();
        map["type"].Should().Be("AcdcAuthException");
    }
}
