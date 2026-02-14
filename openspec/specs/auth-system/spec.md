# auth-system Specification

## Purpose
TBD - created by archiving change add-auth-system. Update Purpose after archive.
## Requirements
### Requirement: Token Provider Interface
`ITokenProvider` SHALL define async methods for token lifecycle management: `GetAccessTokenAsync(CancellationToken)`, `GetRefreshTokenAsync(CancellationToken)`, `SaveTokensAsync(string accessToken, string refreshToken, DateTimeOffset expiresAt, CancellationToken)`, `ClearTokensAsync(CancellationToken)`, and `GetTokenExpiryAsync(CancellationToken)`. All methods MUST accept a `CancellationToken` parameter. The interface MUST NOT prescribe a specific storage mechanism.

#### Scenario: Token round-trip via ITokenProvider
- **WHEN** a caller invokes `SaveTokensAsync` with an access token, refresh token, and expiry
- **THEN** subsequent calls to `GetAccessTokenAsync`, `GetRefreshTokenAsync`, and `GetTokenExpiryAsync` SHALL return the saved values

#### Scenario: Clear tokens removes all stored tokens
- **WHEN** a caller invokes `ClearTokensAsync`
- **THEN** subsequent calls to `GetAccessTokenAsync` and `GetRefreshTokenAsync` SHALL return `null`

#### Scenario: CancellationToken is respected
- **WHEN** a caller passes a cancelled `CancellationToken` to any `ITokenProvider` method
- **THEN** the method SHALL throw `OperationCanceledException`

---

### Requirement: In-Memory Token Provider
`InMemoryTokenProvider` SHALL implement `ITokenProvider` with thread-safe in-memory storage. All read and write operations MUST be serialized using `SemaphoreSlim(1,1)` to guarantee consistency under concurrent access. The implementation MUST be safe for use from multiple threads simultaneously.

#### Scenario: Concurrent reads and writes do not corrupt state
- **WHEN** multiple tasks concurrently invoke `SaveTokensAsync` and `GetAccessTokenAsync` on the same `InMemoryTokenProvider` instance
- **THEN** each `GetAccessTokenAsync` call SHALL return either `null` or a complete, valid access token -- never a partial or corrupted value

#### Scenario: SemaphoreSlim serialization
- **WHEN** two tasks simultaneously invoke `SaveTokensAsync` with different token values
- **THEN** after both complete, `GetAccessTokenAsync` SHALL return the token from whichever `SaveTokensAsync` completed last

---

### Requirement: Token Refresh Strategy
`ITokenRefreshStrategy` SHALL define a single method `RefreshAsync(string refreshToken, CancellationToken ct)` that returns a `TokenRefreshResult`. The `TokenRefreshResult` MUST be an immutable `record` with properties `AccessToken` (string), `RefreshToken` (string), and `ExpiresAt` (DateTimeOffset).

#### Scenario: Strategy returns new tokens on success
- **WHEN** a caller invokes `RefreshAsync` with a valid refresh token
- **THEN** the strategy SHALL return a `TokenRefreshResult` with non-null `AccessToken`, non-null `RefreshToken`, and a future `ExpiresAt`

#### Scenario: Strategy propagates cancellation
- **WHEN** a caller passes a cancelled `CancellationToken` to `RefreshAsync`
- **THEN** the strategy SHALL throw `OperationCanceledException`

---

### Requirement: OAuth Token Refresh
`OAuthTokenRefreshStrategy` SHALL implement the OAuth 2.1 `refresh_token` grant type. It MUST use a separate `HttpClient` obtained from `IHttpClientFactory` with the named client `"acdc-auth"` (which MUST NOT have auth handlers in its pipeline) to prevent infinite refresh recursion. The strategy MUST parse token endpoint responses including RFC 1123 date format for expiry (in addition to `expires_in` as integer seconds).

#### Scenario: Successful OAuth refresh
- **WHEN** `RefreshAsync` is called with a valid refresh token
- **AND** the token endpoint returns HTTP 200 with `access_token`, `refresh_token`, and `expires_in`
- **THEN** the strategy SHALL return a `TokenRefreshResult` with the new tokens and `ExpiresAt` calculated as `DateTimeOffset.UtcNow + TimeSpan.FromSeconds(expires_in)`

#### Scenario: RFC 1123 date parsing for expiry
- **WHEN** the token endpoint response contains an `Expires` header or field in RFC 1123 date format (e.g., `"Sun, 06 Nov 2024 08:49:37 GMT"`)
- **THEN** the strategy SHALL parse the date correctly and use it as `ExpiresAt`, instead of assuming Unix epoch integer format

#### Scenario: invalid_grant error clears tokens
- **WHEN** the token endpoint returns an OAuth error response with `error: "invalid_grant"`
- **THEN** the strategy SHALL throw `AcdcAuthException` indicating the refresh token is invalid
- **AND** the calling code (AuthHandler) SHALL invoke `ITokenProvider.ClearTokensAsync()` to remove stale tokens

#### Scenario: Transient error preserves tokens
- **WHEN** the token endpoint returns HTTP 5xx or a network error occurs during refresh
- **THEN** the strategy SHALL throw an exception that is NOT `AcdcAuthException`
- **AND** the calling code (AuthHandler) SHALL NOT clear tokens but SHALL trigger backoff

#### Scenario: Separate HttpClient prevents infinite recursion
- **WHEN** `OAuthTokenRefreshStrategy` calls the token endpoint
- **THEN** it MUST use `IHttpClientFactory.CreateClient("acdc-auth")` which has no `AuthHandler` in its pipeline
- **AND** a 401 response from the token endpoint SHALL NOT trigger another refresh attempt

---

### Requirement: Custom Token Refresh
`CustomTokenRefreshStrategy` SHALL wrap a user-provided `Func<string, CancellationToken, Task<TokenRefreshResult>>` delegate. The delegate MUST be invoked with the current refresh token and a `CancellationToken`. The strategy SHALL pass through the delegate's return value or exception without modification.

#### Scenario: Custom strategy delegates to user function
- **WHEN** `RefreshAsync` is called on a `CustomTokenRefreshStrategy`
- **THEN** the wrapped `Func` SHALL be invoked with the refresh token and cancellation token
- **AND** the `TokenRefreshResult` from the `Func` SHALL be returned as-is

#### Scenario: Custom strategy propagates exceptions
- **WHEN** the wrapped `Func` throws an exception
- **THEN** `CustomTokenRefreshStrategy.RefreshAsync` SHALL propagate the exception without wrapping

---

### Requirement: Exponential Backoff
`BackoffManager` SHALL implement exponential backoff with a base delay of 1 second and a maximum delay of 30 seconds (clamped). The delay progression SHALL be `min(baseDelay * 2^attempt, maxDelay)`. Jitter of +/-10% MUST be applied to each calculated delay to prevent thundering herd. The implementation MUST be thread-safe using `SemaphoreSlim`.

#### Scenario: Exponential progression
- **WHEN** `RecordFailure()` is called repeatedly
- **THEN** `WaitIfNeededAsync()` SHALL delay for approximately 1s, 2s, 4s, 8s, 16s, 30s on successive calls

#### Scenario: Clamping at maximum
- **WHEN** the calculated delay exceeds 30 seconds
- **THEN** the delay SHALL be clamped to 30 seconds (plus jitter)

#### Scenario: Jitter range
- **WHEN** `WaitIfNeededAsync()` calculates a delay
- **THEN** the actual delay SHALL fall within +/-10% of the nominal delay (e.g., a nominal 4s delay results in 3.6s to 4.4s)

#### Scenario: Reset clears backoff state
- **WHEN** `Reset()` is called after multiple failures
- **THEN** the next `WaitIfNeededAsync()` SHALL delay for approximately 1 second (base delay, as if no failures had occurred)

#### Scenario: Thread-safe concurrent access
- **WHEN** multiple tasks call `RecordFailure()` and `WaitIfNeededAsync()` concurrently
- **THEN** the attempt counter SHALL be incremented correctly without data corruption

---

### Requirement: Token Injection
`AuthHandler` SHALL inject an `Authorization: Bearer {token}` header on outgoing HTTP requests when a valid access token is available from `ITokenProvider`. If no access token is available (i.e., `GetAccessTokenAsync` returns `null`), the handler SHALL forward the request without an `Authorization` header.

#### Scenario: Bearer token added when available
- **WHEN** `ITokenProvider.GetAccessTokenAsync()` returns a non-null access token
- **THEN** `AuthHandler` SHALL add an `Authorization` header with value `Bearer {token}` to the outgoing request

#### Scenario: No header when token is unavailable
- **WHEN** `ITokenProvider.GetAccessTokenAsync()` returns `null`
- **THEN** `AuthHandler` SHALL forward the request without modifying the `Authorization` header

#### Scenario: Existing Authorization header is replaced
- **WHEN** the outgoing request already contains an `Authorization` header
- **AND** `ITokenProvider.GetAccessTokenAsync()` returns a non-null access token
- **THEN** `AuthHandler` SHALL replace the existing header with the new `Bearer {token}` value

---

### Requirement: Proactive Refresh
`AuthHandler` SHALL proactively refresh the access token before it expires when the remaining token lifetime is less than the configurable refresh threshold (default: 60 seconds, configured via `AcdcAuthOptions.RefreshThreshold`). Proactive refresh MUST NOT block the current request -- the request proceeds with the current (still valid) token while refresh happens in the background.

#### Scenario: Token refreshed before expiry
- **WHEN** `ITokenProvider.GetTokenExpiryAsync()` returns an expiry time that is less than `RefreshThreshold` in the future
- **AND** no refresh is currently in progress
- **THEN** `AuthHandler` SHALL initiate a token refresh in the background
- **AND** the current request SHALL proceed with the existing (still valid) access token

#### Scenario: No refresh when token is fresh
- **WHEN** `ITokenProvider.GetTokenExpiryAsync()` returns an expiry time that is more than `RefreshThreshold` in the future
- **THEN** `AuthHandler` SHALL NOT initiate a token refresh

#### Scenario: Proactive refresh failure does not fail the current request
- **WHEN** a proactive background refresh fails
- **THEN** the failure SHALL be logged but SHALL NOT cause the current request to fail
- **AND** the next request SHALL attempt refresh again

---

### Requirement: Reactive 401 Retry
`AuthHandler` SHALL retry a request with a refreshed token when a 401 Unauthorized response is received from the downstream handler. Before retry, the handler MUST clone the original `HttpRequestMessage` (since `HttpRequestMessage` cannot be sent twice) and update the `Authorization` header with the new access token.

#### Scenario: 401 triggers refresh and retry
- **WHEN** the downstream handler returns HTTP 401
- **THEN** `AuthHandler` SHALL invoke `ITokenRefreshStrategy.RefreshAsync()` to obtain new tokens
- **AND** save the new tokens via `ITokenProvider.SaveTokensAsync()`
- **AND** clone the original `HttpRequestMessage`
- **AND** set the `Authorization: Bearer {new_token}` header on the clone
- **AND** send the cloned request through the downstream handler

#### Scenario: Retry succeeds with new token
- **WHEN** a 401-triggered retry is sent with the refreshed token
- **AND** the downstream handler returns HTTP 200
- **THEN** `AuthHandler` SHALL return the successful response to the caller

#### Scenario: Retry fails with 401 again
- **WHEN** a 401-triggered retry also returns HTTP 401
- **THEN** `AuthHandler` SHALL NOT retry again (single retry only)
- **AND** SHALL return the 401 response to the caller

#### Scenario: Request cloning preserves all properties
- **WHEN** `AuthHandler` clones an `HttpRequestMessage` for retry
- **THEN** the clone SHALL preserve the HTTP method, request URI, headers, content, options, and HTTP version from the original request

---

### Requirement: Concurrent Refresh Queue
`AuthHandler` SHALL ensure that when multiple requests receive HTTP 401 simultaneously, exactly ONE token refresh operation executes while all other requests wait for its result. The implementation MUST use `SemaphoreSlim(1,1)` for mutual exclusion combined with `TaskCompletionSource<bool>` for signaling completion to waiting requests.

#### Scenario: Single refresh under concurrent 401s
- **WHEN** 10 concurrent requests all receive HTTP 401 at the same time
- **THEN** exactly one call to `ITokenRefreshStrategy.RefreshAsync()` SHALL execute
- **AND** the other 9 requests SHALL wait for the refresh to complete
- **AND** after refresh succeeds, all 10 requests SHALL retry with the new token

#### Scenario: Queue timeout
- **WHEN** a request is waiting for an in-progress refresh to complete
- **AND** the refresh takes longer than `AcdcAuthOptions.QueueTimeout` (default: 30 seconds)
- **THEN** the waiting request SHALL throw `AcdcAuthException` with a message indicating queue timeout

#### Scenario: Refresh failure signals all waiters
- **WHEN** the refresh operation fails (e.g., `invalid_grant`)
- **THEN** all waiting requests SHALL receive the failure
- **AND** SHALL NOT attempt their own refresh

---

### Requirement: Auth Error Classification
`AuthHandler` SHALL classify errors that occur during token refresh into auth errors and transient errors. Auth errors (`invalid_grant`, `invalid_client`) MUST trigger token clearing via `ITokenProvider.ClearTokensAsync()` and throw `AcdcAuthException`. Transient errors (network failures, HTTP 5xx from token endpoint) MUST preserve existing tokens and trigger `BackoffManager.RecordFailure()`.

#### Scenario: invalid_grant clears tokens
- **WHEN** `ITokenRefreshStrategy.RefreshAsync()` fails with an OAuth `invalid_grant` error
- **THEN** `AuthHandler` SHALL invoke `ITokenProvider.ClearTokensAsync()`
- **AND** SHALL throw `AcdcAuthException`
- **AND** SHALL reset the `BackoffManager`

#### Scenario: Network error during refresh preserves tokens
- **WHEN** `ITokenRefreshStrategy.RefreshAsync()` fails due to a network error (timeout, DNS failure)
- **THEN** `AuthHandler` SHALL NOT clear tokens
- **AND** SHALL invoke `BackoffManager.RecordFailure()`
- **AND** SHALL throw the network exception (wrapped or as-is)

#### Scenario: 5xx from token endpoint preserves tokens
- **WHEN** the token endpoint returns HTTP 500 during refresh
- **THEN** `AuthHandler` SHALL NOT clear tokens
- **AND** SHALL invoke `BackoffManager.RecordFailure()`

---

### Requirement: Logout
`AcdcAuthManager` SHALL orchestrate a complete logout flow by executing steps in strict order: (1) cancel any pending token refresh, (2) clear token cache via `ITokenProvider.ClearTokensAsync()`, (3) revoke tokens at the configured revocation endpoint (if `AcdcAuthOptions.RevocationEndpoint` is set), (4) clear local state (reset backoff manager, clear user ID). Revocation failures MUST be logged but MUST NOT cause the logout operation to throw.

#### Scenario: Full logout sequence
- **WHEN** `AcdcAuthManager.LogoutAsync()` is called
- **THEN** the manager SHALL cancel any in-progress token refresh
- **AND** SHALL invoke `ITokenProvider.ClearTokensAsync()`
- **AND** SHALL POST to the revocation endpoint with the refresh token (if endpoint is configured)
- **AND** SHALL reset the `BackoffManager`
- **AND** SHALL clear the cached user ID

#### Scenario: Logout succeeds even if revocation fails
- **WHEN** the revocation endpoint returns an error or is unreachable
- **THEN** the logout operation SHALL still complete successfully
- **AND** the revocation failure SHALL be logged at Warning level

#### Scenario: Logout without revocation endpoint
- **WHEN** `AcdcAuthOptions.RevocationEndpoint` is `null`
- **THEN** the logout operation SHALL skip the revocation step and complete the remaining steps normally

---

### Requirement: Force Refresh
`AcdcAuthManager.ForceRefreshAsync()` SHALL trigger a token refresh outside the normal request pipeline. The force refresh MUST participate in the concurrent refresh queue -- if a refresh is already in progress, `ForceRefreshAsync` SHALL wait for it instead of starting a duplicate refresh.

#### Scenario: Force refresh obtains new tokens
- **WHEN** `ForceRefreshAsync()` is called
- **THEN** the auth manager SHALL invoke `ITokenRefreshStrategy.RefreshAsync()` with the current refresh token
- **AND** SHALL save the new tokens via `ITokenProvider.SaveTokensAsync()`

#### Scenario: Force refresh joins existing refresh
- **WHEN** `ForceRefreshAsync()` is called while a refresh is already in progress (triggered by a 401)
- **THEN** `ForceRefreshAsync` SHALL wait for the existing refresh to complete instead of starting a new one
- **AND** SHALL return successfully when the existing refresh completes

---

### Requirement: User ID Extraction
`UserIdExtractor` SHALL extract a user identity string from JWT claims with the following priority order: `sub` > `user_id` > `uid`. The extractor MUST prefer `HttpContext.User.Claims` (via `IHttpContextAccessor`) when available, as ASP.NET Core authentication middleware already validates and parses the token. When `IHttpContextAccessor` is not registered or `HttpContext` is null, the extractor SHALL fall back to manual JWT parsing from the `Authorization: Bearer` header using `System.IdentityModel.Tokens.Jwt`.

#### Scenario: Extract user ID from HttpContext claims
- **WHEN** `IHttpContextAccessor` is available and `HttpContext.User` has a `sub` claim
- **THEN** `UserIdExtractor` SHALL return the value of the `sub` claim

#### Scenario: Claim priority ordering
- **WHEN** the JWT contains both `sub` and `user_id` claims
- **THEN** `UserIdExtractor` SHALL return the `sub` claim value (highest priority)

#### Scenario: Fallback to user_id when sub is absent
- **WHEN** the JWT does not contain a `sub` claim but contains `user_id`
- **THEN** `UserIdExtractor` SHALL return the `user_id` claim value

#### Scenario: Fallback to uid when sub and user_id are absent
- **WHEN** the JWT contains only a `uid` claim (no `sub` or `user_id`)
- **THEN** `UserIdExtractor` SHALL return the `uid` claim value

#### Scenario: Fallback to JWT parsing when HttpContext is unavailable
- **WHEN** `IHttpContextAccessor` is not registered or `HttpContext` is null
- **AND** the request contains an `Authorization: Bearer {token}` header
- **THEN** `UserIdExtractor` SHALL parse the JWT from the header and extract the user ID claim

#### Scenario: No user ID available
- **WHEN** no claims are available from either `HttpContext` or the `Authorization` header
- **THEN** `UserIdExtractor` SHALL return `null`

---

### Requirement: Auth Configuration
`AcdcAuthOptions` SHALL be an immutable `record` providing configuration for the authentication system. It MUST include: `RefreshEndpoint` (string, required -- the token endpoint URL), `ClientId` (string, required -- OAuth client identifier), `ClientSecret` (string?, optional -- OAuth client secret), `RefreshThreshold` (TimeSpan, default 60 seconds -- how early to proactively refresh), `QueueTimeout` (TimeSpan, default 30 seconds -- max wait for concurrent refresh), and `RevocationEndpoint` (string?, optional -- token revocation URL). The options MUST be configurable via `IOptions<AcdcAuthOptions>` for DI integration.

#### Scenario: Default configuration values
- **WHEN** `AcdcAuthOptions` is instantiated with only required properties
- **THEN** `RefreshThreshold` SHALL default to 60 seconds
- **AND** `QueueTimeout` SHALL default to 30 seconds
- **AND** `ClientSecret` SHALL default to `null`
- **AND** `RevocationEndpoint` SHALL default to `null`

#### Scenario: IOptions integration
- **WHEN** `AcdcAuthOptions` is registered via `services.Configure<AcdcAuthOptions>(configuration.GetSection("Acdc:Auth"))`
- **THEN** `IOptions<AcdcAuthOptions>` SHALL be injectable into any component that requires auth configuration

