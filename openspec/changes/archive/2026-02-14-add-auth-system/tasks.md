# Tasks: Add Authentication System

## 1. Token Provider
- [ ] 1.1 Define `ITokenProvider` interface with `GetAccessTokenAsync`, `GetRefreshTokenAsync`, `SaveTokensAsync`, `ClearTokensAsync`, `GetTokenExpiryAsync` -- all async with `CancellationToken`
- [ ] 1.2 Implement `InMemoryTokenProvider` with `SemaphoreSlim(1,1)` guarding all read/write operations
- [ ] 1.3 Create `TokenRefreshResult` record with `AccessToken`, `RefreshToken`, `ExpiresAt` properties

## 2. Token Refresh Strategies
- [ ] 2.1 Define `ITokenRefreshStrategy` interface with `RefreshAsync(string refreshToken, CancellationToken ct)` returning `TokenRefreshResult`
- [ ] 2.2 Implement `OAuthTokenRefreshStrategy` with OAuth 2.1 `refresh_token` grant using separate named `HttpClient` (`"acdc-auth"`) from `IHttpClientFactory`
- [ ] 2.3 Handle RFC 1123 date parsing for token expiry (`expires_in` as seconds AND `Expires` header as RFC 1123 date string)
- [ ] 2.4 Map OAuth error codes: `invalid_grant` -> clear tokens and throw `AcdcAuthException`; transient errors -> preserve tokens and allow retry
- [ ] 2.5 Implement `CustomTokenRefreshStrategy` wrapping user-provided `Func<string, CancellationToken, Task<TokenRefreshResult>>`

## 3. Backoff Manager
- [ ] 3.1 Implement exponential backoff progression: 1s -> 2s -> 4s -> 8s -> 16s -> 30s (clamped at 30s max)
- [ ] 3.2 Add +/-10% jitter randomization to prevent thundering herd
- [ ] 3.3 Implement `WaitIfNeededAsync()` (awaits current backoff delay), `RecordFailure()` (advances backoff), `Reset()` (clears backoff)
- [ ] 3.4 Ensure thread safety with `SemaphoreSlim` for all state mutations

## 4. Auth Handler
- [ ] 4.1 Implement token injection -- add `Authorization: Bearer {token}` header on outgoing requests when a valid access token is available
- [ ] 4.2 Implement proactive refresh -- refresh token before expiry when remaining lifetime is less than configurable threshold (default 60s)
- [ ] 4.3 Implement reactive 401 retry -- on 401 response, refresh token and retry request using cloned `HttpRequestMessage`
- [ ] 4.4 Implement concurrent refresh queue -- `SemaphoreSlim(1,1)` + `TaskCompletionSource<bool>` pattern: first request acquires semaphore and creates TCS; subsequent requests await the TCS
- [ ] 4.5 Implement auth error vs transient error distinction -- `invalid_grant` clears tokens via `ITokenProvider.ClearTokensAsync()`; network/5xx errors preserve tokens and trigger backoff
- [ ] 4.6 Implement queue timeout (default 30s) -- throw `AcdcAuthException` when TCS await exceeds timeout

## 5. Auth Manager
- [ ] 5.1 Implement logout flow in strict order: cancel pending refresh -> clear token cache -> revoke tokens at endpoint -> clear local state
- [ ] 5.2 Implement `ForceRefreshAsync()` to trigger token refresh outside the normal request pipeline
- [ ] 5.3 Implement user change detection via `UserIdExtractor` -- detect when the token belongs to a different user than expected

## 6. User ID Extraction
- [ ] 6.1 Implement claim priority: `sub` > `user_id` > `uid` (matching Dart-ACDC behavior)
- [ ] 6.2 Implement `HttpContext.User.Claims` preference when `IHttpContextAccessor` is available (already parsed by ASP.NET Core auth middleware)
- [ ] 6.3 Implement fallback JWT parsing from `Authorization` header using `System.IdentityModel.Tokens.Jwt`

## 7. Configuration
- [ ] 7.1 Create `AcdcAuthOptions` record with: `RefreshEndpoint` (string), `ClientId` (string), `ClientSecret` (string?), `RefreshThreshold` (TimeSpan, default 60s), `QueueTimeout` (TimeSpan, default 30s), `RevocationEndpoint` (string?)

## 8. Unit Tests
- [ ] 8.1 Test `InMemoryTokenProvider` CRUD operations -- save, get, clear, get-expiry round-trip
- [ ] 8.2 Test `InMemoryTokenProvider` thread safety -- concurrent reads/writes from multiple tasks
- [ ] 8.3 Test `OAuthTokenRefreshStrategy` success flow -- valid refresh_token returns new tokens
- [ ] 8.4 Test `OAuthTokenRefreshStrategy` error code mapping -- `invalid_grant` throws `AcdcAuthException`, transient errors throw different exceptions
- [ ] 8.5 Test `BackoffManager` exponential progression -- verify 1s, 2s, 4s, 8s, 16s, 30s sequence
- [ ] 8.6 Test `BackoffManager` jitter range -- verify delays fall within +/-10% of expected value
- [ ] 8.7 Test `BackoffManager` clamp at 30s -- verify backoff does not exceed maximum
- [ ] 8.8 Test AuthHandler token injection -- verify `Authorization: Bearer` header added when token is available
- [ ] 8.9 Test AuthHandler proactive refresh at threshold -- verify refresh triggered when token expires within threshold
- [ ] 8.10 Test AuthHandler reactive 401 retry -- verify request retried with fresh token after 401
- [ ] 8.11 Test AuthHandler concurrent refresh -- verify only one refresh executes when multiple 401s arrive simultaneously
- [ ] 8.12 Test AuthHandler queue timeout -- verify `AcdcAuthException` thrown when refresh takes longer than timeout
- [ ] 8.13 Test `AcdcAuthManager` logout sequence -- verify ordered execution: cancel -> clear cache -> revoke -> clear local
- [ ] 8.14 Test `UserIdExtractor` claim priority -- verify `sub` preferred over `user_id` over `uid`
