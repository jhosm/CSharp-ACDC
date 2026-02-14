# builder-and-di Specification

## Purpose
TBD - created by archiving change add-builder-and-di. Update Purpose after archive.
## Requirements
### Requirement: Fluent Builder
`AcdcClientBuilder` SHALL provide a fluent, immutable API for configuring the HTTP client pipeline. Each `With*()` method SHALL return a new builder instance without modifying the original, enabling safe reuse of partially-configured builders.

#### Scenario: Builder immutability
- **WHEN** a user calls `WithAuth(...)` on an existing builder instance
- **THEN** a new builder instance is returned with the auth configuration applied
- **AND** the original builder instance remains unchanged (no auth configuration)

#### Scenario: Builder method chaining
- **WHEN** a user chains multiple `With*()` calls (e.g., `builder.WithAuth(...).WithCache(...).WithTimeout(...)`)
- **THEN** the final builder instance contains all configured options
- **AND** each intermediate builder instance contains only the options configured up to that point

### Requirement: Zero-Config Default
`AcdcClientBuilder.Build()` SHALL produce a working HTTP client with default settings when no configuration methods are called. The default pipeline SHALL include LoggingHandler, ErrorHandler, CancellationHandler, and DeduplicationHandler. AuthHandler and CacheHandler SHALL be omitted from the default pipeline.

#### Scenario: Zero-config client creation
- **WHEN** a user calls `services.AddAcdcHttpClient()` with no configuration
- **THEN** the resolved `AcdcHttpClient` SHALL be able to send HTTP requests
- **AND** the handler pipeline SHALL contain LoggingHandler, ErrorHandler, CancellationHandler, and DeduplicationHandler
- **AND** the handler pipeline SHALL NOT contain AuthHandler or CacheHandler

#### Scenario: Default timeout
- **WHEN** a user creates a client with no explicit timeout configuration
- **THEN** the underlying `HttpClient` SHALL use the .NET default timeout (100 seconds)

### Requirement: Handler Pipeline Order
`AddAcdcHttpClient()` SHALL register handlers in the exact order: LoggingHandler, ErrorHandler, CancellationHandler, AuthHandler (if configured), CacheHandler (if configured), custom handlers (in registration order), DeduplicationHandler. This order SHALL be enforced regardless of the order in which `With*()` methods are called on the builder.

#### Scenario: Full pipeline ordering
- **WHEN** a user configures auth, cache, and custom handlers
- **THEN** the handler pipeline order SHALL be: LoggingHandler -> ErrorHandler -> CancellationHandler -> AuthHandler -> CacheHandler -> custom handlers -> DeduplicationHandler

#### Scenario: Ordering independent of configuration order
- **WHEN** a user calls `builder.WithCache(...).WithAuth(...)` (cache configured before auth)
- **THEN** AuthHandler SHALL still appear before CacheHandler in the pipeline

#### Scenario: Pipeline without optional handlers
- **WHEN** a user configures only custom handlers (no auth, no cache)
- **THEN** the handler pipeline order SHALL be: LoggingHandler -> ErrorHandler -> CancellationHandler -> custom handlers -> DeduplicationHandler

### Requirement: Custom Handler Registration
`WithCustomHandler<T>()` SHALL insert user-defined `DelegatingHandler` types at position 6 in the pipeline (after CacheHandler, before DeduplicationHandler). Multiple custom handlers SHALL be registered in the order they are added to the builder.

#### Scenario: Single custom handler
- **WHEN** a user registers one custom handler via `WithCustomHandler<MyHandler>()`
- **THEN** `MyHandler` SHALL appear after CacheHandler (or after CancellationHandler if cache is not configured) and before DeduplicationHandler in the pipeline

#### Scenario: Multiple custom handlers in order
- **WHEN** a user registers `WithCustomHandler<HandlerA>()` then `WithCustomHandler<HandlerB>()`
- **THEN** HandlerA SHALL appear before HandlerB in the pipeline
- **AND** both SHALL appear after CacheHandler and before DeduplicationHandler

#### Scenario: Custom handler type constraint
- **WHEN** a user attempts to register a type that does not extend `DelegatingHandler`
- **THEN** a compile-time error SHALL occur (enforced by generic type constraint `where T : DelegatingHandler`)

### Requirement: Optional Handlers
The builder SHALL omit AuthHandler from the pipeline when no auth options are configured, and SHALL omit CacheHandler when no cache options are configured. LoggingHandler, ErrorHandler, CancellationHandler, and DeduplicationHandler SHALL always be included in the pipeline regardless of configuration.

#### Scenario: Auth handler omitted when unconfigured
- **WHEN** a user creates a client without calling `WithAuth()`
- **THEN** the handler pipeline SHALL NOT contain AuthHandler
- **AND** the pipeline SHALL still contain LoggingHandler, ErrorHandler, CancellationHandler, and DeduplicationHandler

#### Scenario: Cache handler omitted when unconfigured
- **WHEN** a user creates a client without calling `WithCache()`
- **THEN** the handler pipeline SHALL NOT contain CacheHandler

#### Scenario: Both optional handlers included when configured
- **WHEN** a user calls both `WithAuth(...)` and `WithCache(...)`
- **THEN** the handler pipeline SHALL contain both AuthHandler and CacheHandler in the correct positions

### Requirement: DI Registration
`AddAcdcHttpClient()` SHALL register the named HttpClient, all pipeline handlers, and all supporting services (`ITokenProvider`, `IAcdcCacheManager`, `ActiveRequestTracker`, `BackoffManager`, `AcdcAuthManager`, etc.) with `IServiceCollection`. All registrations SHALL use appropriate service lifetimes.

#### Scenario: Service resolution succeeds
- **WHEN** a user calls `services.AddAcdcHttpClient(builder => builder.WithAuth(...).WithCache(...))`
- **AND** builds the `ServiceProvider`
- **THEN** resolving `AcdcHttpClient`, `AcdcAuthManager`, `IAcdcCacheManager`, and `ActiveRequestTracker` SHALL succeed without exceptions

#### Scenario: Singleton services are shared
- **WHEN** `AcdcHttpClient` is resolved multiple times from the same `ServiceProvider`
- **THEN** singleton services (`ITokenProvider`, `ActiveRequestTracker`, `BackoffManager`) SHALL return the same instance each time

#### Scenario: Zero-config DI registration
- **WHEN** a user calls `services.AddAcdcHttpClient()` with no configuration
- **THEN** the `ServiceProvider` SHALL resolve `AcdcHttpClient` without exceptions
- **AND** resolving auth-specific services (`AcdcAuthManager`) SHALL NOT be required (they may not be registered when auth is unconfigured)

### Requirement: Configuration Binding
`AcdcClientOptions` SHALL support both fluent API configuration via the builder and `IConfiguration` binding from appsettings.json. When both are used, fluent API values SHALL take precedence over `IConfiguration` values.

#### Scenario: Configuration from appsettings.json
- **WHEN** `IConfiguration` contains an `"Acdc"` section with `"BaseAddress": "https://api.example.com"` and `"Timeout": "00:00:30"`
- **AND** the user calls `services.AddAcdcHttpClient(configuration.GetSection("Acdc"))`
- **THEN** the resolved `AcdcHttpClient` SHALL have `BaseAddress` set to `https://api.example.com` and timeout set to 30 seconds

#### Scenario: Fluent API overrides IConfiguration
- **WHEN** `IConfiguration` sets `BaseAddress` to `https://config.example.com`
- **AND** the user calls `builder.WithBaseAddress(new Uri("https://fluent.example.com"))`
- **THEN** the resolved `AcdcHttpClient` SHALL have `BaseAddress` set to `https://fluent.example.com`

#### Scenario: Nested configuration binding
- **WHEN** `IConfiguration` contains `"Acdc": { "Auth": { "RefreshEndpoint": "https://auth.example.com/token", "ClientId": "my-client" } }`
- **THEN** the resolved `AcdcAuthOptions` SHALL have `RefreshEndpoint` set to `"https://auth.example.com/token"` and `ClientId` set to `"my-client"`

### Requirement: Client Wrapper
`AcdcHttpClient` SHALL expose `.Auth` (`AcdcAuthManager`) for auth management, `.Cache` (`IAcdcCacheManager`) for cache management, and `.CancelAll()` for cancelling all active requests. It SHALL delegate HTTP operations (`GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync`, `SendAsync`) to the underlying `HttpClient` obtained from `IHttpClientFactory`.

#### Scenario: Auth manager access
- **WHEN** a user accesses `client.Auth` on an auth-configured `AcdcHttpClient`
- **THEN** an `AcdcAuthManager` instance SHALL be returned
- **AND** the user SHALL be able to call `client.Auth.LogoutAsync()` and `client.Auth.ForceRefreshAsync()`

#### Scenario: Cache manager access
- **WHEN** a user accesses `client.Cache` on a cache-configured `AcdcHttpClient`
- **THEN** an `IAcdcCacheManager` instance SHALL be returned

#### Scenario: Cancel all requests
- **WHEN** a user calls `client.CancelAll()`
- **THEN** all active requests tracked by `ActiveRequestTracker` SHALL be cancelled

#### Scenario: HTTP delegation
- **WHEN** a user calls `client.GetAsync("https://api.example.com/data")`
- **THEN** the request SHALL pass through the configured handler pipeline
- **AND** the response SHALL be returned from the underlying `HttpClient`

#### Scenario: Dispose does not dispose HttpClient
- **WHEN** a user disposes `AcdcHttpClient`
- **THEN** the underlying `HttpClient` SHALL NOT be disposed (it is managed by `IHttpClientFactory`)

### Requirement: Named Clients
The builder SHALL support named HttpClient instances (default name: `"acdc"`) allowing multiple independent ACDC clients in the same application. Each named client SHALL have its own handler pipeline, configuration, and supporting services.

#### Scenario: Default client name
- **WHEN** a user calls `services.AddAcdcHttpClient()` without specifying a name
- **THEN** the HttpClient SHALL be registered with the name `"acdc"`

#### Scenario: Custom client name
- **WHEN** a user calls `services.AddAcdcHttpClient("my-api", builder => ...)`
- **THEN** the HttpClient SHALL be registered with the name `"my-api"`

#### Scenario: Multiple independent clients
- **WHEN** a user registers two ACDC clients with names `"api-a"` and `"api-b"` with different auth configurations
- **THEN** resolving `"api-a"` SHALL use the auth configuration from `"api-a"` registration
- **AND** resolving `"api-b"` SHALL use the auth configuration from `"api-b"` registration
- **AND** the two clients SHALL be fully independent (separate token providers, separate cache managers)

### Requirement: Public API Surface
The library SHALL export only intentionally public types. A reflection-based surface test SHALL verify that all expected types are present and no unexpected types are accidentally exposed.

#### Scenario: Expected types are public
- **WHEN** the `CSharpAcdc` assembly is inspected via reflection
- **THEN** all types listed in the public API contract (e.g., `AcdcHttpClient`, `AcdcClientBuilder`, `AcdcClientOptions`, `AcdcAuthOptions`, `AcdcCacheOptions`, exception types, handler types) SHALL be present as public types

#### Scenario: No unexpected public types
- **WHEN** the `CSharpAcdc` assembly is inspected via reflection
- **THEN** no types beyond the documented public API contract SHALL be exported as public
- **AND** internal implementation types (e.g., `BackoffManager`, `UserIdExtractor`, internal handler state) SHALL NOT be public

