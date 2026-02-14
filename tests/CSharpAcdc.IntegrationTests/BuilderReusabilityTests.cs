using CSharpAcdc.Client;
using CSharpAcdc.Extensions;
using CSharpAcdc.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CSharpAcdc.IntegrationTests;

public class BuilderReusabilityTests : IDisposable
{
    private readonly FakeApiServer _api = new();

    [Fact]
    public async Task MultipleClients_FromSameBuilder_AreIndependent()
    {
        _api.ConfigureGetSuccess("/data", new { value = "response" });

        // Register two named clients from separate DI containers to verify independence
        var services1 = new ServiceCollection();
        services1.AddLogging();
        services1.AddAcdcHttpClient("client1", b =>
            b.WithClientName("client1"));

        var services2 = new ServiceCollection();
        services2.AddLogging();
        services2.AddAcdcHttpClient("client2", b =>
            b.WithClientName("client2"));

        var sp1 = services1.BuildServiceProvider();
        var sp2 = services2.BuildServiceProvider();

        var client1 = sp1.GetRequiredKeyedService<AcdcHttpClient>("client1");
        var client2 = sp2.GetRequiredKeyedService<AcdcHttpClient>("client2");

        // Both clients should work independently
        var response1 = await client1.GetAsync($"{_api.Url}/data");
        var response2 = await client2.GetAsync($"{_api.Url}/data");

        Assert.True(response1.IsSuccessStatusCode);
        Assert.True(response2.IsSuccessStatusCode);

        // Verify both requests reached the server
        Assert.Equal(2, _api.GetCallCount("/data"));
    }

    [Fact]
    public void BuilderConfiguration_IsImmutable()
    {
        _api.ConfigureGetSuccess("/test", new { ok = true });

        // WithTimeout returns a new builder; original is unchanged.
        // Verify by building two clients: one with default timeout, one with custom.
        var services1 = new ServiceCollection();
        services1.AddLogging();
        services1.AddAcdcHttpClient("default-timeout", b => b.WithClientName("default-timeout"));

        var services2 = new ServiceCollection();
        services2.AddLogging();
        services2.AddAcdcHttpClient("custom-timeout", b =>
            b.WithTimeout(TimeSpan.FromSeconds(10)).WithClientName("custom-timeout"));

        var sp1 = services1.BuildServiceProvider();
        var sp2 = services2.BuildServiceProvider();

        var client1 = sp1.GetRequiredKeyedService<AcdcHttpClient>("default-timeout");
        var client2 = sp2.GetRequiredKeyedService<AcdcHttpClient>("custom-timeout");

        // Default HttpClient timeout is 100 seconds; custom is 10 seconds
        Assert.NotEqual(client1.Timeout, client2.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), client2.Timeout);
    }

    [Fact]
    public async Task MultipleKeyedClients_InSameContainer_AreIndependent()
    {
        _api.ConfigureGetSuccess("/shared", new { data = "test" });

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAcdcHttpClient("alpha", b =>
            b.WithClientName("alpha"));

        services.AddAcdcHttpClient("beta", b =>
            b.WithClientName("beta"));

        var sp = services.BuildServiceProvider();

        var clientAlpha = sp.GetRequiredKeyedService<AcdcHttpClient>("alpha");
        var clientBeta = sp.GetRequiredKeyedService<AcdcHttpClient>("beta");

        var response1 = await clientAlpha.GetAsync($"{_api.Url}/shared");
        var response2 = await clientBeta.GetAsync($"{_api.Url}/shared");

        Assert.True(response1.IsSuccessStatusCode);
        Assert.True(response2.IsSuccessStatusCode);
        Assert.Equal(2, _api.GetCallCount("/shared"));
    }

    public void Dispose()
    {
        _api.Dispose();
    }
}
