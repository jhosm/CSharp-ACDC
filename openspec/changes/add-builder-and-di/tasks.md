# Tasks: Add Builder Pattern and DI Composition Root

## 1. Composite Options
- [ ] 1.1 Create `AcdcClientOptions` record aggregating `AcdcAuthOptions`, `AcdcCacheOptions`, `AcdcLoggingOptions`, `AcdcDeduplicationOptions` sub-records, plus `BaseAddress` (Uri?), `Timeout` (TimeSpan?), and `ClientName` (string, default `"acdc"`)
- [ ] 1.2 Add `IConfiguration` binding support -- ensure all option records have parameterless constructors and settable properties for `IConfiguration.GetSection("Acdc").Bind(options)` compatibility

## 2. Builder
- [ ] 2.1 Implement `AcdcClientBuilder` as immutable record with private constructor and `static AcdcClientBuilder Create()` factory method
- [ ] 2.2 Implement `WithAuth(Action<AcdcAuthOptions>)` -- applies auth configuration delegate, returns new builder instance
- [ ] 2.3 Implement `WithCache(Action<AcdcCacheOptions>)` -- applies cache configuration delegate, returns new builder instance
- [ ] 2.4 Implement `WithLogging(Action<AcdcLoggingOptions>)` -- applies logging configuration delegate, returns new builder instance
- [ ] 2.5 Implement `WithCustomHandler<T>()` where `T : DelegatingHandler` -- adds handler type to custom handler list, returns new builder instance
- [ ] 2.6 Implement `WithTimeout(TimeSpan)` and `WithBaseAddress(Uri)` -- returns new builder instances
- [ ] 2.7 Implement `Build()` with validation -- verify required options are consistent (e.g., auth refresh endpoint is a valid URI when auth is configured), return `AcdcHttpClient`

## 3. DI Extension
- [ ] 3.1 Implement `AddAcdcHttpClient(this IServiceCollection services)` overload -- zero-config default registration with LoggingHandler + ErrorHandler + DeduplicationHandler only
- [ ] 3.2 Implement `AddAcdcHttpClient(this IServiceCollection services, Action<AcdcClientBuilder> configure)` overload -- allows fluent builder configuration
- [ ] 3.3 Implement `AddAcdcHttpClient(this IServiceCollection services, IConfiguration configuration)` overload -- binds from `IConfiguration` section
- [ ] 3.4 Register handler pipeline in correct order: Logging -> Error -> Cancellation -> Auth -> Cache -> Custom -> Dedup
- [ ] 3.5 Register supporting services with appropriate lifetimes: `ITokenProvider` (Singleton), `IAcdcCacheManager` (Singleton), `ActiveRequestTracker` (Singleton), `BackoffManager` (Singleton), `AcdcAuthManager` (Scoped)
- [ ] 3.6 Implement optional handler registration -- skip AuthHandler when `AcdcAuthOptions` is null/not configured; skip CacheHandler when `AcdcCacheOptions` is null/not configured
- [ ] 3.7 Support named HttpClient instances -- use `ClientName` from options (default `"acdc"`) so multiple independent ACDC clients can coexist

## 4. Client Wrapper
- [ ] 4.1 Implement `AcdcHttpClient` wrapping `HttpClient` with `.Auth` (AcdcAuthManager), `.Cache` (IAcdcCacheManager), `.CancelAll()` (delegates to `ActiveRequestTracker`)
- [ ] 4.2 Implement `IDisposable` -- dispose only the wrapper state, NOT the underlying `HttpClient` (it is managed by `IHttpClientFactory`)
- [ ] 4.3 Delegate core HTTP methods (`GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync`, `SendAsync`) to the underlying `HttpClient`

## 5. Tests
- [ ] 5.1 Test builder immutability -- each `With*()` call returns a new instance; original builder is unchanged
- [ ] 5.2 Test zero-config default produces working client -- `AddAcdcHttpClient()` with no configuration resolves and can send requests
- [ ] 5.3 Test handler ordering verification -- use reflection or `IHttpClientFactory` inspection to verify handlers are registered in correct order: Logging -> Error -> Cancellation -> Auth -> Cache -> Custom -> Dedup
- [ ] 5.4 Test custom handler insertion at correct position -- verify custom handlers appear after CacheHandler and before DeduplicationHandler
- [ ] 5.5 Test optional handler omission -- verify AuthHandler is not in pipeline when auth is not configured; same for CacheHandler
- [ ] 5.6 Test DI registration resolves all services -- build `ServiceProvider`, resolve `AcdcHttpClient`, `AcdcAuthManager`, `IAcdcCacheManager`, `ActiveRequestTracker` without exception
- [ ] 5.7 Test configuration binding from `IConfiguration` -- verify `AcdcClientOptions` populated correctly from JSON configuration source
- [ ] 5.8 Test named client support -- register two ACDC clients with different names and verify they resolve independently with different configurations
- [ ] 5.9 Test public API surface (reflection-based) -- enumerate all public types in the `CSharpAcdc` assembly, assert against expected set, fail if unexpected types are exported or expected types are missing
