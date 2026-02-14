using CSharpAcdc.Cache;
using CSharpAcdc.Client;
using CSharpAcdc.Extensions;
using Microsoft.Extensions.DependencyInjection;

// Register ACDC with caching enabled
var services = new ServiceCollection();
services.AddLogging();
services.AddAcdcHttpClient(b => b
    .WithCache(cache =>
    {
        cache.Duration = TimeSpan.FromMinutes(10);
        cache.ETagEnabled = true;
        cache.CacheKeyStrategy = CacheKeyStrategy.Shared;
        cache.FailSafeMaxDuration = TimeSpan.FromHours(1);
        cache.FactorySoftTimeout = TimeSpan.FromSeconds(1);
    })
    .WithBaseAddress(new Uri("https://httpbin.org")));

var sp = services.BuildServiceProvider();
var client = sp.GetRequiredService<AcdcHttpClient>();

// First request — fetches from server and caches the response
Console.WriteLine("First request (cache miss):");
var response1 = await client.GetAsync("/get");
Console.WriteLine($"  Status: {response1.StatusCode}");
Console.WriteLine($"  From cache: {response1.Headers.Contains("X-ACDC-From-Cache")}");

// Second request — served from cache
Console.WriteLine("\nSecond request (cache hit):");
var response2 = await client.GetAsync("/get");
Console.WriteLine($"  Status: {response2.StatusCode}");
Console.WriteLine($"  From cache: {response2.Headers.Contains("X-ACDC-From-Cache")}");

// Clear cache programmatically
await client.Cache!.ClearCacheAsync();
Console.WriteLine("\nCache cleared");
