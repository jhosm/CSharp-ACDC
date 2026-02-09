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
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");

        var clone = await original.CloneAsync();

        clone.Method.Should().Be(HttpMethod.Post);
        clone.RequestUri.Should().Be(new Uri("https://api.example.com/data"));
    }

    [Fact]
    public async Task CloneAsync_CopiesRequestHeaders()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        original.Headers.Add("X-Custom", "value1");
        original.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var clone = await original.CloneAsync();

        clone.Headers.GetValues("X-Custom").Should().ContainSingle().Which.Should().Be("value1");
        clone.Headers.Accept.Should().ContainSingle().Which.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CloneAsync_CopiesContentAndContentHeaders()
    {
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");
        original.Content = new StringContent("""{"key":"value"}""", System.Text.Encoding.UTF8, "application/json");

        var clone = await original.CloneAsync();

        clone.Content.Should().NotBeNull();
        var clonedBody = await clone.Content!.ReadAsStringAsync();
        clonedBody.Should().Be("""{"key":"value"}""");
        clone.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CloneAsync_NullContent_RemainsNull()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var clone = await original.CloneAsync();

        clone.Content.Should().BeNull();
    }

    [Fact]
    public async Task CloneAsync_CopiesOptions()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        original.Options.Set(AcdcRequestOptions.SkipCache, true);
        original.Options.Set(AcdcRequestOptions.RetryCount, 3);

        var clone = await original.CloneAsync();

        clone.Options.TryGetValue(AcdcRequestOptions.SkipCache, out var skipCache).Should().BeTrue();
        skipCache.Should().BeTrue();
        clone.Options.TryGetValue(AcdcRequestOptions.RetryCount, out var retryCount).Should().BeTrue();
        retryCount.Should().Be(3);
    }

    [Fact]
    public async Task CloneAsync_ProducesIndependentCopy()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        original.Headers.Add("X-Original", "present");

        var clone = await original.CloneAsync();
        clone.Headers.Add("X-Clone-Only", "added");

        original.Headers.Contains("X-Clone-Only").Should().BeFalse();
    }
}
