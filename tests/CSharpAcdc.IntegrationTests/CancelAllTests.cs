using CSharpAcdc.Client;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Extensions;
using CSharpAcdc.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CSharpAcdc.IntegrationTests;

public class CancelAllTests : IDisposable
{
    private readonly FakeApiServer _api = new();

    [Fact]
    public async Task CancelAll_CancelsInFlightRequests()
    {
        // Configure a slow endpoint so requests are still in-flight when we cancel
        _api.ConfigureGetWithDelay("/slow", new { value = "response" }, TimeSpan.FromSeconds(10));

        using var client = BuildClient();

        // Start a request that will be slow
        var requestTask = client.GetAsync($"{_api.Url}/slow");

        // Give it a moment to start
        await Task.Delay(100);

        // Cancel all in-flight requests
        client.CancelAll();

        // CancelAll uses a linked CTS internal to CancellationHandler; the caller's
        // CancellationToken is NOT cancelled. The ErrorHandler sees this as a timeout
        // (TaskCanceledException without caller cancellation) and wraps it as AcdcNetworkException.
        var ex = await Assert.ThrowsAsync<AcdcNetworkException>(() => requestTask);
        Assert.Equal(NetworkErrorType.Timeout, ex.NetworkErrorType);
    }

    [Fact]
    public async Task CancelAll_NewRequestsSucceedAfterward()
    {
        _api.ConfigureGetWithDelay("/slow", new { value = "slow" }, TimeSpan.FromSeconds(10));
        _api.ConfigureGetSuccess("/fast", new { value = "fast" });

        using var client = BuildClient();

        // Start a slow request
        var slowTask = client.GetAsync($"{_api.Url}/slow");
        await Task.Delay(100);

        // Cancel all
        client.CancelAll();

        // Wait for cancellation to propagate
        try { await slowTask; } catch { /* expected */ }

        // New requests should succeed
        var response = await client.GetAsync($"{_api.Url}/fast");
        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("fast", body);
    }

    private AcdcHttpClient BuildClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcdcHttpClient();

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<AcdcHttpClient>();
    }

    public void Dispose()
    {
        _api.Dispose();
    }
}
