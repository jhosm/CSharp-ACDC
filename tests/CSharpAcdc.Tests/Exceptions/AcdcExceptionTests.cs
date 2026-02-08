using System.Net;
using CSharpAcdc.Exceptions;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Exceptions;

public class AcdcExceptionTests
{
    [Fact]
    public void Constructor_WithAllProperties_SetsPropertiesCorrectly()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new AcdcException(
            "Test error",
            HttpStatusCode.InternalServerError,
            "error details",
            "https://api.example.com/***",
            inner);

        ex.Message.Should().Be("Test error");
        ex.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.ResponseBody.Should().Be("error details");
        ex.RequestUrl.Should().Be("https://api.example.com/***");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Constructor_WithMessageOnly_LeavesOptionalPropertiesNull()
    {
        var ex = new AcdcException("Test error");

        ex.Message.Should().Be("Test error");
        ex.StatusCode.Should().BeNull();
        ex.ResponseBody.Should().BeNull();
        ex.RequestUrl.Should().BeNull();
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void ToMap_ReturnsAllKeys()
    {
        var inner = new Exception("original");
        var ex = new AcdcException(
            "Test error",
            HttpStatusCode.BadRequest,
            "body",
            "https://api.example.com/***",
            inner);

        var map = ex.ToMap();

        map.Should().ContainKey("type").WhoseValue.Should().Be("AcdcException");
        map.Should().ContainKey("message").WhoseValue.Should().Be("Test error");
        map.Should().ContainKey("statusCode").WhoseValue.Should().Be(400);
        map.Should().ContainKey("requestUrl").WhoseValue.Should().Be("https://api.example.com/***");
        map.Should().ContainKey("responseBody").WhoseValue.Should().Be("body");
        map.Should().ContainKey("originalError").WhoseValue.Should().Be("original");
    }

    [Fact]
    public void ToMap_WithNullFields_ContainsNullValues()
    {
        var ex = new AcdcException("Test");

        var map = ex.ToMap();

        map["statusCode"].Should().BeNull();
        map["requestUrl"].Should().BeNull();
        map["responseBody"].Should().BeNull();
        map["originalError"].Should().BeNull();
    }

    [Fact]
    public void ToMap_TypeReflectsRuntimeType()
    {
        var ex = new AcdcAuthException("auth error", HttpStatusCode.Unauthorized);
        var map = ex.ToMap();

        map["type"].Should().Be("AcdcAuthException");
    }

    [Fact]
    public void IsHttpRequestException()
    {
        var ex = new AcdcException("test");
        (ex is HttpRequestException).Should().BeTrue();
    }

    // --- RedactUrl tests ---

    [Fact]
    public void RedactUrl_WithQueryParams_StripsQueryAndMasksPath()
    {
        var result = AcdcException.RedactUrl("https://api.example.com/users/12345/orders?token=abc&page=1");
        result.Should().Be("https://api.example.com/***");
    }

    [Fact]
    public void RedactUrl_WithoutQueryParams_MasksPath()
    {
        var result = AcdcException.RedactUrl("https://api.example.com/users/12345");
        result.Should().Be("https://api.example.com/***");
    }

    [Fact]
    public void RedactUrl_DomainOnly_ReturnsUnchanged()
    {
        var result = AcdcException.RedactUrl("https://api.example.com");
        result.Should().Be("https://api.example.com");
    }

    [Fact]
    public void RedactUrl_DomainWithTrailingSlash_MasksPath()
    {
        // URI.AbsolutePath for "https://api.example.com/" is "/"
        var result = AcdcException.RedactUrl("https://api.example.com/");
        result.Should().Be("https://api.example.com");
    }

    [Fact]
    public void RedactUrl_MalformedUrl_ReturnsSafeFallback()
    {
        var result = AcdcException.RedactUrl("not a url");
        result.Should().Be("[redacted]");
    }

    [Fact]
    public void RedactUrl_Null_ReturnsNull()
    {
        var result = AcdcException.RedactUrl(null);
        result.Should().BeNull();
    }

    [Fact]
    public void RedactUrl_Empty_ReturnsEmpty()
    {
        var result = AcdcException.RedactUrl("");
        result.Should().BeEmpty();
    }

    // --- TruncateResponseBody tests ---

    [Fact]
    public void TruncateResponseBody_Null_ReturnsNull()
    {
        AcdcException.TruncateResponseBody(null).Should().BeNull();
    }

    [Fact]
    public void TruncateResponseBody_Empty_ReturnsEmpty()
    {
        AcdcException.TruncateResponseBody("").Should().BeEmpty();
    }

    [Fact]
    public void TruncateResponseBody_UnderLimit_ReturnsUnchanged()
    {
        AcdcException.TruncateResponseBody("short body").Should().Be("short body");
    }

    [Fact]
    public void TruncateResponseBody_AtLimit_ReturnsUnchanged()
    {
        var body = new string('x', 500);
        AcdcException.TruncateResponseBody(body).Should().Be(body);
    }

    [Fact]
    public void TruncateResponseBody_OverLimit_TruncatesWithSuffix()
    {
        var body = new string('x', 600);
        var result = AcdcException.TruncateResponseBody(body);
        result.Should().HaveLength(500 + "[truncated]".Length);
        result.Should().EndWith("[truncated]");
        result.Should().StartWith(new string('x', 500));
    }

    [Fact]
    public void TruncateResponseBody_CustomMaxLength()
    {
        var body = new string('a', 20);
        var result = AcdcException.TruncateResponseBody(body, maxLength: 10);
        result.Should().Be("aaaaaaaaaa[truncated]");
    }
}
