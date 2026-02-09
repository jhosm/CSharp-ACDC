using CSharpAcdc.Cache;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Cache;

public class CacheKeyBuilderTests
{
    [Fact]
    public void BuildKey_Shared_ReturnsMethodAndUrl()
    {
        var key = CacheKeyBuilder.BuildKey(HttpMethod.Get, "https://api.example.com/products", CacheKeyStrategy.Shared);

        key.Should().Be("GET:https://api.example.com/products");
    }

    [Fact]
    public void BuildKey_Shared_IgnoresUserId()
    {
        var key = CacheKeyBuilder.BuildKey(
            HttpMethod.Get, "https://api.example.com/products", CacheKeyStrategy.Shared, userId: "user-42");

        key.Should().Be("GET:https://api.example.com/products");
    }

    [Fact]
    public void BuildKey_UserIsolated_WithUserId_IncludesUserId()
    {
        var key = CacheKeyBuilder.BuildKey(
            HttpMethod.Get, "https://api.example.com/profile", CacheKeyStrategy.UserIsolated, userId: "user-42");

        key.Should().Be("GET:user-42:https://api.example.com/profile");
    }

    [Fact]
    public void BuildKey_UserIsolated_WithoutUserId_FallsBackToSharedFormat()
    {
        var key = CacheKeyBuilder.BuildKey(
            HttpMethod.Get, "https://api.example.com/profile", CacheKeyStrategy.UserIsolated);

        key.Should().Be("GET:https://api.example.com/profile");
    }

    [Fact]
    public void BuildKey_NoCache_ReturnsNull()
    {
        var key = CacheKeyBuilder.BuildKey(HttpMethod.Get, "https://api.example.com/products", CacheKeyStrategy.NoCache);

        key.Should().BeNull();
    }

    [Fact]
    public void BuildKey_Head_ReturnsHeadMethod()
    {
        var key = CacheKeyBuilder.BuildKey(HttpMethod.Head, "https://api.example.com/health", CacheKeyStrategy.Shared);

        key.Should().Be("HEAD:https://api.example.com/health");
    }

    [Fact]
    public void BuildKey_Post_ReturnsPostMethod()
    {
        var key = CacheKeyBuilder.BuildKey(HttpMethod.Post, "https://api.example.com/data", CacheKeyStrategy.Shared);

        key.Should().Be("POST:https://api.example.com/data");
    }
}
