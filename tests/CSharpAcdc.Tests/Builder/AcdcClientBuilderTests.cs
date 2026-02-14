using CSharpAcdc.Builder;
using CSharpAcdc.Configuration;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Builder;

public class AcdcClientBuilderTests
{
    [Fact]
    public void Create_ReturnsBuilderWithDefaults()
    {
        var builder = AcdcClientBuilder.Create();

        builder.HasAuth.Should().BeFalse();
        builder.HasCache.Should().BeFalse();
        builder.GetCustomHandlerTypes().Should().BeEmpty();
    }

    [Fact]
    public void WithAuth_ReturnsNewInstance_OriginalUnchanged()
    {
        var original = AcdcClientBuilder.Create();
        var modified = original.WithAuth(a =>
        {
            a.RefreshEndpoint = "https://auth/token";
            a.ClientId = "test";
        });

        modified.Should().NotBeSameAs(original);
        modified.HasAuth.Should().BeTrue();
        original.HasAuth.Should().BeFalse();
    }

    [Fact]
    public void WithCache_ReturnsNewInstance_OriginalUnchanged()
    {
        var original = AcdcClientBuilder.Create();
        var modified = original.WithCache(c => c.Duration = TimeSpan.FromMinutes(10));

        modified.Should().NotBeSameAs(original);
        modified.HasCache.Should().BeTrue();
        original.HasCache.Should().BeFalse();
    }

    [Fact]
    public void WithLogging_ReturnsNewInstance()
    {
        var original = AcdcClientBuilder.Create();
        var modified = original.WithLogging(l => l.SlowRequestThreshold = TimeSpan.FromSeconds(10));

        modified.Should().NotBeSameAs(original);
    }

    [Fact]
    public void WithCustomHandler_ReturnsNewInstance_OriginalUnchanged()
    {
        var original = AcdcClientBuilder.Create();
        var modified = original.WithCustomHandler<TestHandler>();

        modified.Should().NotBeSameAs(original);
        modified.GetCustomHandlerTypes().Should().ContainSingle()
            .Which.Should().Be(typeof(TestHandler));
        original.GetCustomHandlerTypes().Should().BeEmpty();
    }

    [Fact]
    public void WithCustomHandler_Multiple_PreservesOrder()
    {
        var builder = AcdcClientBuilder.Create()
            .WithCustomHandler<TestHandler>()
            .WithCustomHandler<AnotherTestHandler>();

        builder.GetCustomHandlerTypes().Should().Equal(
            typeof(TestHandler), typeof(AnotherTestHandler));
    }

    [Fact]
    public void WithTimeout_ReturnsNewInstance()
    {
        var original = AcdcClientBuilder.Create();
        var modified = original.WithTimeout(TimeSpan.FromSeconds(30));

        modified.Should().NotBeSameAs(original);
    }

    [Fact]
    public void WithBaseAddress_ReturnsNewInstance()
    {
        var original = AcdcClientBuilder.Create();
        var modified = original.WithBaseAddress(new Uri("https://api.example.com"));

        modified.Should().NotBeSameAs(original);
    }

    [Fact]
    public void WithClientName_ReturnsNewInstance()
    {
        var original = AcdcClientBuilder.Create();
        var modified = original.WithClientName("my-api");

        modified.Should().NotBeSameAs(original);
    }

    [Fact]
    public void MethodChaining_AllOptionsApplied()
    {
        var builder = AcdcClientBuilder.Create()
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth/token";
                a.ClientId = "test";
            })
            .WithCache(c => c.Duration = TimeSpan.FromMinutes(10))
            .WithLogging(l => l.SlowRequestThreshold = TimeSpan.FromSeconds(5))
            .WithCustomHandler<TestHandler>()
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithBaseAddress(new Uri("https://api.example.com"))
            .WithClientName("my-api");

        builder.HasAuth.Should().BeTrue();
        builder.HasCache.Should().BeTrue();
        builder.GetCustomHandlerTypes().Should().ContainSingle();
    }

    [Fact]
    public void BuildOptions_AppliesAuthDelegate()
    {
        var builder = AcdcClientBuilder.Create()
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth/token";
                a.ClientId = "test-client";
            });

        var options = builder.BuildOptions();

        options.Auth.Should().NotBeNull();
        options.Auth!.RefreshEndpoint.Should().Be("https://auth/token");
        options.Auth.ClientId.Should().Be("test-client");
    }

    [Fact]
    public void BuildOptions_AppliesCacheDelegate()
    {
        var builder = AcdcClientBuilder.Create()
            .WithCache(c => c.Duration = TimeSpan.FromMinutes(15));

        var options = builder.BuildOptions();

        options.Cache.Should().NotBeNull();
        options.Cache!.Duration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void BuildOptions_AppliesLoggingDelegate()
    {
        var builder = AcdcClientBuilder.Create()
            .WithLogging(l => l.SlowRequestThreshold = TimeSpan.FromSeconds(10));

        var options = builder.BuildOptions();

        options.Logging.SlowRequestThreshold.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void BuildOptions_NoConfigDelegates_ReturnsDefaults()
    {
        var builder = AcdcClientBuilder.Create();
        var options = builder.BuildOptions();

        options.Auth.Should().BeNull();
        options.Cache.Should().BeNull();
        options.Logging.Should().NotBeNull();
        options.ClientName.Should().Be("acdc");
        options.BaseAddress.Should().BeNull();
        options.Timeout.Should().BeNull();
    }

    [Fact]
    public void BuildOptions_SetsBaseAddressAndTimeout()
    {
        var builder = AcdcClientBuilder.Create()
            .WithBaseAddress(new Uri("https://api.example.com"))
            .WithTimeout(TimeSpan.FromSeconds(42));

        var options = builder.BuildOptions();

        options.BaseAddress.Should().Be(new Uri("https://api.example.com"));
        options.Timeout.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void BuildOptions_SetsClientName()
    {
        var builder = AcdcClientBuilder.Create()
            .WithClientName("my-custom-api");

        var options = builder.BuildOptions();

        options.ClientName.Should().Be("my-custom-api");
    }

    private class TestHandler : DelegatingHandler;
    private class AnotherTestHandler : DelegatingHandler;
}
