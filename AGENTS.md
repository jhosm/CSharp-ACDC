<!-- OPENSPEC:START -->
# OpenSpec Instructions

These instructions are for AI assistants working in this project.

Always open `@/openspec/AGENTS.md` when the request:
- Mentions planning or proposals (words like proposal, spec, change, plan)
- Introduces new capabilities, breaking changes, architecture shifts, or big performance/security work
- Sounds ambiguous and you need the authoritative spec before coding

Use `@/openspec/AGENTS.md` to learn:
- How to create and apply change proposals
- Spec format and conventions
- Project structure and guidelines

Keep this managed block so 'openspec update' can refresh the instructions.

<!-- OPENSPEC:END -->

# CSharp-ACDC

**Server-only** C# port of **Dart-ACDC** (Authentication, Caching, Debugging, Client) — a production HTTP client library with auth, caching, logging, and structured error handling. Targets ASP.NET Core / .NET 8+.

## Status

Research/planning phase. No C# code written yet. Detailed analysis of the Dart source lives in `research/`. 11 OpenSpec change proposals (P1-P11) are planned — see roadmap below.

## Implementation Roadmap

### Resolved Decisions

- **Root namespace**: `CSharpAcdc`
- **Mock framework**: NSubstitute
- **Default timeout**: 5 seconds (matches Dart-ACDC)
- **`AcdcSecurityException`**: Skipped (server cert validation uses `HttpClientHandler` callback)

### Proposal Dependency Graph

```
P1: add-solution-scaffold
  |
  +---> P9: add-ci-and-nuget-publishing    (.github/ only — no code conflicts)
  |
P2: add-exceptions-and-error-handler
  |
  +---> P3: add-logging-handler           \
  +---> P4: add-cancellation-and-dedup     |-- ALL PARALLEL
  +---> P5: add-auth-system               |
  +---> P6: add-cache-system              /
         |
P7: add-builder-and-di  <-- waits for P3+P4+P5+P6
  |
  +---> P8:  add-integration-tests         \
  +---> P10: add-documentation-and-examples |-- PARALLEL
  +---> P11: add-nuget-package-metadata    /
```

### Proposal Summary

| # | Change ID | Layer | Size | Depends On | Parallel With |
|---|-----------|-------|------|------------|---------------|
| P1 | `add-solution-scaffold` | 0 | S | — | — |
| P2 | `add-exceptions-and-error-handler` | 1 | M | P1 | P9 |
| P3 | `add-logging-handler` | 2 | S | P2 | P4, P5, P6, P9 |
| P4 | `add-cancellation-and-dedup-handlers` | 2 | M | P2 | P3, P5, P6, P9 |
| P5 | `add-auth-system` | 2 | L | P2 | P3, P4, P6, P9 |
| P6 | `add-cache-system` | 2 | L | P2 | P3, P4, P5, P9 |
| P7 | `add-builder-and-di` | 3 | M | P3-P6 | P9 |
| P8 | `add-integration-tests` | 4 | M | P7 | P10, P11 |
| P9 | `add-ci-and-nuget-publishing` | 1 | M | P1 | P2-P8 |
| P10 | `add-documentation-and-examples` | 4 | M | P7 | P8, P11 |
| P11 | `add-nuget-package-metadata` | 4 | S | P7 | P8, P10 |

**Critical path**: P1 → P2 → P5 (largest) → P7 → P8

**Strategy**: Hybrid (layered foundation + parallel features). All NuGet packages are front-loaded in P1 via `Directory.Packages.props` to eliminate `.csproj` merge conflicts. Interfaces are designed alongside their consumers (exceptions with ErrorHandler, auth interfaces with AuthHandler) to prevent interface instability. Full details in `.claude/plans/playful-finding-octopus.md`.

## Prerequisites

- .NET 8+ SDK
- Redis (for integration tests with L2 cache — P8)
- `openspec` CLI (manages change proposals)
- `bd` CLI (issue tracking via beads)

## Commands

```bash
# OpenSpec — change proposal workflow
openspec list                              # List all proposals and status
openspec validate <change-id>              # Validate a proposal
openspec apply <change-id>                 # Apply a proposal (merges spec deltas)
openspec archive <change-id>               # Archive a completed proposal

# Beads — issue tracking
bd ready                                   # Find available work
bd show <id>                               # View issue details
bd update <id> --status in_progress        # Claim work
bd close <id>                              # Complete work
bd sync                                    # Sync with git
```

Once P1 scaffold lands:
```bash
dotnet build                               # Build solution
dotnet test                                # Run all tests
dotnet test tests/CSharpAcdc.Tests         # Unit tests only
dotnet test tests/CSharpAcdc.IntegrationTests  # Integration tests only
```

## Repository Structure

```
openspec/
  AGENTS.md                                # OpenSpec conventions and format rules
  project.md                               # Tech stack, code style, architecture patterns
  changes/                                 # Change proposals (P1-P11) — created as work begins
  specs/                                   # Current truth — populated as proposals are archived
research/
  01-architecture-and-interceptors.md      # Builder, interceptor chain, extension methods
  02-authentication-and-security.md        # Auth, token refresh, cert pinning, JWT
  03-caching-and-offline.md                # Two-tier cache, SWR, offline fallback
  04-exceptions-tests-dependencies.md      # Exceptions, tests, dependency mapping
  *-REVIEW.md                             # Cross-review corrections (incorporated into main docs)
.claude/
  commands/openspec/                       # Slash commands: /apply, /archive, /proposal
  plans/playful-finding-octopus.md         # Full proposal breakdown with rationale
```

After P1 lands, the source tree will be:
```
src/CSharpAcdc/                            # Library (namespace: CSharpAcdc)
  Exceptions/  Handlers/  Auth/  Cache/
  Logging/  Configuration/  Extensions/
  Builder/  Client/
tests/CSharpAcdc.Tests/                    # Unit tests (xUnit + NSubstitute)
tests/CSharpAcdc.IntegrationTests/         # Integration tests (WireMock.Net)
```

## Server-Only Scope

This port targets **server-side only** (ASP.NET Core). The following Dart-ACDC features are **excluded**:

| Excluded | Reason |
|----------|--------|
| OfflineInterceptor | Servers don't go offline. Use Polly resilience policies for transient downstream failures. |
| `connectivity_plus` / `INetworkInfo` | No connectivity monitoring needed on server. |
| `flutter_secure_storage` / device Keychain | Use in-memory or `IDistributedCache` (Redis) for token storage. |
| Encrypted disk cache (`EncryptedCacheStore`) | Use `IDistributedCache` (Redis) instead. Redis handles its own security. |
| `path_provider` / file paths | No file-based cache. Redis for persistence. |
| MAUI / Xamarin / mobile platform APIs | Server-only — no mobile targets. |

## Source Architecture (Dart-ACDC)

### Interceptor Chain (order is critical)

```
Dart source (8 interceptors):              C# server port (7 handlers):
1. LoggingInterceptor                      1. LoggingHandler
2. ErrorInterceptor                        2. ErrorHandler
3. CancellationInterceptor                 3. CancellationHandler
4. OfflineInterceptor        ← REMOVED     (not ported)
5. AuthInterceptor                         4. AuthHandler
6. CacheInterceptor                        5. CacheHandler
7. Custom interceptors                     6. Custom handlers
8. DeduplicationInterceptor                7. DeduplicationHandler
```

### Key Modules

- **Builder** — immutable (`_copyWith`), progressive disclosure, zero-config default
- **Auth** — `TokenProvider` abstraction, `TokenRefreshStrategy` (OAuth 2.1 + custom), `BackoffManager` (exponential 1s→30s clamped), concurrent refresh queue with `Completer`
- **Cache** — `TwoTierCacheStore` (L1 `MemCacheStore` + L2 `EncryptedCacheStore`), SWR support, ETag/If-None-Match, user isolation via JWT
- **Exceptions** — hierarchy extending `DioException`: `AcdcException` → `Auth|Client|Server|Network|Cache|Security` exceptions, with URL redaction and response truncation

## C# Porting Approach

### Core Mapping: Dio Interceptors → DelegatingHandler Pipeline

```
Dart (Dio):                         C# (HttpClient):
Interceptor.onRequest()    →       SendAsync() before base.SendAsync()
Interceptor.onResponse()   →       SendAsync() after base.SendAsync()
Interceptor.onError()      →       SendAsync() catch block
```

`IHttpClientFactory` is **required** (not optional) for server-side. It prevents socket exhaustion, handles DNS rotation, and manages handler lifetime:
```csharp
services.AddHttpClient("acdc")
    .AddHttpMessageHandler<LoggingHandler>()
    .AddHttpMessageHandler<ErrorHandler>()
    .AddHttpMessageHandler<CancellationHandler>()
    .AddHttpMessageHandler<AuthHandler>()
    .AddHttpMessageHandler<CacheHandler>()
    .AddHttpMessageHandler<DeduplicationHandler>();
```

### Key Dart → C# Mappings

| Dart | C# (server) |
|------|-------------|
| `Dio` + interceptors | `HttpClient` + `DelegatingHandler` chain via `IHttpClientFactory` |
| `Completer<T>` | `TaskCompletionSource<T>` |
| `CancelToken` | `CancellationToken` / `CancellationTokenSource` |
| `flutter_secure_storage` | `ITokenProvider` interface — default impl uses in-memory or `IDistributedCache` (Redis) |
| `connectivity_plus` | **Removed** — not needed on server |
| `dio_cache_interceptor` | FusionCache (`IMemoryCache` L1 + `IDistributedCache` Redis L2) |
| `encrypt` (AES for cache) | **Removed** — Redis handles its own security |
| `jwt_decoder` | `System.IdentityModel.Tokens.Jwt` — or use `HttpContext.User.Claims` from middleware |
| Dart `on Type catch` ordering | C# `catch` block ordering (most specific first) |
| Immutable builder `_copyWith()` | C# `record` with `with` expression or fluent builder |
| Dio's `options.extra` (per-request metadata) | `HttpRequestMessage.Options` dictionary |

### Exception Hierarchy (extends HttpRequestException)

```
HttpRequestException
  └─ AcdcException (base: URL redaction, response truncation, ToMap())
       ├─ AcdcAuthException        (401, 403)
       ├─ AcdcClientException      (4xx, has RetryAfter)
       ├─ AcdcServerException      (5xx)
       ├─ AcdcNetworkException     (timeouts, DNS — has NetworkErrorType enum)
       └─ AcdcCacheException       (cache failures — has CacheOperation enum)
```

`AcdcSecurityException` is **skipped** — server cert validation is handled by `HttpClientHandler.ServerCertificateCustomValidationCallback`, not the handler pipeline.

### Testing Stack

| Dart | C# (server) |
|------|-------------|
| `package:test` | xUnit |
| `mockito` | NSubstitute |
| `http_mock_adapter` (DioAdapter) | `RichardSzalay.MockHttp` |
| `shelf` / `shelf_io` (real HTTP server) | `WireMock.Net` |
| `FakeTokenProvider` (in-memory) | `InMemoryTokenProvider` implementing `ITokenProvider` (must be thread-safe) |
| `MockNetworkInfo` (always-online) | **Removed** — no `INetworkInfo` on server |

## Important Gotchas

- **Thread safety is the #1 concern** — Dart is single-threaded; C# server handles concurrent requests. All handlers, token refresh queues, backoff managers, and deduplication maps must use `SemaphoreSlim`, `ConcurrentDictionary`, `Interlocked`, etc.
- **`DelegatingHandler` instances are pooled** by `IHttpClientFactory` (2-min default lifetime). They must not store per-request state in instance fields — use `HttpRequestMessage.Options` instead.
- **AuthInterceptor retry uses a bare Dio instance** (no interceptors) — C# retry must request a separate `HttpClient` from `IHttpClientFactory`, not create `new HttpClient()`
- **`HttpRequestMessage` cannot be sent twice** — must clone before retry (unlike Dio's `RequestOptions`)
- **ErrorInterceptor only overrides `onError`** — it does NOT participate in request/response phases
- **Auth error vs transient error distinction is critical** — transient errors (network, 5xx) preserve tokens; auth errors (invalid_grant) clear tokens
- **DeduplicationInterceptor** only deduplicates GETs with identical URL+headers — not POSTs
- **User identity on server** — prefer `HttpContext.User.Claims` from ASP.NET Core auth middleware over manual JWT parsing
- **No `.gitignore` yet** — be careful not to commit `.claude/settings.local.json`, `.env`, or other local-only files. P1 scaffold will add a proper `.gitignore`.

## Code Style

- Target: .NET 8+, C# 12, ASP.NET Core
- `IHttpClientFactory` for all `HttpClient` usage — never `new HttpClient()`
- Use `record` types for DTOs and immutable config
- Use `IOptions<T>` pattern for configuration
- Use `ILogger<T>` for structured logging
- Use `IMemoryCache` / `IDistributedCache` / FusionCache for caching
- Async all the way — no `.Result` or `.Wait()`
- Nullable reference types enabled
- File-scoped namespaces
- All `DelegatingHandler` implementations must be thread-safe
