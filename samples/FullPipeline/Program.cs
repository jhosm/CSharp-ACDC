using CSharpAcdc.Auth;
using CSharpAcdc.Cache;
using CSharpAcdc.Client;
using CSharpAcdc.Exceptions;
using CSharpAcdc.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Register ACDC with all features enabled
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

services.AddAcdcHttpClient(b => b
    .WithAuth(auth =>
    {
        auth.RefreshEndpoint = "https://auth.example.com/oauth/token";
        auth.ClientId = "my-client-id";
        auth.RefreshThreshold = TimeSpan.FromSeconds(60);
    })
    .WithCache(cache =>
    {
        cache.Duration = TimeSpan.FromMinutes(5);
        cache.ETagEnabled = true;
        cache.CacheKeyStrategy = CacheKeyStrategy.UserIsolated;
    })
    .WithLogging(logging =>
    {
        logging.SlowRequestThreshold = TimeSpan.FromSeconds(2);
    })
    .WithBaseAddress(new Uri("https://api.example.com"))
    .WithTimeout(TimeSpan.FromSeconds(10)));

services.AddHttpClient("acdc-auth");

var sp = services.BuildServiceProvider();

// Seed tokens
var tokenProvider = sp.GetRequiredKeyedService<ITokenProvider>("acdc");
await tokenProvider.SaveTokensAsync(
    "my-access-token", "my-refresh-token",
    DateTimeOffset.UtcNow.AddHours(1),
    CancellationToken.None);

var client = sp.GetRequiredService<AcdcHttpClient>();

try
{
    // Authenticated + cached GET request
    var response = await client.GetAsync("/api/data");
    Console.WriteLine($"GET /api/data: {response.StatusCode}");

    // POST invalidates related cache entries
    await client.PostAsync("/api/data", new StringContent("{}"));
    Console.WriteLine("POST /api/data: cache invalidated");
}
catch (AcdcAuthException ex)
{
    Console.WriteLine($"Auth error: {ex.Message} (status: {ex.StatusCode})");
}
catch (AcdcNetworkException ex)
{
    Console.WriteLine($"Network error: {ex.NetworkErrorType} — {ex.Message}");
}
catch (AcdcServerException ex)
{
    Console.WriteLine($"Server error: {ex.StatusCode} — {ex.ResponseBody}");
}
catch (AcdcClientException ex)
{
    Console.WriteLine($"Client error: {ex.StatusCode}");
    if (ex.RetryAfter.HasValue)
        Console.WriteLine($"  Retry after: {ex.RetryAfter.Value.TotalSeconds}s");
}
