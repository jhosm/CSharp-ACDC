# CSharp-ACDC

Server-only HTTP client library for .NET with authentication, caching, logging, and structured error handling. Uses `DelegatingHandler` pipeline with `IHttpClientFactory`.

## Features

- **Authentication** — OAuth 2.1 token refresh with proactive (before expiry) and reactive (on 401) strategies, concurrent refresh queue, exponential backoff
- **Caching** — FusionCache integration with ETag/If-None-Match, stale-while-revalidate, mutation invalidation, user-isolated cache keys
- **Logging** — Structured logging via `ILogger<T>` with sensitive data redaction, slow request warnings, large payload alerts
- **Error Handling** — Typed exception hierarchy mapping HTTP status codes to specific exception types
- **Cancellation** — Per-request cancellation tokens with bulk `CancelAll()` support
- **Deduplication** — Automatic deduplication of concurrent identical GET and HEAD requests
- **DI-First** — Full `IHttpClientFactory` integration with keyed services and fluent builder API

## Installation

```bash
dotnet add package CSharpAcdc
```

## Quick Start

```csharp
// Zero-config — registers the ACDC pipeline with sensible defaults
builder.Services.AddAcdcHttpClient();

// Inject and use
app.MapGet("/", async (AcdcHttpClient client) =>
{
    var response = await client.GetAsync("https://api.example.com/data");
    return await response.Content.ReadAsStringAsync();
});
```

## Authenticated Client

```csharp
builder.Services.AddAcdcHttpClient(b => b
    .WithAuth(auth =>
    {
        auth.RefreshEndpoint = "https://auth.example.com/oauth/token";
        auth.ClientId = "my-client-id";
        auth.ClientSecret = "my-client-secret";
        auth.RefreshThreshold = TimeSpan.FromSeconds(60);
    })
    .WithBaseAddress(new Uri("https://api.example.com")));
```

Token refresh happens automatically — proactively before expiry and reactively on 401 responses. Concurrent requests share a single refresh call.

## Cached Client

```csharp
builder.Services.AddAcdcHttpClient(b => b
    .WithCache(cache =>
    {
        cache.Duration = TimeSpan.FromMinutes(10);
        cache.ETagEnabled = true;
        cache.CacheKeyStrategy = CacheKeyStrategy.UserIsolated;
        cache.MaxStaleAge = TimeSpan.FromHours(1);
        cache.StaleWhileRevalidateTimeout = TimeSpan.FromSeconds(1);
    }));
```

## Full Pipeline

```csharp
builder.Services.AddAcdcHttpClient(b => b
    .WithAuth(auth =>
    {
        auth.RefreshEndpoint = "https://auth.example.com/oauth/token";
        auth.ClientId = "my-client-id";
    })
    .WithCache(cache =>
    {
        cache.Duration = TimeSpan.FromMinutes(5);
        cache.ETagEnabled = true;
    })
    .WithLogging(logging =>
    {
        logging.SlowRequestThreshold = TimeSpan.FromSeconds(2);
    })
    .WithBaseAddress(new Uri("https://api.example.com"))
    .WithTimeout(TimeSpan.FromSeconds(10)));
```

## Handler Pipeline

Requests and responses flow through a chain of `DelegatingHandler` instances in a fixed order:

```
Request  → [Logging] → [Error] → [Cancellation] → [Auth] → [Cache] → [Custom] → [Dedup] → HttpClient
                                                                                                ↓
Response ← [Logging] ← [Error] ← [Cancellation] ← [Auth] ← [Cache] ← [Custom] ← [Dedup] ← Server
```

| Handler | Purpose |
|---------|---------|
| **Logging** | Logs request/response details with sensitive data redaction |
| **Error** | Converts HTTP errors and exceptions to typed ACDC exceptions |
| **Cancellation** | Tracks active requests for bulk cancellation |
| **Auth** | Injects Bearer token, handles proactive/reactive refresh |
| **Cache** | FusionCache with ETag, SWR, mutation invalidation |
| **Custom** | User-registered `DelegatingHandler` types |
| **Dedup** | Deduplicates concurrent identical GET and HEAD requests |

## Configuration Reference

### AcdcAuthOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RefreshEndpoint` | `string` | *(required)* | OAuth token endpoint URL |
| `ClientId` | `string` | *(required)* | OAuth client ID |
| `ClientSecret` | `string?` | `null` | OAuth client secret |
| `RefreshThreshold` | `TimeSpan` | 60s | Time before expiry to trigger proactive refresh |
| `QueueTimeout` | `TimeSpan` | 30s | Max wait time for concurrent refresh queue |
| `RevocationEndpoint` | `string?` | `null` | OAuth token revocation endpoint |

### AcdcCacheOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Duration` | `TimeSpan` | 5 min | Cache entry lifetime |
| `ETagEnabled` | `bool` | `true` | Enable ETag/If-None-Match revalidation |
| `CacheKeyStrategy` | `CacheKeyStrategy` | `Shared` | `Shared`, `UserIsolated`, or `NoCache` |
| `MaxStaleAge` | `TimeSpan?` | `null` | Max stale data lifetime (enables fail-safe) |
| `StaleWhileRevalidateTimeout` | `TimeSpan?` | `null` | Timeout before returning stale data (SWR) |
| `BackgroundRefreshOnTimeout` | `bool` | `true` | Continue refresh in background after SWR |

### AcdcLoggingOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SlowRequestThreshold` | `TimeSpan` | 3s | Threshold for slow request warnings |
| `LargePayloadThreshold` | `long` | 1 MiB | Threshold for large payload alerts |
| `SensitiveFields` | `IReadOnlySet<string>` | *(see below)* | Header/field names to redact |

### AcdcClientOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseAddress` | `Uri?` | `null` | Base URL for all requests |
| `Timeout` | `TimeSpan?` | `null` | Request timeout |
| `ClientName` | `string` | `"acdc"` | Named HttpClient identifier |

## Exception Handling

All ACDC exceptions extend `HttpRequestException`, so they integrate naturally with existing error handling:

```
HttpRequestException
  └─ AcdcException (base: URL redaction, response truncation)
       ├─ AcdcAuthException        (401, 403)
       ├─ AcdcClientException      (4xx, has RetryAfter)
       ├─ AcdcServerException      (5xx)
       ├─ AcdcNetworkException     (timeouts, DNS; has NetworkErrorType)
       └─ AcdcCacheException       (cache failures; has CacheOperation)
```

```csharp
try
{
    var response = await client.GetAsync("/api/data");
}
catch (AcdcAuthException ex)
{
    // 401/403 — token expired or insufficient permissions
    logger.LogWarning("Auth failed: {Message}", ex.Message);
}
catch (AcdcServerException ex)
{
    // 5xx — downstream service error
    logger.LogError("Server error {StatusCode}: {Body}", ex.StatusCode, ex.ResponseBody);
}
catch (AcdcNetworkException ex) when (ex.NetworkErrorType == NetworkErrorType.Timeout)
{
    // Request timed out
    logger.LogWarning("Request timed out");
}
catch (AcdcClientException ex)
{
    // 4xx — bad request, not found, etc.
    if (ex.RetryAfter.HasValue)
        await Task.Delay(ex.RetryAfter.Value);
}
```

## Migration from Raw HttpClient

**Before** (raw HttpClient):
```csharp
services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Manual token management, no caching, no structured errors...
```

**After** (CSharp-ACDC):
```csharp
services.AddAcdcHttpClient(b => b
    .WithAuth(auth =>
    {
        auth.RefreshEndpoint = "https://auth.example.com/token";
        auth.ClientId = "my-client";
    })
    .WithCache(cache => cache.Duration = TimeSpan.FromMinutes(5))
    .WithBaseAddress(new Uri("https://api.example.com"))
    .WithTimeout(TimeSpan.FromSeconds(10)));

// Auth, caching, logging, error handling — all automatic
```

## Per-Request Options

Override behavior on individual requests using `HttpRequestMessage.Options`:

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
request.Options.Set(AcdcRequestOptions.SkipCache, true);    // Bypass cache
request.Options.Set(AcdcRequestOptions.SkipAuth, true);     // Skip auth header
request.Options.Set(AcdcRequestOptions.CacheMaxAge, TimeSpan.FromSeconds(30)); // Custom TTL
```

## Named Clients

Register multiple independent ACDC clients for different downstream services:

```csharp
services.AddAcdcHttpClient("service-a", b => b
    .WithBaseAddress(new Uri("https://service-a.example.com"))
    .WithAuth(auth => { auth.RefreshEndpoint = "..."; auth.ClientId = "a"; }));

services.AddAcdcHttpClient("service-b", b => b
    .WithBaseAddress(new Uri("https://service-b.example.com"))
    .WithCache(cache => cache.Duration = TimeSpan.FromMinutes(10)));

// Resolve by key
var clientA = sp.GetRequiredKeyedService<AcdcHttpClient>("service-a");
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes with tests
4. Submit a pull request

## License

[MIT](LICENSE)
