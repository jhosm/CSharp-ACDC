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

# General Guidelines

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

# CSharp-ACDC

**Server-only** C# port of **Dart-ACDC** (Authentication, Caching, Debugging, Client) — a production HTTP client library with auth, caching, logging, and structured error handling. Targets ASP.NET Core / .NET 10.

## Status

**v1.0.0 shipped.** All 11 OpenSpec change proposals (P1-P11) are implemented and archived. The library is fully functional with auth, caching, logging, cancellation, deduplication, and structured error handling.

## Implementation Roadmap

### Resolved Decisions

- **Root namespace**: `CSharpAcdc`
- **Mock framework**: NSubstitute
- **Default timeout**: 5 seconds (matches Dart-ACDC)
- **`AcdcSecurityException`**: Skipped (server cert validation uses `HttpClientHandler` callback)

### Completed Proposals

All proposals are implemented and archived in `openspec/changes/archive/`:

| # | Change ID | Status |
|---|-----------|--------|
| P1 | `add-solution-scaffold` | Archived |
| P2 | `add-exceptions-and-error-handler` | Archived |
| P3 | `add-logging-handler` | Archived |
| P4 | `add-cancellation-and-dedup-handlers` | Archived |
| P5 | `add-auth-system` | Archived |
| P6 | `add-cache-system` | Archived |
| P7 | `add-builder-and-di` | Archived |
| P8 | `add-integration-tests` | Archived |
| P9 | `add-ci-and-nuget-publishing` | Archived |
| P10 | `add-documentation-and-examples` | Archived |
| P11 | `add-nuget-package-metadata` | Archived |

## Prerequisites

- .NET 10 SDK
- Redis (for integration tests with L2 cache)
- `bd` CLI (issue tracking via beads)

## Commands

```bash
# Build and test
dotnet build                               # Build solution
dotnet test                                # Run all tests
dotnet test tests/CSharpAcdc.Tests         # Unit tests only
dotnet test tests/CSharpAcdc.IntegrationTests  # Integration tests only

# OpenSpec — change proposal workflow
openspec list                              # List all proposals and status
openspec archive <change-id>               # Archive a completed proposal

# Beads — issue tracking
bd ready                                   # Find available work
bd show <id>                               # View issue details
bd update <id> --status in_progress        # Claim work
bd close <id>                              # Complete work
bd sync                                    # Sync with git
```

## Repository Structure

```
src/CSharpAcdc/                            # Library (namespace: CSharpAcdc)
  Exceptions/  Handlers/  Auth/  Cache/
  Logging/  Configuration/  Extensions/
  Builder/  Client/  Cancellation/
tests/CSharpAcdc.Tests/                    # Unit tests (xUnit + NSubstitute)
tests/CSharpAcdc.IntegrationTests/         # Integration tests (WireMock.Net)
samples/
  BasicUsage/                              # Zero-config GET request to httpbin.org
  AuthenticatedClient/                     # OAuth 2.1 auth with token seeding
  CachedClient/                            # Caching with SWR and ETag support
  FullPipeline/                            # All features: auth + cache + logging
openspec/
  specs/                                   # Archived spec deltas from all proposals
  changes/archive/                         # Archived change proposals (P1-P11)
research/                                  # Dart-ACDC analysis docs
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
- **`.gitignore` is in place** — build artifacts, IDE files, `.env`, and OS files are excluded.

## Code Style

- Target: .NET 10, C# 14, ASP.NET Core
- `IHttpClientFactory` for all `HttpClient` usage — never `new HttpClient()`
- Use `record` types for DTOs and immutable config
- Use `IOptions<T>` pattern for configuration
- Use `ILogger<T>` for structured logging
- Use `IMemoryCache` / `IDistributedCache` / FusionCache for caching
- Async all the way — no `.Result` or `.Wait()`
- Nullable reference types enabled
- File-scoped namespaces
- All `DelegatingHandler` implementations must be thread-safe
