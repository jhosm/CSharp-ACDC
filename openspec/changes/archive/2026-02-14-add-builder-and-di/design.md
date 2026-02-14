# Design: Add Builder Pattern and DI Composition Root

## Context

This is the composition root for the CSharp-ACDC pipeline. It must integrate 6 handler types (Logging, Error, Cancellation, Auth, Cache, Deduplication) plus user-defined custom handlers into a single `IHttpClientFactory`-managed pipeline, with DI registration via `IServiceCollection` and configuration via `IOptions<T>`. The builder and DI extension are the primary public API surface that users interact with -- all other types (handlers, token providers, cache managers) are implementation details that users configure through the builder.

This design must support the "progressive disclosure" principle from Dart-ACDC: zero-config works out of the box, but advanced users can configure every aspect of the pipeline.

## Goals

- **Progressive disclosure:** `services.AddAcdcHttpClient()` with zero arguments produces a working client with sensible defaults (logging, error handling, deduplication). Adding auth or caching requires explicit opt-in via `WithAuth()` / `WithCache()`.
- **Immutable builder:** Each `With*()` method returns a new builder instance, enabling safe reuse of partially-configured builders (e.g., a "base" builder that is extended differently for different named clients).
- **Correct handler ordering enforced automatically:** Users cannot accidentally mis-order handlers. The `AddAcdcHttpClient()` extension method always registers handlers in the specified order regardless of the order `With*()` methods are called on the builder.
- **Idiomatic .NET:** Follows `IServiceCollection` extension method conventions, `IOptions<T>` configuration pattern, and `IHttpClientFactory` named client pattern used by other .NET libraries (Polly, Refit, etc.).

## Non-Goals

- **No runtime handler reordering:** Once the pipeline is built, the handler order cannot be changed. There is no API to insert a handler at a specific position other than the designated custom handler slot (position 6).
- **No dynamic pipeline modification after build:** `AcdcHttpClient` is not reconfigurable after construction. To change configuration, create a new client.
- **No handler removal API:** Individual built-in handlers cannot be selectively removed (except via the optional handler mechanism -- auth and cache are omitted when not configured).
- **No `HttpClientFactory` replacement:** This design works WITH `IHttpClientFactory`, not as a replacement. Users who need raw `HttpClient` access can still use `IHttpClientFactory` directly.

## Decisions

### 1. Immutable Builder via C# Record with `with` Expressions

**Decision:** Implement `AcdcClientBuilder` as a C# `record` type. Each `With*()` method returns a new instance using the record's `with` expression, leaving the original unchanged.

**Why:** Immutability prevents subtle bugs where shared builder state is accidentally mutated. Records provide value equality, which simplifies testing. The `with` expression is more concise than manual cloning and is a well-understood C# pattern.

**Alternatives considered:**
- Mutable fluent builder (classic pattern): Rejected because mutation enables aliasing bugs and is not thread-safe.
- Frozen/builder pair (separate `AcdcClientConfig` and `AcdcClientBuilder`): Rejected as over-engineered for the number of configuration options.

### 2. Handler Order Enforced by `AddAcdcHttpClient()`, Not by Individual Handlers

**Decision:** The `AddAcdcHttpClient()` extension method is the sole authority on handler ordering. Individual handlers have no knowledge of their position in the pipeline. The order is hard-coded:

```
1. LoggingHandler       -- logs all requests/responses including those modified by later handlers
2. ErrorHandler         -- converts HTTP error responses to typed exceptions
3. CancellationHandler  -- checks CancellationToken before forwarding
4. AuthHandler          -- injects auth token, handles 401 retry (optional)
5. CacheHandler         -- serves from cache, stores responses (optional)
6. Custom handlers      -- user-defined handlers, in registration order
7. DeduplicationHandler -- deduplicates identical GET requests
```

**Why:** Centralizing order in one place prevents accidental mis-ordering. The Dart-ACDC interceptor chain order is critical for correctness (e.g., logging must be outermost to capture the final request; dedup must be innermost to catch duplicate calls after all transformations).

**Alternatives considered:**
- Handler self-ordering via `[Order(n)]` attribute: Rejected because it scatters ordering knowledge across handler classes and makes the pipeline order implicit.
- User-specified ordering: Rejected because incorrect ordering causes subtle bugs (e.g., auth before logging hides auth failures from logs).

### 3. Custom Handlers at Position 6 (After Cache, Before Dedup)

**Decision:** User-defined handlers registered via `WithCustomHandler<T>()` are inserted at position 6, after the CacheHandler and before the DeduplicationHandler. Multiple custom handlers are registered in the order they are added to the builder.

**Why:** This matches Dart-ACDC's custom interceptor position. Custom handlers at this position can:
- See the authenticated request (after AuthHandler adds the Bearer token)
- Interact with cached responses (after CacheHandler)
- Be deduplicated (before DeduplicationHandler)

### 4. Named HttpClient Support (Default: `"acdc"`)

**Decision:** The named HttpClient defaults to `"acdc"` but is configurable via `AcdcClientOptions.ClientName`. Multiple ACDC clients with different configurations can coexist in the same `IServiceCollection`.

**Why:** Real-world applications often call multiple APIs with different auth credentials, cache policies, or base addresses. Named clients are the standard `IHttpClientFactory` pattern for this use case.

**Usage example:**
```csharp
services.AddAcdcHttpClient("api-a", builder => builder
    .WithBaseAddress(new Uri("https://api-a.example.com"))
    .WithAuth(auth => auth.ClientId = "client-a"));

services.AddAcdcHttpClient("api-b", builder => builder
    .WithBaseAddress(new Uri("https://api-b.example.com"))
    .WithAuth(auth => auth.ClientId = "client-b"));
```

### 5. `AcdcHttpClient` as Thin Wrapper, Not Full HttpClient Replacement

**Decision:** `AcdcHttpClient` wraps `HttpClient` and delegates all HTTP operations. It is NOT a subclass of `HttpClient` and does not reimplement HTTP semantics.

**Why:** `HttpClient` is a complex class with many overloads. Wrapping (composition) rather than inheriting avoids fragile base class problems and ensures all `HttpClient` features work correctly. The wrapper adds only ACDC-specific surface area: `.Auth`, `.Cache`, `.CancelAll()`.

**Alternatives considered:**
- Inherit from `HttpClient`: Rejected because `HttpClient` is not designed for inheritance; its `SendAsync` is not consistently virtual across all overloads.
- Return raw `HttpClient` with extension methods: Rejected because extension methods cannot hold state (auth manager, cache manager references).

### 6. Dual Configuration: Fluent API + `IConfiguration` Binding

**Decision:** Support both programmatic configuration via the fluent builder and declarative configuration via `IConfiguration` binding (appsettings.json). When both are used, fluent API values override `IConfiguration` values.

**Why:** Fluent API is better for compile-time-known configuration and IDE discoverability. `IConfiguration` binding is better for environment-specific settings (connection strings, endpoints). Supporting both follows .NET conventions (ASP.NET Core's `AddAuthentication`, `AddDbContext`, etc. all support this dual pattern).

**Precedence:** Fluent API > `IConfiguration` > defaults. This is implemented by first binding `IConfiguration`, then applying the fluent API delegate on top.

### 7. Optional Handlers Based on Configuration

**Decision:** AuthHandler is omitted from the pipeline when no `AcdcAuthOptions` are configured. CacheHandler is omitted when no `AcdcCacheOptions` are configured. LoggingHandler, ErrorHandler, CancellationHandler, and DeduplicationHandler are always included.

**Why:** Including AuthHandler without configuration would cause runtime errors (no token provider, no refresh endpoint). Including CacheHandler without configuration would add overhead with no benefit. The always-included handlers (logging, error, cancellation, dedup) are useful even without explicit configuration and have sensible zero-config defaults.

**Detection:** The `AddAcdcHttpClient()` method checks whether `AcdcAuthOptions` and `AcdcCacheOptions` are null (not configured) or populated (configured) on the resolved `AcdcClientOptions` after both `IConfiguration` binding and fluent API have been applied.

## Risks / Trade-offs

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Handler ordering change breaks behavior silently | Low | High | Handler ordering verification test (task 5.3) fails if order changes; test runs in CI on every PR |
| Public API surface drift -- internal types accidentally exposed | Medium | Medium | Reflection-based surface test (task 5.9) enumerates all public types and asserts against expected set |
| Named client name collision with non-ACDC HttpClients | Low | Low | Default name `"acdc"` is distinctive; users can override via `ClientName` option |
| `IOptions<T>` validation runs too late (at first resolution, not at registration) | Medium | Low | `Build()` method performs eager validation; `AddAcdcHttpClient()` registers `IValidateOptions<AcdcClientOptions>` for startup validation via `ValidateOnStart()` |
| Immutable builder creates garbage from `with` expressions | Low | Low | Builder is used once at startup, not in hot paths; allocation is negligible |

## Open Questions

None. The design follows well-established .NET patterns (`IHttpClientFactory`, `IServiceCollection`, `IOptions<T>`) and maps directly from Dart-ACDC's builder and interceptor chain architecture.
