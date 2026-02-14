using System.Net;
using CSharpAcdc.Auth;
using CSharpAcdc.Builder;
using CSharpAcdc.Cache;
using CSharpAcdc.Cancellation;
using CSharpAcdc.Client;
using CSharpAcdc.Configuration;
using CSharpAcdc.Extensions;
using CSharpAcdc.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace CSharpAcdc.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void ZeroConfig_ResolvesWorkingClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient();
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<AcdcHttpClient>();

        client.Should().NotBeNull();
        client.Auth.Should().BeNull();
        client.Cache.Should().BeNull();
    }

    [Fact]
    public void ZeroConfig_HandlerPipeline_ExcludesAuthAndCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient();
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        handlerTypes.Should().Contain(typeof(LoggingHandler));
        handlerTypes.Should().Contain(typeof(ErrorHandler));
        handlerTypes.Should().Contain(typeof(CancellationHandler));
        handlerTypes.Should().Contain(typeof(DeduplicationHandler));
        handlerTypes.Should().NotContain(typeof(AuthHandler));
        handlerTypes.Should().NotContain(typeof(CacheHandler));
    }

    [Fact]
    public void FullConfig_HandlerPipeline_CorrectOrder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth/token";
                a.ClientId = "test";
            })
            .WithCache(c => { })
            .WithCustomHandler<TestCustomHandler>());
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        handlerTypes.Should().Equal(
            typeof(LoggingHandler),
            typeof(ErrorHandler),
            typeof(CancellationHandler),
            typeof(AuthHandler),
            typeof(CacheHandler),
            typeof(TestCustomHandler),
            typeof(DeduplicationHandler));
    }

    [Fact]
    public void CustomHandler_InsertedAfterCacheBeforeDedup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithCache(c => { })
            .WithCustomHandler<TestCustomHandler>());
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        var cacheIndex = handlerTypes.IndexOf(typeof(CacheHandler));
        var customIndex = handlerTypes.IndexOf(typeof(TestCustomHandler));
        var dedupIndex = handlerTypes.IndexOf(typeof(DeduplicationHandler));

        customIndex.Should().BeGreaterThan(cacheIndex);
        customIndex.Should().BeLessThan(dedupIndex);
    }

    [Fact]
    public void CustomHandler_WithoutCache_InsertedBeforeDedup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithCustomHandler<TestCustomHandler>());
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        var cancellationIndex = handlerTypes.IndexOf(typeof(CancellationHandler));
        var customIndex = handlerTypes.IndexOf(typeof(TestCustomHandler));
        var dedupIndex = handlerTypes.IndexOf(typeof(DeduplicationHandler));

        customIndex.Should().BeGreaterThan(cancellationIndex);
        customIndex.Should().BeLessThan(dedupIndex);
    }

    [Fact]
    public void MultipleCustomHandlers_InsertedInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithCustomHandler<TestCustomHandler>()
            .WithCustomHandler<AnotherCustomHandler>());
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        var firstIndex = handlerTypes.IndexOf(typeof(TestCustomHandler));
        var secondIndex = handlerTypes.IndexOf(typeof(AnotherCustomHandler));

        firstIndex.Should().BeLessThan(secondIndex);
    }

    [Fact]
    public void OptionalHandlers_AuthOmittedWhenNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithCache(c => { }));
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        handlerTypes.Should().Contain(typeof(CacheHandler));
        handlerTypes.Should().NotContain(typeof(AuthHandler));
    }

    [Fact]
    public void OptionalHandlers_CacheOmittedWhenNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth/token";
                a.ClientId = "test";
            }));
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        handlerTypes.Should().Contain(typeof(AuthHandler));
        handlerTypes.Should().NotContain(typeof(CacheHandler));
    }

    [Fact]
    public void DiRegistration_ResolvesAllServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth/token";
                a.ClientId = "test";
            })
            .WithCache(c => { }));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<AcdcHttpClient>();
        var tracker = sp.GetRequiredKeyedService<ActiveRequestTracker>("acdc");
        var authManager = sp.GetRequiredKeyedService<AcdcAuthManager>("acdc");
        var cacheManager = sp.GetRequiredKeyedService<IAcdcCacheManager>("acdc");

        client.Should().NotBeNull();
        client.Auth.Should().NotBeNull();
        client.Cache.Should().NotBeNull();
        tracker.Should().NotBeNull();
        authManager.Should().NotBeNull();
        cacheManager.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurationBinding_BindsFromIConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseAddress"] = "https://api.example.com",
                ["Timeout"] = "00:00:30",
                ["Auth:RefreshEndpoint"] = "https://auth.example.com/token",
                ["Auth:ClientId"] = "my-client",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(config);
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<AcdcHttpClient>();

        client.Should().NotBeNull();
        client.BaseAddress.Should().Be(new Uri("https://api.example.com"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        client.Auth.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurationBinding_NullAuth_OmitsAuthHandler()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseAddress"] = "https://api.example.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(config);
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<AcdcHttpClient>();
        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");

        client.Auth.Should().BeNull();
        handlerTypes.Should().NotContain(typeof(AuthHandler));
    }

    [Fact]
    public void NamedClients_IndependentConfigurations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient("api-a", builder => builder
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth-a/token";
                a.ClientId = "client-a";
            })
            .WithBaseAddress(new Uri("https://api-a.example.com")));
        services.AddAcdcHttpClient("api-b", builder => builder
            .WithBaseAddress(new Uri("https://api-b.example.com")));
        using var sp = services.BuildServiceProvider();

        var clientA = sp.GetRequiredKeyedService<AcdcHttpClient>("api-a");
        var clientB = sp.GetRequiredKeyedService<AcdcHttpClient>("api-b");

        clientA.BaseAddress.Should().Be(new Uri("https://api-a.example.com"));
        clientA.Auth.Should().NotBeNull();

        clientB.BaseAddress.Should().Be(new Uri("https://api-b.example.com"));
        clientB.Auth.Should().BeNull();
    }

    [Fact]
    public void NamedClients_IndependentTrackers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient("api-a", null);
        services.AddAcdcHttpClient("api-b", null);
        using var sp = services.BuildServiceProvider();

        var trackerA = sp.GetRequiredKeyedService<ActiveRequestTracker>("api-a");
        var trackerB = sp.GetRequiredKeyedService<ActiveRequestTracker>("api-b");

        trackerA.Should().NotBeSameAs(trackerB);
    }

    [Fact]
    public void PipelineOrdering_IndependentOfConfigOrder()
    {
        // Configure cache before auth — auth should still precede cache in pipeline
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithCache(c => { })
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth/token";
                a.ClientId = "test";
            }));
        using var sp = services.BuildServiceProvider();

        var handlerTypes = GetPipelineHandlerTypes(sp, "acdc");
        var authIndex = handlerTypes.IndexOf(typeof(AuthHandler));
        var cacheIndex = handlerTypes.IndexOf(typeof(CacheHandler));

        authIndex.Should().BeLessThan(cacheIndex);
    }

    [Fact]
    public void BaseAddressAndTimeout_AppliedToHttpClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithBaseAddress(new Uri("https://api.example.com"))
            .WithTimeout(TimeSpan.FromSeconds(42)));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<AcdcHttpClient>();

        client.BaseAddress.Should().Be(new Uri("https://api.example.com"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void SingletonServices_SharedAcrossResolutions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient(builder => builder
            .WithAuth(a =>
            {
                a.RefreshEndpoint = "https://auth/token";
                a.ClientId = "test";
            }));
        using var sp = services.BuildServiceProvider();

        var tracker1 = sp.GetRequiredKeyedService<ActiveRequestTracker>("acdc");
        var tracker2 = sp.GetRequiredKeyedService<ActiveRequestTracker>("acdc");
        var backoff1 = sp.GetRequiredKeyedService<BackoffManager>("acdc");
        var backoff2 = sp.GetRequiredKeyedService<BackoffManager>("acdc");

        tracker1.Should().BeSameAs(tracker2);
        backoff1.Should().BeSameAs(backoff2);
    }

    // -- Helper to inspect handler pipeline order --

    private static List<Type> GetPipelineHandlerTypes(IServiceProvider sp, string clientName)
    {
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();
        var httpOptions = optionsMonitor.Get(clientName);

        var builder = new TestHandlerBuilder(sp) { Name = clientName };
        foreach (var action in httpOptions.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        return builder.AdditionalHandlers.Select(h => h.GetType()).ToList();
    }

    private sealed class TestHandlerBuilder : HttpMessageHandlerBuilder
    {
        private readonly IServiceProvider _services;
        public TestHandlerBuilder(IServiceProvider services) => _services = services;
        public override string? Name { get; set; }
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();
        public override IServiceProvider Services => _services;
        public override HttpMessageHandler Build() => PrimaryHandler;
    }

    public class TestCustomHandler : DelegatingHandler;
    public class AnotherCustomHandler : DelegatingHandler;
}
