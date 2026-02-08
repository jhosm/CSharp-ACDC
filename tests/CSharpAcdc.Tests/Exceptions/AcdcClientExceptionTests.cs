using System.Net;
using CSharpAcdc.Exceptions;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Exceptions;

public class AcdcClientExceptionTests
{
    [Fact]
    public void RetryAfter_Delta_SetsTimeSpan()
    {
        var retryAfter = TimeSpan.FromSeconds(60);
        var ex = new AcdcClientException(
            "Too many requests",
            HttpStatusCode.TooManyRequests,
            retryAfter: retryAfter);

        ex.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void RetryAfter_Null_WhenNotProvided()
    {
        var ex = new AcdcClientException("Not found", HttpStatusCode.NotFound);

        ex.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void ToMap_IncludesRetryAfter_WhenSet()
    {
        var ex = new AcdcClientException(
            "Rate limited",
            HttpStatusCode.TooManyRequests,
            retryAfter: TimeSpan.FromSeconds(120));

        var map = ex.ToMap();

        map.Should().ContainKey("retryAfter").WhoseValue.Should().Be(120.0);
    }

    [Fact]
    public void ToMap_OmitsRetryAfter_WhenNull()
    {
        var ex = new AcdcClientException("Not found", HttpStatusCode.NotFound);

        var map = ex.ToMap();

        map.Should().NotContainKey("retryAfter");
    }

    [Fact]
    public void StatusCode_SetsCorrectly()
    {
        var ex = new AcdcClientException("Bad request", HttpStatusCode.BadRequest);
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void IsAcdcException()
    {
        var ex = new AcdcClientException("test");
        (ex is AcdcException).Should().BeTrue();
    }
}
