# Change: Add Builder Pattern and DI Composition Root

## Why

Individual handlers (P2-P6) need to be composed into a working HTTP client pipeline. Without a composition root, users would have to manually register each handler, manage pipeline ordering, and wire up supporting services -- all error-prone tasks that violate the library's "progressive disclosure" and "zero-config works" design principles. The builder pattern provides a fluent, discoverable API for configuring the client, while the DI extension method integrates with ASP.NET Core's `IServiceCollection` following idiomatic .NET patterns. This is P7 -- the composition root that ties all handlers into a cohesive API and sits on the critical path: P2 -> P5 -> P7 -> P8.

## What Changes

### Composite Options
- **`AcdcClientOptions`** record -- composite configuration aggregating auth, cache, logging, and dedup sub-options. Uses sub-records (`AcdcAuthOptions`, `AcdcCacheOptions`, `AcdcLoggingOptions`, `AcdcDeduplicationOptions`) for each feature area. Supports both fluent API configuration and `IConfiguration` binding (appsettings.json) via `IOptions<AcdcClientOptions>`.

### Builder
- **`AcdcClientBuilder`** -- immutable fluent API using record `with` expressions. Each `With*()` method returns a new builder instance, enabling safe reuse of partially-configured builders:
  - `WithAuth(Action<AcdcAuthOptions>)` -- configure authentication (token provider, refresh strategy, backoff)
  - `WithCache(Action<AcdcCacheOptions>)` -- configure caching (FusionCache, SWR, ETag)
  - `WithLogging(Action<AcdcLoggingOptions>)` -- configure structured logging (verbosity, redaction)
  - `WithCustomHandler<T>()` -- register user-defined `DelegatingHandler` types (inserted at position 6 in pipeline, after Cache, before Dedup)
  - `WithTimeout(TimeSpan)` -- configure default HTTP timeout
  - `WithBaseAddress(Uri)` -- configure base address for all requests
  - `Build()` -- validates configuration and returns `AcdcHttpClient`

### DI Extension
- **`AddAcdcHttpClient()`** extension on `IServiceCollection` -- registers named HttpClient with handler pipeline in the correct order:
  1. LoggingHandler
  2. ErrorHandler
  3. CancellationHandler
  4. AuthHandler (optional -- omitted when no auth options configured)
  5. CacheHandler (optional -- omitted when no cache options configured)
  6. Custom handlers (user-provided, in registration order)
  7. DeduplicationHandler
- Registers all supporting services: `ITokenProvider`, `IAcdcCacheManager`, `ActiveRequestTracker`, `BackoffManager`, `AcdcAuthManager`, etc.
- Supports named HttpClient instances (default name: `"acdc"`, configurable) for multiple independent ACDC clients in the same application.

### Client Wrapper
- **`AcdcHttpClient`** -- thin wrapper around `HttpClient` obtained from `IHttpClientFactory`:
  - Exposes `.Auth` property (`AcdcAuthManager`) for logout, force-refresh flows
  - Exposes `.Cache` property (`IAcdcCacheManager`) for cache invalidation, warm-up
  - Exposes `.CancelAll()` method (delegates to `ActiveRequestTracker`)
  - Delegates HTTP operations (`GetAsync`, `PostAsync`, etc.) to the underlying `HttpClient`
  - Implements `IDisposable`

### Configuration Binding
- **`IOptions<AcdcClientOptions>`** + **`IConfiguration`** binding support -- allows configuring ACDC from appsettings.json:
  ```json
  {
    "Acdc": {
      "BaseAddress": "https://api.example.com",
      "Timeout": "00:00:30",
      "Auth": { "RefreshEndpoint": "...", "ClientId": "..." },
      "Cache": { "DefaultTtl": "00:05:00" }
    }
  }
  ```

### Tests
- Public API surface validation test (reflection-based -- verifies all expected types are exported and no internal types leak)
- Handler ordering verification test (validates pipeline order matches specification)
- Builder immutability tests
- DI registration resolution tests

## Impact

- **Affected specs:** builder-and-di (new)
- **Depends on:** P2 (exceptions), P3 (LoggingHandler), P4 (CancellationHandler + DeduplicationHandler), P5 (AuthHandler + auth system), P6 (CacheHandler + cache system)
- **Enables:** P8 (integration tests), P10 (advanced configuration), P11 (documentation/samples)
- **Critical path:** P2 -> P5 -> P7 -> P8

### Files to be created

**Source files:**
- `src/CSharpAcdc/Configuration/AcdcClientOptions.cs`
- `src/CSharpAcdc/Builder/AcdcClientBuilder.cs`
- `src/CSharpAcdc/Client/AcdcHttpClient.cs`
- `src/CSharpAcdc/Extensions/ServiceCollectionExtensions.cs`

**Test files:**
- `tests/CSharpAcdc.Tests/Builder/AcdcClientBuilderTests.cs`
- `tests/CSharpAcdc.Tests/Extensions/ServiceCollectionExtensionsTests.cs`
- `tests/CSharpAcdc.Tests/PublicApiSurfaceTests.cs`
