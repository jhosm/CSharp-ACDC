# Tasks: Add Builder Pattern and DI Composition Root

## 1. Composite Options
- [x] 1.1 Create `AcdcClientOptions` record aggregating `AcdcAuthOptions`, `AcdcCacheOptions`, `AcdcLoggingOptions`, `AcdcDeduplicationOptions` sub-records, plus `BaseAddress` (Uri?), `Timeout` (TimeSpan?), and `ClientName` (string, default `"acdc"`)
- [x] 1.2 Add `IConfiguration` binding support -- ensure all option records have parameterless constructors and settable properties for `IConfiguration.GetSection("Acdc").Bind(options)` compatibility

## 2. Builder
- [x] 2.1 Implement `AcdcClientBuilder` as immutable record with private constructor and `static AcdcClientBuilder Create()` factory method
- [x] 2.2 Implement `WithAuth(Action<AcdcAuthOptions>)` -- applies auth configuration delegate, returns new builder instance
- [x] 2.3 Implement `WithCache(Action<AcdcCacheOptions>)` -- applies cache configuration delegate, returns new builder instance
- [x] 2.4 Implement `WithLogging(Action<AcdcLoggingOptions>)` -- applies logging configuration delegate, returns new builder instance
- [x] 2.5 Implement `WithCustomHandler<T>()` where `T : DelegatingHandler` -- adds handler type to custom handler list, returns new builder instance
- [x] 2.6 Implement `WithTimeout(TimeSpan)` and `WithBaseAddress(Uri)` -- returns new builder instances
- [x] 2.7 Implement `Build()` with validation -- verify required options are consistent (e.g., auth refresh endpoint is a valid URI when auth is configured), return `AcdcHttpClient`

## 3. DI Extension
- [x] 3.1 Implement `AddAcdcHttpClient(this IServiceCollection services)` overload -- zero-config default registration with LoggingHandler + ErrorHandler + DeduplicationHandler only
- [x] 3.2 Implement `AddAcdcHttpClient(this IServiceCollection services, Func<AcdcClientBuilder, AcdcClientBuilder> configure)` overload -- allows fluent builder configuration (uses Func instead of Action for immutable builder compatibility)
- [x] 3.3 Implement `AddAcdcHttpClient(this IServiceCollection services, IConfiguration configuration)` overload -- binds from `IConfiguration` section
- [x] 3.4 Register handler pipeline in correct order: Logging -> Error -> Cancellation -> Auth -> Cache -> Custom -> Dedup
- [x] 3.5 Register supporting services with appropriate lifetimes: `ITokenProvider` (Singleton), `IAcdcCacheManager` (Singleton), `ActiveRequestTracker` (Singleton), `BackoffManager` (Singleton), `AcdcAuthManager` (Singleton)
- [x] 3.6 Implement optional handler registration -- skip AuthHandler when `AcdcAuthOptions` is null/not configured; skip CacheHandler when `AcdcCacheOptions` is null/not configured
- [x] 3.7 Support named HttpClient instances -- use `ClientName` from options (default `"acdc"`) so multiple independent ACDC clients can coexist

## 4. Client Wrapper
- [x] 4.1 Implement `AcdcHttpClient` wrapping `HttpClient` with `.Auth` (AcdcAuthManager), `.Cache` (IAcdcCacheManager), `.CancelAll()` (delegates to `ActiveRequestTracker`)
- [x] 4.2 Implement `IDisposable` -- dispose only the wrapper state, NOT the underlying `HttpClient` (it is managed by `IHttpClientFactory`)
- [x] 4.3 Delegate core HTTP methods (`GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync`, `SendAsync`) to the underlying `HttpClient`

## 5. Tests
- [x] 5.1 Test builder immutability -- each `With*()` call returns a new instance; original builder is unchanged
- [x] 5.2 Test zero-config default produces working client -- `AddAcdcHttpClient()` with no configuration resolves and can send requests
- [x] 5.3 Test handler ordering verification -- use reflection or `IHttpClientFactory` inspection to verify handlers are registered in correct order: Logging -> Error -> Cancellation -> Auth -> Cache -> Custom -> Dedup
- [x] 5.4 Test custom handler insertion at correct position -- verify custom handlers appear after CacheHandler and before DeduplicationHandler
- [x] 5.5 Test optional handler omission -- verify AuthHandler is not in pipeline when auth is not configured; same for CacheHandler
- [x] 5.6 Test DI registration resolves all services -- build `ServiceProvider`, resolve `AcdcHttpClient`, `AcdcAuthManager`, `IAcdcCacheManager`, `ActiveRequestTracker` without exception
- [x] 5.7 Test configuration binding from `IConfiguration` -- verify `AcdcClientOptions` populated correctly from JSON configuration source
- [x] 5.8 Test named client support -- register two ACDC clients with different names and verify they resolve independently with different configurations
- [x] 5.9 Test public API surface (reflection-based) -- enumerate all public types in the `CSharpAcdc` assembly, assert against expected set, fail if unexpected types are exported or expected types are missing
