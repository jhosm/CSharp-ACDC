using System.Net.Http.Headers;
using CSharpAcdc.Extensions;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Extensions;

public class HttpRequestMessageExtensionsTests
{
    [Fact]
    public async Task CloneAsync_CopiesMethodAndUri()
    {
        using var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");

        using var clone = await original.CloneAsync();

        clone.Method.Should().Be(HttpMethod.Post);
        clone.RequestUri.Should().Be(new Uri("https://api.example.com/data"));
    }

    [Fact]
    public async Task CloneAsync_CopiesRequestHeaders()
    {
        using var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        original.Headers.Add("X-Custom", "value1");
        original.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var clone = await original.CloneAsync();

        clone.Headers.GetValues("X-Custom").Should().ContainSingle().Which.Should().Be("value1");
        clone.Headers.Accept.Should().ContainSingle().Which.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CloneAsync_CopiesContentAndContentHeaders()
    {
        using var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");
        original.Content = new StringContent("""{"key":"value"}""", System.Text.Encoding.UTF8, "application/json");

        using var clone = await original.CloneAsync();

        clone.Content.Should().NotBeNull();
        var clonedBody = await clone.Content!.ReadAsStringAsync();
        clonedBody.Should().Be("""{"key":"value"}""");
        clone.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CloneAsync_NullContent_RemainsNull()
    {
        using var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        using var clone = await original.CloneAsync();

        clone.Content.Should().BeNull();
    }

    [Fact]
    public async Task CloneAsync_CopiesOptions()
    {
        using var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        original.Options.Set(AcdcRequestOptions.SkipCache, true);
        original.Options.Set(AcdcRequestOptions.RetryCount, 3);

        using var clone = await original.CloneAsync();

        clone.Options.TryGetValue(AcdcRequestOptions.SkipCache, out var skipCache).Should().BeTrue();
        skipCache.Should().BeTrue();
        clone.Options.TryGetValue(AcdcRequestOptions.RetryCount, out var retryCount).Should().BeTrue();
        retryCount.Should().Be(3);
    }

    [Fact]
    public async Task CloneAsync_ProducesIndependentCopy()
    {
        using var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        original.Headers.Add("X-Original", "present");

        using var clone = await original.CloneAsync();
        clone.Headers.Add("X-Clone-Only", "added");

        original.Headers.Contains("X-Clone-Only").Should().BeFalse();
    }

    [Fact]
    public async Task CloneAsync_CopiesVersionAndVersionPolicy()
    {
        using var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test")
        {
            Version = new Version(2, 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        using var clone = await original.CloneAsync();

        clone.Version.Should().Be(new Version(2, 0));
        clone.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionExact);
    }

    [Fact]
    public async Task CloneAsync_OriginalContentRemainsSendable()
    {
        using var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");
        original.Content = new StringContent("original-body");

        using var clone = await original.CloneAsync();

        // Original content should still be readable after cloning
        var originalBody = await original.Content!.ReadAsStringAsync();
        originalBody.Should().Be("original-body");

        var clonedBody = await clone.Content!.ReadAsStringAsync();
        clonedBody.Should().Be("original-body");
    }
}
