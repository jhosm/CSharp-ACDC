# Change: Add Authentication System

## Why

Authentication is the most critical feature of the HTTP client library. The auth system handles token storage, injection, proactive/reactive refresh, concurrent refresh queuing, exponential backoff, and logout. All of these components are tightly coupled -- splitting them would create interface instability since the concurrent refresh queue deeply depends on `ITokenProvider`'s thread-safety contract and the `ITokenRefreshStrategy` abstraction. This is P5, the most complex proposal in the pipeline, and sits on the critical path: P1 -> P2 -> P5 -> P7 -> P8.

The Dart-ACDC `AuthInterceptor` is the most complex interceptor in the library, and the C# port introduces significant additional complexity due to thread safety requirements. Dart is single-threaded, so `AuthInterceptor` uses a simple `Completer<void>` for refresh queuing. C# servers handle concurrent requests across multiple threads, requiring `SemaphoreSlim(1,1)` + `TaskCompletionSource<bool>` patterns, thread-safe token providers, and careful lock ordering to prevent deadlocks.

## What Changes

### Token Provider
- **`ITokenProvider`** interface -- `GetAccessTokenAsync()`, `GetRefreshTokenAsync()`, `SaveTokensAsync()`, `ClearTokensAsync()`, `GetTokenExpiryAsync()`, all async with `CancellationToken`
- **`InMemoryTokenProvider`** -- thread-safe default implementation using `SemaphoreSlim(1,1)` for all read/write operations

### Token Refresh
- **`TokenRefreshResult`** record -- immutable DTO with `AccessToken`, `RefreshToken`, `ExpiresAt`
- **`ITokenRefreshStrategy`** interface -- `RefreshAsync(string refreshToken, CancellationToken ct)` returning `TokenRefreshResult`
- **`OAuthTokenRefreshStrategy`** -- OAuth 2.1 `refresh_token` grant implementation
  - RFC 1123 date parsing (fixes Dart bug that assumes Unix epoch integers)
  - OAuth error code mapping (`invalid_grant` -> clear tokens, others -> retry)
  - Uses separate `HttpClient` from `IHttpClientFactory` (named `"acdc-auth"`, no interceptors) for token endpoint calls
- **`CustomTokenRefreshStrategy`** -- wraps user-provided `Func<string, CancellationToken, Task<TokenRefreshResult>>`

### Backoff
- **`BackoffManager`** -- thread-safe exponential backoff: 1s base -> 30s max, `WaitIfNeededAsync()`, `Reset()`, `RecordFailure()`
  - Uses `SemaphoreSlim` for thread safety
  - Jitter: +/-10% randomization to prevent thundering herd

### Auth Handler
- **`AuthHandler : DelegatingHandler`** -- the main pipeline handler:
  - Token injection: adds `Authorization: Bearer {token}` header
  - Proactive refresh: refreshes token before expiry (configurable threshold, default 60s)
  - Reactive 401 retry: on 401 response, refresh token and retry request (must clone `HttpRequestMessage`)
  - Concurrent refresh queue: `SemaphoreSlim(1,1)` + `TaskCompletionSource<bool>` pattern -- when multiple requests get 401 simultaneously, only ONE triggers refresh, others wait on the TCS
  - Auth error vs transient error distinction: `invalid_grant` clears tokens; network/5xx errors preserve tokens
  - Queue timeout: default 30s, throws `AcdcAuthException` on timeout

### Auth Manager
- **`AcdcAuthManager`** -- orchestrates logout and force-refresh flows:
  - Logout: cancel pending refresh -> clear token cache -> revoke tokens at endpoint -> clear local state
  - `ForceRefreshAsync()` -- force a token refresh outside the normal pipeline
  - User change detection via `UserIdExtractor`

### User Identity
- **`UserIdExtractor`** -- extracts user ID from JWT claims with priority: `sub` > `user_id` > `uid`
  - Prefers `HttpContext.User.Claims` when available (already parsed by ASP.NET Core auth middleware)
  - Falls back to manual JWT parsing from `Authorization` header

### Configuration
- **`AcdcAuthOptions`** record -- refresh endpoint URL, client ID, client secret, refresh threshold (60s), queue timeout (30s), revocation endpoint URL

## Impact

- **Affected specs:** auth-system (new)
- **Depends on:** P2 (exceptions -- `AcdcAuthException`, `AcdcRequestOptions`), P4 (`HttpRequestMessageExtensions` for request cloning -- but P5 can use its own internal clone method until P4 lands)
- **Parallel with:** P3 (LoggingHandler), P4 (CancellationHandler + DeduplicationHandler), P6 (CacheHandler)
- **Critical path:** P1 -> P2 -> P5 -> P7 -> P8

### Files to be created

**Source files:**
- `src/CSharpAcdc/Auth/ITokenProvider.cs`
- `src/CSharpAcdc/Auth/InMemoryTokenProvider.cs`
- `src/CSharpAcdc/Auth/TokenRefreshResult.cs`
- `src/CSharpAcdc/Auth/ITokenRefreshStrategy.cs`
- `src/CSharpAcdc/Auth/OAuthTokenRefreshStrategy.cs`
- `src/CSharpAcdc/Auth/CustomTokenRefreshStrategy.cs`
- `src/CSharpAcdc/Auth/BackoffManager.cs`
- `src/CSharpAcdc/Auth/AcdcAuthManager.cs`
- `src/CSharpAcdc/Auth/UserIdExtractor.cs`
- `src/CSharpAcdc/Handlers/AuthHandler.cs`
- `src/CSharpAcdc/Configuration/AcdcAuthOptions.cs`

**Test files:**
- `tests/CSharpAcdc.Tests/Auth/InMemoryTokenProviderTests.cs`
- `tests/CSharpAcdc.Tests/Auth/OAuthTokenRefreshStrategyTests.cs`
- `tests/CSharpAcdc.Tests/Auth/BackoffManagerTests.cs`
- `tests/CSharpAcdc.Tests/Auth/AcdcAuthManagerTests.cs`
- `tests/CSharpAcdc.Tests/Auth/UserIdExtractorTests.cs`
- `tests/CSharpAcdc.Tests/Handlers/AuthHandlerTests.cs`
- `tests/CSharpAcdc.Tests/Handlers/AuthHandlerConcurrencyTests.cs`
