# Design: Add Authentication System

## Context

This is a port of the Dart-ACDC authentication system (`AuthInterceptor`, `TokenProvider`, `TokenRefreshStrategy`, `BackoffManager`, `AcdcAuthManager`) to C#. The Dart implementation is single-threaded, using `Completer<void>` for refresh queuing and simple in-memory variables for token storage. The C# port targets ASP.NET Core servers where multiple HTTP requests execute concurrently on different threads, making thread safety the primary concern.

Key stakeholders:
- **Library consumers** -- need a simple, zero-config-possible auth handler that injects tokens and handles refresh transparently
- **ASP.NET Core developers** -- expect `IHttpClientFactory` integration, `IOptions<T>` configuration, and DI-friendly interfaces
- **The pipeline itself** -- AuthHandler sits at position 4 in the handler chain (after Logging, Error, Cancellation), and upstream handlers (Error, Logging) depend on AuthHandler throwing typed `AcdcAuthException` instances

Constraints:
- `DelegatingHandler` instances are pooled by `IHttpClientFactory` (2-minute default lifetime) -- no per-request state in instance fields
- `HttpRequestMessage` cannot be sent twice -- must clone before retry
- Auth retry must use a separate `HttpClient` without auth handlers to prevent infinite recursion

## Goals

- **Thread-safe token lifecycle** -- all token read/write operations are protected by synchronization primitives; no race conditions under concurrent access
- **Exactly-once refresh under concurrency** -- when N requests receive 401 simultaneously, exactly one refresh operation executes and the other N-1 requests wait for its result
- **Graceful logout** -- ordered teardown (cancel refresh -> clear cache -> revoke -> clear local) that prevents stale tokens from leaking
- **Extensible strategy pattern** -- library ships with `OAuthTokenRefreshStrategy` and `CustomTokenRefreshStrategy`; consumers can implement `ITokenRefreshStrategy` for any auth scheme
- **DI-first design** -- all components registered through `IServiceCollection`, configured via `IOptions<AcdcAuthOptions>`, injectable via constructor

## Non-Goals

- **No OAuth authorization code flow** -- this library handles the `refresh_token` grant only. The initial token acquisition (login, authorization code exchange) is the application's responsibility.
- **No OIDC discovery** -- we do not fetch `.well-known/openid-configuration`. The refresh endpoint URL is provided explicitly via `AcdcAuthOptions.RefreshEndpoint`.
- **No token introspection** -- we do not call the introspection endpoint to validate tokens server-side.
- **No multi-tenant token management** -- one `AuthHandler` instance manages one set of tokens. Multi-tenant scenarios require multiple named `HttpClient` registrations.

## Decisions

### 1. Concurrent Refresh Queue: SemaphoreSlim(1,1) + TaskCompletionSource<bool>

**Decision:** Use `SemaphoreSlim(1,1)` to serialize refresh entry, combined with a shared `TaskCompletionSource<bool>` that all waiting requests await.

**How it works:**
1. Request A gets 401, acquires the semaphore, creates a new `TaskCompletionSource<bool>`, stores it in a shared volatile field, and begins token refresh.
2. Request B gets 401, tries to acquire the semaphore but fails (timeout = 0), sees the existing TCS, and awaits it with a configurable timeout (default 30s).
3. Request A completes refresh, saves new tokens, sets `TCS.SetResult(true)`, releases the semaphore.
4. Request B's await completes, reads the refreshed token from `ITokenProvider`, and retries its request.

**Alternatives considered:**
- `lock` / `Monitor` -- cannot `await` inside a lock; would require blocking waits
- `AsyncLock` (third-party) -- adds external dependency for a single use case
- `Channel<T>` -- too complex for this pattern; channels are for producer/consumer, not one-shot signaling
- Dart's `Completer<void>` pattern -- conceptually equivalent, but `TaskCompletionSource<bool>` is the idiomatic C# equivalent

**Why this approach:** It is the direct C# equivalent of Dart's `Completer` pattern, uses only BCL types, supports async/await, and provides deterministic timeout behavior via `Task.WhenAny` with `Task.Delay`.

### 2. Separate Named HttpClient for Auth Retry

**Decision:** Token refresh calls use `IHttpClientFactory.CreateClient("acdc-auth")` -- a separate named client registered WITHOUT the auth handler chain.

**Rationale:** If the auth handler uses the same `HttpClient` (which has the auth handler in its pipeline), a 401 during refresh would trigger another refresh, causing infinite recursion. The Dart source solves this with a "bare" `Dio()` instance (no interceptors). The C# equivalent is a separate named client.

**Registration:**
```csharp
services.AddHttpClient("acdc-auth"); // No AddHttpMessageHandler calls
```

**Alternatives considered:**
- Passing a raw `HttpMessageHandler` -- breaks `IHttpClientFactory` pooling and DNS rotation
- Using `new HttpClient()` -- causes socket exhaustion on servers; violates the project constraint

### 3. Request Cloning Before Retry

**Decision:** When a 401 triggers a retry, the original `HttpRequestMessage` MUST be cloned before the second send, because `HttpRequestMessage` cannot be sent twice in .NET.

**Implementation:** Uses `CloneAsync()` from P4's `HttpRequestMessageExtensions`. If P4 has not yet landed, AuthHandler provides its own internal `CloneRequestAsync()` helper that copies method, URI, headers, content, options, and version. This internal helper is removed once P4 lands.

**Content cloning detail:** `HttpContent` does not expose a public clone API. The internal helper reads content into a byte array via `ReadAsByteArrayAsync()`, creates a new `ByteArrayContent`, and copies content headers. This works for all content types but buffers the body in memory. For streaming bodies, consumers should not use auth retry (set a request option to skip retry).

### 4. Auth Error vs Transient Error Classification

**Decision:** OAuth error responses are classified into two categories that trigger different behavior:

| Error | Action | Rationale |
|-------|--------|-----------|
| `invalid_grant` | Clear tokens, throw `AcdcAuthException` | Refresh token is invalid/expired/revoked; no amount of retrying will help |
| `invalid_client` | Clear tokens, throw `AcdcAuthException` | Client credentials are wrong; configuration error |
| `server_error`, `temporarily_unavailable` | Preserve tokens, backoff, retry | Transient server issue; tokens may still be valid |
| Network error (timeout, DNS, etc.) | Preserve tokens, backoff, retry | Token endpoint is temporarily unreachable |
| 5xx from token endpoint | Preserve tokens, backoff, retry | Server-side transient failure |

This matches the Dart-ACDC behavior where `AuthInterceptor._handleTokenRefreshError()` distinguishes between auth errors (clear tokens) and transient errors (preserve tokens).

### 5. Exponential Backoff with Jitter

**Decision:** Backoff progression is `min(baseDelay * 2^attempt, maxDelay)` with +/-10% jitter.

- Base delay: 1 second
- Max delay: 30 seconds
- Progression: 1s -> 2s -> 4s -> 8s -> 16s -> 30s -> 30s -> ...
- Jitter: multiply delay by random factor in [0.9, 1.1]

**Thread safety:** `BackoffManager` uses `SemaphoreSlim(1,1)` to protect the attempt counter. `RecordFailure()` acquires the semaphore, increments the counter, and releases. `WaitIfNeededAsync()` acquires the semaphore, reads the counter, releases, then awaits `Task.Delay()` outside the semaphore (to avoid holding the lock during the delay). `Reset()` acquires the semaphore and sets the counter to zero.

**Alternatives considered:**
- Polly retry policies -- Polly is a heavier dependency and designed for wrapping entire call sites, not for internal backoff state management within a handler
- `Interlocked` only -- insufficient because backoff calculation reads and writes multiple fields (attempt count, last failure time)

### 6. UserIdExtractor: HttpContext.User.Claims Preference

**Decision:** `UserIdExtractor` first attempts to read claims from `HttpContext.User.Claims` (via `IHttpContextAccessor`), falling back to manual JWT parsing from the `Authorization` header only when `IHttpContextAccessor` is not registered or `HttpContext` is null.

**Claim priority:** `sub` > `user_id` > `uid` (matching Dart-ACDC's `UserIdExtractor`).

**Rationale:** In ASP.NET Core, the authentication middleware already validates and parses the JWT token, populating `HttpContext.User.Claims`. Parsing the JWT again is redundant and skips middleware validation. The fallback exists for scenarios where the library is used outside of ASP.NET Core request pipeline (e.g., background services).

**Alternatives considered:**
- Always parse JWT manually -- duplicates middleware work, skips validation
- Require `IHttpContextAccessor` -- too restrictive; breaks background service usage

### 7. Logout Flow Ordering

**Decision:** Logout executes steps in strict order, matching the Dart-ACDC `AcdcAuthManager.logout()` sequence:

1. **Cancel pending refresh** -- set a cancellation flag that the refresh queue checks; any in-progress refresh completes but result is discarded
2. **Clear token cache** -- call `ITokenProvider.ClearTokensAsync()` to remove tokens from storage
3. **Revoke tokens at endpoint** -- POST to `AcdcAuthOptions.RevocationEndpoint` (if configured) to invalidate tokens server-side
4. **Clear local state** -- reset backoff manager, clear TCS reference, reset user ID

**Error handling:** Revocation failures are logged but do not throw -- logout must succeed even if the revocation endpoint is unreachable. This matches the Dart behavior.

## Risks / Trade-offs

### Deadlock in Refresh Queue
- **Risk:** If the refresh operation hangs indefinitely, all concurrent requests block on the TCS forever.
- **Mitigation:** Configurable timeout on TCS await (default 30s via `AcdcAuthOptions.QueueTimeout`). On timeout, the waiting request throws `AcdcAuthException` with a descriptive message. The refresh owner continues independently.

### Token Provider Race Condition
- **Risk:** Concurrent calls to `GetAccessTokenAsync()` and `SaveTokensAsync()` could return stale tokens or corrupt storage.
- **Mitigation:** `InMemoryTokenProvider` uses `SemaphoreSlim(1,1)` for ALL operations (read and write). Custom `ITokenProvider` implementations MUST document their thread-safety guarantees.

### Stale Token After Refresh Failure
- **Risk:** If refresh fails with a transient error, the old (potentially expired) token remains in storage. Subsequent requests may send an expired token and get 401 again.
- **Mitigation:** BackoffManager prevents rapid retry. After max backoff (30s), if refresh still fails, the token is cleared and `AcdcAuthException` is thrown. Proactive refresh attempts to refresh well before expiry, reducing the window for this scenario.

### HttpRequestMessage Content Buffering
- **Risk:** Cloning request content for 401 retry buffers the entire body in memory. Large request bodies (file uploads) could cause memory pressure.
- **Mitigation:** Document that auth retry is not suitable for streaming/large-body requests. Consumers can set a request option to disable retry for specific requests.

### DelegatingHandler Pooling
- **Risk:** `DelegatingHandler` instances are pooled with a 2-minute default lifetime. Any per-request state stored in instance fields would leak across requests.
- **Mitigation:** All per-request state (current TCS, per-request tokens) flows through method parameters or `HttpRequestMessage.Options`. The only instance-level state is the shared `SemaphoreSlim` and volatile TCS reference, which are intentionally shared across requests.

## Migration Plan

Not applicable -- this is a new capability in a greenfield project. No migration from existing code is needed.

## Open Questions

1. **Should `InMemoryTokenProvider` support `IDistributedCache` (Redis) as a secondary store?** -- Current design is in-memory only. Redis-backed `ITokenProvider` could be a separate implementation (e.g., `DistributedTokenProvider`) in a future proposal.
2. **Should `OAuthTokenRefreshStrategy` support PKCE for refresh?** -- OAuth 2.1 recommends PKCE, but for the `refresh_token` grant specifically, PKCE is not required. Defer unless there is a concrete use case.
3. **Should `ForceRefreshAsync` participate in the concurrent refresh queue?** -- Current design: yes, it uses the same queue to prevent duplicate refreshes. Alternative: bypass the queue for force refresh. Decision: use the queue for consistency.
