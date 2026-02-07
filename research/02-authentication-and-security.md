# Authentication and Security Module Analysis

> Research document for porting Dart-ACDC auth/security subsystem to C#.
> Source: `/Users/joaomiranda/dev/Dart-ACDC/`

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Token Provider Abstraction](#2-token-provider-abstraction)
3. [Token Refresh Strategies](#3-token-refresh-strategies)
4. [Auth Interceptor - Core Flow](#4-auth-interceptor---core-flow)
5. [Concurrency Handling During Token Refresh](#5-concurrency-handling-during-token-refresh)
6. [Backoff Strategy for Failed Refreshes](#6-backoff-strategy-for-failed-refreshes)
7. [Auth Manager - Logout and Token Revocation](#7-auth-manager---logout-and-token-revocation)
8. [Certificate Pinning](#8-certificate-pinning)
9. [User ID Extraction and JWT Utilities](#9-user-id-extraction-and-jwt-utilities)
10. [Test Patterns and Edge Cases](#10-test-patterns-and-edge-cases)
11. [C# Porting Considerations](#11-c-porting-considerations)

---

## 1. Architecture Overview

The authentication and security subsystem is organized into three layers:

```
lib/src/auth/                    # Token management and refresh strategies
  token_provider.dart            # Abstract interface for token storage
  secure_token_provider.dart     # FlutterSecureStorage implementation
  token_refresh_strategy.dart    # Abstract interface for refresh logic
  oauth_token_refresh_strategy.dart  # OAuth 2.1 implementation
  custom_token_refresh_strategy.dart # User-provided function wrapper
  acdc_auth_manager.dart         # High-level auth operations (logout, refresh)
  backoff_manager.dart           # Exponential backoff for server errors
  token_refresh_result.dart      # DTO for refresh operation results

lib/src/interceptors/            # Request/response pipeline integration
  auth_interceptor.dart          # Dio interceptor: token injection + refresh
  auth_request_helper.dart       # Static utilities for header manipulation

lib/src/security/                # Transport-level security
  certificate_pinning_config.dart  # Pinning configuration model
  pinning_http_client.dart         # HttpClient wrapper with pinning
  pinning_verifier.dart            # Certificate chain verification logic
  spki_util.dart                   # ASN.1/DER parser for SPKI extraction
  user_id_extractor.dart           # JWT-based user identity extraction
```

**Key Design Principles:**
- Strategy pattern for pluggable refresh mechanisms (OAuth, custom)
- Interface-based token storage for platform independence
- Interceptor-based transparent token injection (no app code changes)
- Dual refresh: proactive (before expiry) + reactive (on 401)
- Best-effort error handling (auth failures degrade gracefully)

> **Server-only note:** `SecureTokenProvider` (backed by `FlutterSecureStorage` / Keychain / Keystore) is **not ported**. The C# server port uses an `ITokenProvider` interface with a default `InMemoryTokenProvider` implementation. For distributed scenarios, tokens can be stored in `IDistributedCache` (Redis). Hardware-backed secure storage is a mobile concern.

> **Added from review:** `AcdcAuthException` handles both 401 and 403, with different default messages:
> - 401: "Authentication failed: Invalid or expired token"
> - 403: "Authorization failed: Insufficient permissions"
>
> All exceptions apply URL redaction via `AcdcException.redactUrl()` and response body truncation via `AcdcException.truncateResponseBody()`.

---

## 2. Token Provider Abstraction

### Interface (`token_provider.dart`)

```dart
abstract class TokenProvider {
  Future<String?> getAccessToken();
  Future<String?> getRefreshToken();
  Future<DateTime?> getAccessTokenExpiry();    // Enables proactive refresh
  Future<DateTime?> getRefreshTokenExpiry();   // Enables refresh token expiry check
  Future<void> setTokens({
    required String accessToken,
    String? refreshToken,          // Optional for token rotation
    DateTime? accessExpiry,
    DateTime? refreshExpiry,
  });
  Future<void> clearTokens();
}
```

**Design decisions:**
- All methods are `async` (Future-based) to support platform-specific storage that may be I/O-bound
- Expiry is stored separately, not parsed from JWT -- enables both JWT and opaque tokens
- `setTokens` uses optional parameters for partial updates (e.g., rotation may only update refresh token)

> **Review correction:** `accessToken` is **always** written (it is a required parameter). Only `refreshToken`, `accessExpiry`, and `refreshExpiry` are conditional on being non-null. When `refreshToken` is null, the **old refresh token is preserved** in storage. However, different test `TokenProvider` implementations handle this inconsistently -- `TestTokenProvider` in `logout_during_refresh_test.dart` always overwrites `_refreshToken` (even with null), while `oauth_21_compliance_test.dart` correctly preserves old refresh tokens when null. The C# port must define this contract clearly.

- Provider stores tokens without validating expiry -- expiry validation is the interceptor's responsibility

### Secure Implementation (`secure_token_provider.dart`)

```dart
class SecureTokenProvider implements TokenProvider {
  const SecureTokenProvider({FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage(
            iOptions: IOSOptions(
              accessibility: KeychainAccessibility.first_unlock,
            ),
          );

  static const String _keyAccessToken = 'acdc_access_token';
  static const String _keyRefreshToken = 'acdc_refresh_token';
  static const String _keyAccessExpiry = 'acdc_access_expiry';
  static const String _keyRefreshExpiry = 'acdc_refresh_expiry';
```

**Storage keys:** All prefixed with `acdc_` to avoid collisions.

**Platform-specific defaults:**
- iOS: Keychain with `first_unlock` accessibility (available after first device unlock, survives app restart)
- Android: EncryptedSharedPreferences (AES-256 backed by Android Keystore)

**Parallelized writes:** Uses `Future.wait()` for concurrent storage operations:

```dart
Future<void> setTokens({...}) async {
  await Future.wait([
    _storage.write(key: _keyAccessToken, value: accessToken),
    if (refreshToken != null) _storage.write(key: _keyRefreshToken, value: refreshToken),
    if (accessExpiry != null) _storage.write(key: _keyAccessExpiry, value: accessExpiry.toIso8601String()),
    if (refreshExpiry != null) _storage.write(key: _keyRefreshExpiry, value: refreshExpiry.toIso8601String()),
  ]);
}
```

> **Server-only note:** The `SecureTokenProvider` with `FlutterSecureStorage`, iOS Keychain `first_unlock` accessibility, and Android EncryptedSharedPreferences are all mobile-specific and **not ported**. The C# server port provides:
> - `InMemoryTokenProvider` — default, suitable for single-instance servers
> - `DistributedTokenProvider` — backed by `IDistributedCache` (Redis), for multi-instance deployments
> - Custom implementations via `ITokenProvider` interface
>
> Token encryption at rest is handled by Redis TLS and infrastructure-level security, not application-level AES.

> **Added from review:** The builder defaults to creating a `SecureTokenProvider()` when no `TokenProvider` is explicitly configured (`acdc_client_builder.dart:540`): `final tokenProvider = _tokenProvider ?? const SecureTokenProvider();`. This means auth is always active by default unless `disableAuth()` is called. The C# server-side port needs to decide if this default-on behavior is appropriate -- a server-side default might be `InMemoryTokenProvider` or requiring explicit configuration.

---

## 3. Token Refresh Strategies

### Strategy Interface (`token_refresh_strategy.dart`)

```dart
abstract class TokenRefreshStrategy {
  Future<TokenRefreshResult> refresh(String refreshToken);
}
```

Single method interface. Implementations must throw typed exceptions:
- `AcdcAuthException` for auth errors (invalid tokens)
- `AcdcNetworkException` for connectivity issues
- `AcdcServerException` for server errors (5xx)

### Token Refresh Result (`token_refresh_result.dart`)

```dart
class TokenRefreshResult {
  const TokenRefreshResult({
    required this.accessToken,
    this.refreshToken,       // null = no rotation, keep existing
    this.accessExpiry,       // null = unknown expiry
    this.refreshExpiry,
  });
}
```

### OAuth 2.1 Strategy (`oauth_token_refresh_strategy.dart`)

Implements the standard OAuth 2.1 refresh token grant flow:

```dart
class OAuthTokenRefreshStrategy implements TokenRefreshStrategy {
  OAuthTokenRefreshStrategy({
    required String refreshEndpointUrl,
    required String clientId,
    Dio? httpClient,   // Separate client to avoid interceptor loops
  });
```

**Request format:**
```
POST {refreshEndpointUrl}
Content-Type: application/x-www-form-urlencoded
Accept: application/json

grant_type=refresh_token&refresh_token={token}&client_id={clientId}
```

**Clock skew handling:** Uses server `Date` header when available to compute accurate expiry:

```dart
DateTime? accessExpiry;
if (expiresIn != null) {
  final dateHeader = response.headers.value('date');
  if (dateHeader != null) {
    try {
      final serverTime = DateTime.parse(dateHeader);
      accessExpiry = serverTime.add(Duration(seconds: expiresIn));
    } on FormatException {
      accessExpiry = DateTime.now().toUtc().add(Duration(seconds: expiresIn));
    }
  } else {
    accessExpiry = DateTime.now().toUtc().add(Duration(seconds: expiresIn));
  }
}
```

> **Review correction:** The clock skew code uses `DateTime.parse(dateHeader)` which parses **ISO 8601 format**, NOT RFC 1123. Standard HTTP `Date` headers use RFC 1123 format (e.g., `"Tue, 07 Feb 2026 12:00:00 GMT"`), and `DateTime.parse()` will fail to parse them, always falling back to local time. This is confirmed by `test/helpers/fake_oauth_server.dart:172` which sends the Date header in `HttpDate.format()` (RFC 1123). The tests pass because the `FormatException` is silently caught. **This is a bug in the Dart source.** The C# port should use `DateTimeOffset.ParseExact` with `"R"` format (RFC 1123) for the server Date header.

**OAuth error mapping:** Maps standard OAuth error codes to user-friendly messages:

| OAuth Error Code | Mapped Message |
|---|---|
| `invalid_grant` | "Refresh token expired or invalid. Please log in again." |
| `invalid_client` | "Client authentication failed. Check client configuration." |
| `unauthorized_client` | "Client not authorized for token refresh." |
| `unsupported_grant_type` | "Server does not support refresh token grant." |
| default | "Token refresh failed" |

**Error classification:**
- HTTP 400 with OAuth error body -> `AcdcAuthException`
- HTTP 5xx -> `AcdcServerException`
- Timeout/connection errors -> `AcdcNetworkException`

**Public client pattern:** The implementation follows OAuth 2.1 for mobile/native apps -- `client_id` is sent in the body, `client_secret` is NOT included. This is verified by the OAuth compliance integration test.

### Custom Strategy (`custom_token_refresh_strategy.dart`)

Simple wrapper that delegates to a user-provided function:

```dart
class CustomTokenRefreshStrategy implements TokenRefreshStrategy {
  CustomTokenRefreshStrategy({
    required Future<TokenRefreshResult> Function(String) refreshFn,
  }) : _refreshFn = refreshFn;

  @override
  Future<TokenRefreshResult> refresh(String refreshToken) async =>
      _refreshFn(refreshToken);
}
```

---

## 4. Auth Interceptor - Core Flow

The `AuthInterceptor` (`auth_interceptor.dart`) is the central orchestrator. It handles:

1. **Token injection** on outgoing requests
2. **Proactive refresh** before token expiry
3. **Reactive refresh** on 401 responses
4. **Retry prevention** (no infinite loops)
5. **Concurrent request queuing** during refresh

### Constructor Configuration

```dart
AuthInterceptor({
  required TokenProvider tokenProvider,
  TokenRefreshStrategy? refreshStrategy,   // Direct strategy injection
  String? refreshEndpointUrl,              // OR OAuth config
  String? clientId,
  Future<TokenRefreshResult> Function(String)? customRefreshFn, // OR custom function
  Duration refreshThreshold = const Duration(seconds: 60),      // Proactive window
  Duration refreshQueueTimeout = const Duration(seconds: 10),   // Max queue wait
  Dio? httpClient,
  AcdcLogDelegate? logDelegate,
});
```

> **Added from review:** The `AuthInterceptor` validates that `refreshThreshold` must be positive (`auth_interceptor.dart:57-59`):
> ```dart
> if (refreshThreshold.inSeconds <= 0) {
>     throw ArgumentError('refreshThreshold must be positive');
> }
> ```
> This validation should be ported to C#.

Strategy resolution priority:
1. `refreshStrategy` (direct injection)
2. `refreshEndpointUrl` + `clientId` (creates `OAuthTokenRefreshStrategy`)
3. `customRefreshFn` (creates `CustomTokenRefreshStrategy`)
4. None -> token injection only, no refresh capability

### Request Flow (`onRequest`)

```
onRequest(options, handler)
  |
  +-- Has manual Authorization header? -> Skip (pass through)
  |
  +-- Get access token from TokenProvider
  |     |
  |     +-- Token is null? -> Proceed without auth
  |     |
  |     +-- TokenProvider threw? -> Log error, proceed without auth
  |
  +-- Refresh strategy configured?
  |     |
  |     +-- Token expiry within threshold? -> Trigger proactive refresh
  |     |     |
  |     |     +-- _refreshTokenWithQueue()
  |     |     +-- Get new token, inject, proceed
  |     |
  |     +-- Token valid -> Inject existing token, proceed
  |
  +-- No strategy -> Inject existing token, proceed
```

**Manual header override:** If `Authorization` header already exists, the interceptor skips entirely:

```dart
if (AuthRequestHelper.hasManualAuthHeader(options)) {
  handler.next(options);
  return;
}
```

**Proactive refresh check:**

```dart
Future<bool> _needsProactiveRefresh() async {
  final expiry = await _tokenProvider.getAccessTokenExpiry();
  if (expiry == null) return false;  // No expiry info -> rely on reactive refresh
  final now = DateTime.now().toUtc();
  final timeUntilExpiry = expiry.difference(now);
  return timeUntilExpiry <= _refreshThreshold;  // Default: 60 seconds
}
```

### Error Flow (`onError`) - Reactive 401 Handling

```
onError(err, handler)
  |
  +-- Not 401? -> Pass through
  |
  +-- No refresh strategy? -> Pass through
  |
  +-- Is retry request? -> Clear tokens, fail with AcdcAuthException
  |     (prevents infinite loop: original -> 401 -> refresh -> retry -> 401 -> STOP)
  |
  +-- Attempt refresh via _refreshTokenWithQueue()
  |     |
  |     +-- Get new token
  |     +-- Inject into original request
  |     +-- Mark request as retry (extra['_acdc_retry_after_refresh'] = true)
  |     +-- Retry with separate Dio client
  |     +-- handler.resolve(response)  // Success!
  |
  +-- Refresh failed -> Pass error through
```

**Retry marking** prevents infinite refresh loops:

```dart
static void markAsRetry(RequestOptions options) {
  options.extra['_acdc_retry_after_refresh'] = true;
}

static bool isRetryRequest(RequestOptions options) =>
    options.extra['_acdc_retry_after_refresh'] == true;
```

**Separate retry client:** Uses a lazy-initialized `Dio()` instance (without interceptors) to retry, avoiding interceptor loops:

```dart
_retryClient ??= Dio();
final response = await _retryClient!.fetch<dynamic>(requestOptions);
handler.resolve(response);
```

> **Added from review:** When a non-DioException occurs during retry (`auth_interceptor.dart:224-230`), the **original** error `err` is passed through rather than the new exception. This means the original 401 response is returned to the caller, masking the actual refresh failure. The C# port should consider whether to preserve or change this behavior.

---

## 5. Concurrency Handling During Token Refresh

This is one of the most critical aspects of the design. When multiple requests trigger refresh simultaneously, only ONE refresh executes while others queue.

### Implementation (`_refreshTokenWithQueue`)

```dart
Completer<void>? _refreshCompleter;
bool _isRefreshing = false;

Future<void> _refreshTokenWithQueue() async {
  // If refresh is already in progress, wait for it
  if (_isRefreshing) {
    final completer = _refreshCompleter;
    if (completer != null) {
      await completer.future.timeout(
        _refreshQueueTimeout,
        onTimeout: () => throw _createAuthException('Token refresh timeout'),
      );
    }
    return;
  }

  // Start new refresh
  _isRefreshing = true;
  _refreshCompleter = Completer<void>();
  // Prevent unhandled exception if no one awaits the future when error is completed
  unawaited(_refreshCompleter!.future.catchError((_) {}));

  try {
    await _performTokenRefresh();
    _refreshCompleter?.complete();      // Signal waiting requests: success
  } catch (e) {
    _refreshCompleter?.completeError(e); // Signal waiting requests: failure
    rethrow;
  } finally {
    _isRefreshing = false;
    _refreshCompleter = null;
  }
}
```

**Key mechanics:**
- `Completer<void>` serves as a broadcast mechanism: the first request creates it, subsequent requests await its future
- `_isRefreshing` boolean gate ensures only one refresh executes
- Timeout on waiting (`refreshQueueTimeout`, default 10s) prevents indefinite blocking
- `unawaited(...catchError(...))` prevents unhandled exception warnings when the completer completes with error but no one is currently awaiting
- On success: `complete()` unblocks all waiting requests
- On failure: `completeError(e)` propagates the error to all waiting requests

### Verified by Integration Test

From `custom_refresh_function_test.dart`:

```dart
test('custom refresh function is called only once for concurrent requests', () async {
  var callCount = 0;
  Future<TokenRefreshResult> customRefresh(String refreshToken) async {
    callCount++;
    await Future<void>.delayed(const Duration(milliseconds: 100));
    return const TokenRefreshResult(accessToken: 'concurrent-access', ...);
  }
  // ... 3 concurrent requests ...
  await Future.wait(futures);
  expect(callCount, 1);  // Refresh called only ONCE
});
```

---

## 6. Backoff Strategy for Failed Refreshes

The `BackoffManager` implements exponential backoff specifically for server errors (5xx):

```dart
class BackoffManager {
  int _backoffSeconds = 0;
  DateTime? _lastAttempt;
  bool _waitSatisfied = false;
```

### Progression

```
increment() sequence: 0 -> 1 -> 2 -> 4 -> 8 -> 16 -> 30 (clamped at max)
```

```dart
void increment({int maxSeconds = 30}) {
  _backoffSeconds =
      (_backoffSeconds == 0 ? 1 : _backoffSeconds * 2).clamp(0, maxSeconds);
  _waitSatisfied = false;
}
```

### Integration with Auth Interceptor

```dart
Future<void> _performTokenRefresh() async {
  try {
    await _backoffManager.waitIfNeeded();   // Wait if backoff active
    // ... perform refresh ...
    _backoffManager.reset();                // Success -> reset backoff
  } on AcdcAuthException {
    await _clearTokensSafely();             // Auth error -> clear tokens (no backoff)
    rethrow;
  } on AcdcNetworkException {
    rethrow;                                // Network error -> no backoff, no clear
  } on AcdcServerException {
    _backoffManager.increment();            // Server error -> increase backoff
    rethrow;
  }
}
```

**Error handling differentiation:**
| Error Type | Action | Backoff | Clear Tokens |
|---|---|---|---|
| `AcdcAuthException` | Clear tokens, rethrow | No | Yes |
| `AcdcNetworkException` | Rethrow | No | No |
| `AcdcServerException` | Increment backoff, rethrow | Yes | No |

### `waitIfNeeded()` - Smart Partial Wait

```dart
Future<void> waitIfNeeded() async {
  if (_backoffSeconds > 0 && _lastAttempt != null && !_waitSatisfied) {
    final timeSinceLastAttempt = DateTime.now().difference(_lastAttempt!);
    final backoffDuration = Duration(seconds: _backoffSeconds);
    if (timeSinceLastAttempt < backoffDuration) {
      await Future<void>.delayed(backoffDuration - timeSinceLastAttempt);
    }
  }
  _lastAttempt = DateTime.now();
  _waitSatisfied = true;
}
```

If some time has already passed since the last attempt, only the remaining time is waited. The `_waitSatisfied` flag ensures a given backoff period's wait is only enforced once.

---

## 7. Auth Manager - Logout and Token Revocation

`AcdcAuthManager` provides high-level auth operations, accessible via `dio.auth` extension:

```dart
extension AcdcAuth on Dio {
  AcdcAuthManager get auth {
    final manager = options.extra['_acdc_auth_manager'] as AcdcAuthManager?;
    if (manager == null) {
      throw StateError('No TokenProvider configured...');
    }
    return manager;
  }
}
```

### `refreshNow()` Method

> **Added from review:** The `refreshNow()` method works by creating a **synthetic request** and pushing it through `_authInterceptor.onRequest()` (`acdc_auth_manager.dart:167-178`):
> ```dart
> final options = RequestOptions(path: '/refresh-trigger');
> await _authInterceptor.onRequest(options, RequestInterceptorHandler());
> ```
> This is a clever but fragile approach -- it depends on the interceptor's proactive refresh logic, which only triggers if the token is near expiry. If the token is NOT near expiry, `refreshNow()` will just inject the existing token without actually refreshing. The C# port should handle this differently with a dedicated `ForceRefresh()` method on the handler.

### Logout Flow

```dart
Future<void> logout() async {
  // 1. Cancel any in-progress token refresh
  _authInterceptor?.cancelRefresh();

  // 2. Clear cache (needs user ID from current token, so done BEFORE clearing tokens)
  await _clearCache();

  // 3. Revoke tokens (best-effort) if revocation endpoint configured
  if (_revocationEndpointUrl != null && _clientId != null) {
    await _revokeTokens();
  }

  // 4. Clear tokens from local storage
  try {
    await _tokenProvider?.clearTokens();
  } on Exception catch (e) {
    logDelegate?.log('Failed to clear tokens during logout', LogLevel.warning, ...);
  }

  // 5. Reset user tracking
  _currentUserId = null;
}
```

> **Added from review:** The `logout()` ordering is intentional: `_clearCache()` is called **before** `_revokeTokens()` and `clearTokens()` because cache clearing may need the current user ID (derived from the access token). If tokens were cleared first, the cache manager wouldn't know which user's cache to clear (`acdc_auth_manager.dart:122`).

**Best-effort revocation:** Revocation failures are logged but do NOT prevent logout from completing:

```dart
Future<void> _revokeToken(String revocationUrl, String clientId, String token, String tokenTypeHint) async {
  try {
    final dio = _httpClient ?? Dio();  // Separate client to avoid interceptor loops
    await dio.post<void>(revocationUrl, data: {
      'token': token,
      'token_type_hint': tokenTypeHint,
      'client_id': clientId,
    }, options: Options(contentType: 'application/x-www-form-urlencoded'));
  } on DioException catch (e) {
    logDelegate?.log('Failed to revoke token', LogLevel.warning, ...);
  }
}
```

> **Review correction:** The revocation request also includes an `Accept: application/json` header (`acdc_auth_manager.dart:253-256`):
> ```dart
> options: Options(
>     contentType: 'application/x-www-form-urlencoded',
>     headers: {'Accept': 'application/json'},
> ),
> ```
> This was omitted from the document snippet.

**Revocation order:** Refresh token first (higher priority since it can generate new access tokens), then access token.

> **Added from review:** `_revokeTokens()` retrieves both `refreshToken` and `accessToken` from the provider **before** any revocation begins (`acdc_auth_manager.dart:203-213`). If `getRefreshToken()` succeeds but `getAccessToken()` throws, the entire revocation is skipped. The C# port could improve on this by fetching them independently.

> **Added from review:** `_initializeUserTracking()` is called in the constructor as fire-and-forget (`acdc_auth_manager.dart:38-40, 63-66`). The `_currentUserId` may not be set by the time the first request completes. This is intentional ("best-effort") but matters for C# thread safety.

### Cancel Refresh During Logout

```dart
void cancelRefresh() {
  if (_isRefreshing && _refreshCompleter != null) {
    _refreshCompleter!.completeError(
      _createAuthException('Token refresh cancelled'),
    );
    _isRefreshing = false;
    _refreshCompleter = null;
  }
}
```

This unblocks all queued requests with an error, allowing logout to proceed cleanly.

### User Change Detection

The auth manager tracks the current user ID (extracted from JWT) and clears cache when the user changes:

```dart
Future<void> _checkUserChangeAndClearCache() async {
  final previousUserId = _currentUserId;
  await _updateCurrentUserId();
  if (previousUserId != null && _currentUserId != null && previousUserId != _currentUserId) {
    await _clearCache();
  }
}
```

---

## 8. Certificate Pinning

> **Server-only note:** Certificate pinning is typically **not needed** on server-side. Server-to-server communication usually occurs over trusted internal networks, VPNs, or service meshes that handle TLS termination. If downstream API certificate validation is needed, use `HttpClientHandler.ServerCertificateCustomValidationCallback` directly. The full `PinningVerifier`, `PinningHttpClient`, and `SpkiUtil` classes are **not ported** unless explicitly required. This section is retained for reference.

### Configuration (`certificate_pinning_config.dart`)

```dart
@immutable
class CertificatePinningConfig {
  CertificatePinningConfig({
    required this.allowedPins,        // Map<domain, List<SHA256 hashes>>
    this.reportOnly = false,          // Log-only mode (no enforcement)
    this.enablePinningInDebug = true, // Bypass for dev proxy tools
    this.onPinningFailure,            // Callback for reporting/analytics
  });
```

**Pin format:** `SHA256:<base64-encoded-hash>` (e.g., `SHA256:AgZnBoktUi/KWOA5ma+y6jW9+WtFMqZrSYtAwLQ9vW0=`)

**Validation at construction time:**
- Pin must start with `SHA256:`
- Pin must be at least 10 characters
- Domain must have at least one pin

### Verification (`pinning_verifier.dart`)

```dart
class PinningVerifier {
  PinningVerifier(this._config, {String Function(X509Certificate)? spkiExtractor})
      : spkiExtractor = spkiExtractor ?? SpkiUtil.extractSpkiHash;

  void verify(String hostname, List<X509Certificate> chain) {
    // 1. Debug bypass check
    var splitDebug = false;
    assert(() { splitDebug = true; return true; }(), 'Debug check');
    if (splitDebug && !_config.enablePinningInDebug) return;

    // 2. Find pins for host (exact match, then wildcard)
    final matchedPins = _findPinsForHost(hostname);
    if (matchedPins == null || matchedPins.isEmpty) return;  // Unpinned domain -> pass

    // 3. Check certificate chain against pins
    for (final cert in chain) {
      final hash = spkiExtractor(cert);
      if (matchedPins.contains(hash)) return;  // Match found -> pass
    }

    // 4. No match -> failure
    if (_config.reportOnly) {
      _config.onPinningFailure?.call(hostname, peerSpkiHashes);
    } else {
      throw AcdcSecurityException(...);
    }
  }
}
```

> **Review correction:** The actual verification source (`pinning_verifier.dart:55-69`) wraps the SPKI extraction in try/catch and collects hashes:
> ```dart
> for (final cert in chain) {
>     try {
>         final hash = spkiExtractor(cert);
>         peerSpkiHashes.add(hash);
>         if (matchedPins.contains(hash)) return;
>     } on Object {
>         continue;  // Skip certs that fail extraction
>     }
> }
> ```
> The `peerSpkiHashes` list is populated during iteration and passed to the failure callback/exception. The document's pseudocode omits both the error handling and hash collection.

**Wildcard matching:** `*.example.com` matches `api.example.com` but NOT `example.com` or `deep.api.example.com`:

```dart
List<String>? _findPinsForHost(String hostname) {
  // 1. Exact match
  if (_config.allowedPins.containsKey(hostname)) return _config.allowedPins[hostname];

  // 2. Wildcard match
  for (final key in _config.allowedPins.keys) {
    if (key.startsWith('*.')) {
      final domainPart = key.substring(2);
      final hostParts = hostname.split('.');
      final domainParts = domainPart.split('.');
      if (!hostname.endsWith(domainPart)) continue;
      if (hostParts.length == domainParts.length + 1) {
        return _config.allowedPins[key];
      }
    }
  }
  return null;
}
```

### SPKI Extraction (`spki_util.dart`)

Custom ASN.1/DER parser that extracts the SubjectPublicKeyInfo from X.509 certificates:

```dart
static String extractSpkiHash(X509Certificate certificate) =>
    extractSpkiHashFromBytes(certificate.der);

static String extractSpkiHashFromBytes(Uint8List der) {
  final spkiBytes = _extractSpki(der);
  final digest = sha256.convert(spkiBytes);
  return 'SHA256:${base64.encode(digest.bytes)}';
}
```

**DER structure navigation:**
```
Certificate SEQUENCE
  TBSCertificate SEQUENCE
    [0] Version (optional, tag 0xA0)
    SerialNumber (INTEGER, tag 0x02)
    Signature (SEQUENCE, tag 0x30)
    Issuer (SEQUENCE, tag 0x30)
    Validity (SEQUENCE, tag 0x30)
    Subject (SEQUENCE, tag 0x30)
    SubjectPublicKeyInfo (SEQUENCE, tag 0x30)  <-- TARGET
```

The `_DerParser` class implements a minimal ASN.1 walker that skips fields sequentially until reaching the 7th field (SPKI).

### HTTP Client Integration (`pinning_http_client.dart`)

Wraps Dart's `HttpClient` to intercept certificate validation:

```dart
class PinningHttpClient implements HttpClient {
  PinningHttpClient(this._inner, this._verifier, {this.logDelegate}) {
    _inner.badCertificateCallback = _handleBadCertificate;
  }

  bool _handleBadCertificate(X509Certificate cert, String host, int port) {
    try {
      _verifier.verify(host, [cert]);
      return true;   // Pinned & trusted
    } on AcdcSecurityException {
      return false;   // Pinning failed -> reject
    }
  }
```

**Key limitation:** `badCertificateCallback` only provides the leaf certificate, not the full chain. The verifier receives a single-element list. This is typically sufficient for leaf pinning.

> **Added from review:** The builder creates an `HttpClient` with an **empty `SecurityContext()`** (`acdc_client_builder.dart:509-524`), which bypasses the OS trust store entirely. This forces ALL certificates through the `badCertificateCallback` (since they're all "untrusted"). Combined with the `PinningVerifier`, this means verification only happens for certificates the OS considers "bad". In C#, `ServerCertificateCustomValidationCallback` is **always called** regardless of certificate validity, which is a simpler and more reliable model -- no empty trust store workaround needed.

**External callback override protection:** If external code tries to set `badCertificateCallback`, it is silently ignored with a warning log:

```dart
set badCertificateCallback(...) {
  logDelegate?.log(
    'External badCertificateCallback ignored by PinningHttpClient.',
    LogLevel.warning, ...
  );
}
```

---

## 9. User ID Extraction and JWT Utilities

### UserIdExtractor (`user_id_extractor.dart`)

Dual-mode user ID extraction with fallback chain:

```dart
class UserIdExtractor {
  const UserIdExtractor({this.userIdProvider});
  final Future<String?> Function(String accessToken)? userIdProvider;

  Future<UserIdResult> extract(String? authHeader) async {
    // 1. Parse auth header -> extract token
    // 2. Try custom userIdProvider (if configured)
    // 3. Fall back to JWT extraction via JwtUtils
  }
}
```

**Result type:**

```dart
class UserIdResult {
  final bool hasAuth;     // Whether request has auth header
  final String? userId;   // Extracted user ID (if available)
  final String? token;    // Raw token string
}
```

### JwtUtils (`jwt_utils.dart`)

Decodes JWTs without signature verification (auth server's responsibility):

```dart
static String? extractUserId(String? token) {
  final decodedToken = JwtDecoder.decode(token);
  // Priority: sub > user_id > uid
  if (decodedToken.containsKey('sub')) return decodedToken['sub'].toString();
  if (decodedToken.containsKey('user_id')) return decodedToken['user_id'].toString();
  if (decodedToken.containsKey('uid')) return decodedToken['uid'].toString();
  return null;
}
```

> **Server-only note:** On server-side, prefer `HttpContext.User.Claims` from ASP.NET Core authentication middleware over manual JWT parsing. The JWT is already validated and decoded by the auth middleware. Use `user.FindFirstValue(ClaimTypes.NameIdentifier)` or `user.FindFirstValue("sub")` to get the user ID. Manual JWT parsing via `JwtUtils` / `System.IdentityModel.Tokens.Jwt` is only needed for outgoing HTTP client calls where the server acts as a client to downstream APIs.

> **Added from review:** `UserIdExtractor._extractToken()` is case-insensitive for "Bearer" (`user_id_extractor.dart:88`): `if (trimmed.toLowerCase().startsWith('bearer '))`. This follows RFC 6750. The C# port should preserve this behavior.

> **Added from review:** `JwtUtils` lives in `lib/src/cache/` not `lib/src/auth/` or `lib/src/security/`. This is because JWT user ID extraction is primarily used for cache key isolation. The C# port should consider correct namespace placement.

---

## 10. Test Patterns and Edge Cases

### Unit Tests

**Auth Manager Tests** (`test/auth/acdc_auth_manager_test.dart`):
- Logout calls `cancelRefresh` on interceptor
- Logout clears tokens from provider
- Logout completes even if `clearTokens` throws
- Revocation skipped when endpoint not configured
- Revocation skipped when only one of endpoint/clientId is configured
- Revocation continues despite `getRefreshToken`/`getAccessToken` throwing
- Revocation request format verified (token, token_type_hint, client_id, content-type)
- `refreshNow()` triggers refresh through interceptor
- `dio.auth` extension returns manager when configured
- `dio.auth` throws `StateError` when not configured

**Backoff Manager Tests** (`test/auth/backoff_manager_test.dart`):
- Zero initial backoff, exponential progression: 1->2->4->8->16->30
- Custom max seconds, partial wait after elapsed time
- Reset clears all state, integration scenario (retry with backoff then success)

**Token Refresh Strategy Tests** (`test/auth/token_refresh_strategy_test.dart`):
- OAuth: successful refresh with/without rotation, with/without expiry
- OAuth: server time from Date header for accurate expiry
- OAuth: error mapping for all OAuth error codes
- OAuth: network timeout handling
- Custom: function invocation, exception passthrough, result handling

**Secure Token Provider Tests** (`test/auth/secure_token_provider_test.dart`):
- Uses Mockito-generated mock for FlutterSecureStorage
- Read/write/delete operations verified with correct storage keys

**Auth Manager Accessibility** (`test/auth/auth_manager_accessibility_test.dart`):
- Auth manager accessible even when auth is disabled (`disableAuth()`)
- `refreshNow()` throws `StateError` when auth is disabled

### Security Tests

**Certificate Pinning Config** (`test/security/certificate_pinning_config_test.dart`):
- Validation: empty pin list, invalid prefix, too-short pin
- Equality and hashCode

**Pinning Verifier** (`test/security/pinning_verifier_test.dart`):
- Unpinned domain passes, exact match success, chain match failure
- Wildcard matching: `*.example.com` matches `api.example.com` but NOT `example.com` or `deep.api.example.com`
- Report-only mode: no throw, callback invoked
- Debug bypass: skips verification when `enablePinningInDebug = false`

**SPKI Util** (`test/security/spki_util_test.dart`):
- Constructs minimal ASN.1/DER structure, verifies hash extraction
- Malformed DER throws `FormatException`

**User ID Extractor** (`test/security/user_id_extractor_test.dart`):
- Null/empty header, empty Bearer token, valid JWT extraction
- Custom provider, fallback from provider to JWT

### Integration Tests

**OAuth 2.1 Compliance** (`test/integration/oauth_21_compliance_test.dart`):
- Uses `FakeOAuthServer` (real HTTP server via `shelf`)
- Verifies POST method, content-type, required parameters
- Verifies `client_secret` is NOT included (public client)
- Token rotation: new refresh token stored when provided
- No rotation: original refresh token preserved

**Logout During Refresh** (`test/integration/logout_during_refresh_test.dart`):
- Slow refresh (200ms delay) + concurrent requests + logout during refresh
- Verifies tokens cleared, revocation attempted
- Logout before refresh, logout despite revocation failure
- Post-logout requests proceed without auth

**Token Provider Exception** (`test/integration/token_provider_exception_test.dart`):
- `getAccessToken` throws -> request proceeds without auth
- `getAccessTokenExpiry` throws -> no proactive refresh, token injected
- `getRefreshToken` throws -> reactive refresh fails with `AcdcAuthException`
- `setTokens` throws -> refresh "succeeds" but token not stored, request proceeds without auth
- `clearTokens` throws -> logout still completes, revocation still attempted
- All methods throwing -> complete degradation, requests work without auth
- Mixed exceptions -> partial functionality

**Certificate Pinning Integration** (`test/integration/certificate_pinning_integration_test.dart`):
- Uses self-signed HTTPS server with pre-generated certificates
- Valid pin allows connection, invalid pin aborts with `HandshakeException`
- Report-only mode allows connection with callback

**Custom Refresh Function** (`test/integration/custom_refresh_function_test.dart`):
- Proactive and reactive refresh, parameter verification
- All token fields updated (access, refresh, expiry)
- Concurrent requests -> custom function called only ONCE
- Exception in custom function -> tokens cleared

---

## 11. C# Porting Considerations

### 11.1 Token Provider -> Platform-Appropriate Storage

**Dart (Mobile):**
- `FlutterSecureStorage` -> iOS Keychain / Android Keystore
- Hardware-backed encryption, OS-managed key lifecycle
- Accessible after first device unlock

**C# Porting Options:**

| Scenario | C# Equivalent | Notes |
|---|---|---|
| Server-side (ASP.NET) | `IDistributedCache` / `IMemoryCache` | Tokens per-session/per-user in memory or Redis |
| Server-side (secure storage) | Azure Key Vault / `DPAPI` | For long-lived service credentials |
| Desktop (WPF/WinUI) | `DPAPI` via `ProtectedData` class | User-scope or machine-scope encryption |
| Desktop (cross-platform MAUI) | `SecureStorage` from MAUI Essentials | Maps to Keychain (macOS/iOS), Keystore (Android) |
| Blazor WebAssembly | `ProtectedLocalStorage` | Browser-side encrypted storage |

> **Server-only note:** Only the "Server-side (ASP.NET)" row applies. The Desktop, MAUI, and Blazor WebAssembly rows are excluded. The default `ITokenProvider` implementation should be `InMemoryTokenProvider` (thread-safe, using `ConcurrentDictionary` or `lock`). For multi-instance server deployments, provide a `DistributedTokenProvider` backed by `IDistributedCache` (Redis).

**Interface mapping:**

```csharp
public interface ITokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
    Task<string?> GetRefreshTokenAsync(CancellationToken ct = default);
    Task<DateTimeOffset?> GetAccessTokenExpiryAsync(CancellationToken ct = default);
    Task<DateTimeOffset?> GetRefreshTokenExpiryAsync(CancellationToken ct = default);
    Task SetTokensAsync(
        string accessToken,
        string? refreshToken = null,
        DateTimeOffset? accessExpiry = null,
        DateTimeOffset? refreshExpiry = null,
        CancellationToken ct = default);
    Task ClearTokensAsync(CancellationToken ct = default);
}
```

**Key differences:**
- Use `DateTimeOffset` instead of `DateTime` for timezone clarity
- Add `CancellationToken` parameters throughout
- Server-side may not need encryption-at-rest (tokens in memory), but should still clear on logout

### 11.2 Token Refresh Strategy -> IdentityModel / Custom HttpClient

**Dart:** Uses `Dio` (HTTP client) with `OAuthTokenRefreshStrategy`

**C# Options:**

1. **IdentityModel library** (`IdentityModel` NuGet package):
```csharp
var tokenClient = new HttpClient();
var response = await tokenClient.RequestRefreshTokenAsync(new RefreshTokenRequest
{
    Address = refreshEndpointUrl,
    ClientId = clientId,
    RefreshToken = refreshToken,
});
```

2. **Manual implementation** using `HttpClient`:
```csharp
var content = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["grant_type"] = "refresh_token",
    ["refresh_token"] = refreshToken,
    ["client_id"] = clientId,
});
var response = await httpClient.PostAsync(tokenEndpoint, content, ct);
```

**Key consideration:** In C#, the `HttpClient` used for refresh MUST be a separate instance from the main client (or use `IHttpClientFactory`) to avoid the DelegatingHandler/interceptor loop -- same pattern as Dart's separate `Dio()` instance.

> **Added from review:** For both the retry client and OAuth refresh client, the C# port should use `IHttpClientFactory` to avoid socket exhaustion. This should be a **firm recommendation**, not just an alternative.

### 11.3 Auth Interceptor -> DelegatingHandler

**Dart:** `Interceptor` (Dio concept)
**C#:** `DelegatingHandler` (HttpClient pipeline concept)

```csharp
public class AuthDelegatingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Token injection (same as onRequest)
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        // Reactive refresh (same as onError for 401)
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RefreshTokenWithQueue(cancellationToken);
            // Retry...
        }

        return response;
    }
}
```

**Proactive refresh in C#:** Can be done in `SendAsync` before calling `base.SendAsync`, checking `_tokenProvider.GetAccessTokenExpiryAsync()`.

> **Added from review:** `DelegatingHandler` instances are typically managed by `IHttpClientFactory` and may be pooled/reused. The C# `AuthDelegatingHandler` should NOT hold its own `HttpClient` for retries -- it should request one from the factory.

### 11.4 Concurrency Handling -> SemaphoreSlim / TaskCompletionSource

**Dart:** `Completer<void>` + `_isRefreshing` flag
**C#:** `SemaphoreSlim(1, 1)` or `TaskCompletionSource<bool>`

```csharp
private readonly SemaphoreSlim _refreshLock = new(1, 1);
private TaskCompletionSource<bool>? _refreshTcs;

private async Task RefreshTokenWithQueueAsync(CancellationToken ct)
{
    if (_refreshTcs != null)
    {
        // Wait for in-progress refresh with timeout
        var completed = await Task.WhenAny(
            _refreshTcs.Task,
            Task.Delay(TimeSpan.FromSeconds(10), ct));
        if (completed != _refreshTcs.Task)
            throw new TimeoutException("Token refresh timeout");
        return;
    }

    await _refreshLock.WaitAsync(ct);
    try
    {
        _refreshTcs = new TaskCompletionSource<bool>();
        await PerformTokenRefreshAsync(ct);
        _refreshTcs.TrySetResult(true);
    }
    catch (Exception ex)
    {
        _refreshTcs?.TrySetException(ex);
        throw;
    }
    finally
    {
        _refreshTcs = null;
        _refreshLock.Release();
    }
}
```

> **Review correction:** The C# concurrent refresh code has a race condition: between checking `_refreshTcs != null` and calling `_refreshLock.WaitAsync()`, another thread could complete the refresh and set `_refreshTcs = null`. The correct pattern should check `_refreshTcs` **inside** the lock, or use `Lazy<Task>` / `AsyncLazy<T>` pattern.

### 11.5 Backoff Manager

Direct port -- the `BackoffManager` is pure logic with no platform dependencies:

```csharp
public class BackoffManager
{
    private int _backoffSeconds;
    private DateTime? _lastAttempt;
    private bool _waitSatisfied;

    public async Task WaitIfNeededAsync(CancellationToken ct = default)
    {
        if (_backoffSeconds > 0 && _lastAttempt.HasValue && !_waitSatisfied)
        {
            var elapsed = DateTime.UtcNow - _lastAttempt.Value;
            var backoff = TimeSpan.FromSeconds(_backoffSeconds);
            if (elapsed < backoff)
                await Task.Delay(backoff - elapsed, ct);
        }
        _lastAttempt = DateTime.UtcNow;
        _waitSatisfied = true;
    }

    public void Increment(int maxSeconds = 30) {
        _backoffSeconds = Math.Clamp(
            _backoffSeconds == 0 ? 1 : _backoffSeconds * 2, 0, maxSeconds);
        _waitSatisfied = false;
    }

    public void Reset() { _backoffSeconds = 0; _waitSatisfied = false; }
}
```

> **Added from review:** The Dart `BackoffManager` is NOT thread-safe (Dart is single-threaded). The C# port needs synchronization for `_backoffSeconds`, `_lastAttempt`, and `_waitSatisfied`. Options: `lock`, `Interlocked`, or immutable instances. Also, the C# `CancellationToken` in `WaitIfNeededAsync` is an improvement over the Dart source which has no cancellation support in backoff waits.

### 11.6 Certificate Pinning -> HttpClientHandler

**Dart:** Custom `HttpClient` wrapper with `badCertificateCallback`
**C#:** `HttpClientHandler.ServerCertificateCustomValidationCallback`

```csharp
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        if (cert == null) return false;

        // Extract SPKI hash from certificate
        var spkiHash = ComputeSpkiHash(cert);

        // Check against allowed pins for the host
        var host = message.RequestUri?.Host;
        if (host != null && _allowedPins.TryGetValue(host, out var pins))
        {
            return pins.Contains(spkiHash);
        }

        // Unpinned domain -> use default validation
        return errors == SslPolicyErrors.None;
    }
};
```

**SPKI hash in C#:**

```csharp
static string ComputeSpkiHash(X509Certificate2 cert)
{
    var spkiBytes = cert.PublicKey.EncodedKeyValue.RawData;
    // OR for full SubjectPublicKeyInfo:
    // var spkiBytes = cert.GetPublicKey(); // raw public key
    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(spkiBytes);
    return $"SHA256:{Convert.ToBase64String(hash)}";
}
```

> **Review correction:** `cert.PublicKey.EncodedKeyValue.RawData` gives only the public key value bytes, NOT the full SubjectPublicKeyInfo (SPKI) structure. The SPKI includes the algorithm identifier + public key. To match the Dart behavior, the C# code needs:
> ```csharp
> var spkiBytes = cert.PublicKey.ExportSubjectPublicKeyInfo();
> ```
> The document's comment "No need for manual ASN.1 parsing" is only partially correct -- you don't need to parse DER manually, but you DO need the correct API.

**Key differences from Dart:**
- C# provides the full chain in the callback (not just leaf)
- Can validate intermediate/root certificates too
- `SslPolicyErrors` gives standard validation result alongside pinning check
- No need for manual ASN.1 parsing -- `X509Certificate2.PublicKey` provides direct access

> **Server-only note:** Certificate pinning is generally handled at infrastructure level on servers (reverse proxy, service mesh, mutual TLS). The `CertificatePinningHandler` is **not ported** by default. If needed for specific downstream API calls, the `ServerCertificateCustomValidationCallback` approach shown above can be used as-is, but this is expected to be rare in server deployments.

### 11.7 Server-Side vs Mobile Security Model Differences

| Aspect | Mobile (Dart) | Server-Side (C#) |
|---|---|---|
| **Token Storage** | Hardware-backed secure storage (Keychain/Keystore) | In-memory or distributed cache (Redis) |
| **Token Lifetime** | Long-lived (weeks/months) | Short-lived sessions, or service tokens |
| **Refresh Model** | Proactive + reactive refresh with user interaction | Service-to-service: client credentials grant, no refresh |
| **Certificate Pinning** | Essential for MITM protection on untrusted networks | Less critical; server-to-server over internal networks |
| **Secret Storage** | No client secret (public client) | Client secret in env vars or Key Vault |
| **User Context** | Single user per device | Multi-tenant, multiple users per process |
| **Concurrency** | Few concurrent requests | High concurrency; thread safety critical |
| **Token Revocation** | Best-effort on logout | Token introspection or reference tokens |
| **Key Rotation** | Pin rotation requires app update | Certificate rotation is server config |
| **Proxy/Debug** | Debug bypass for Charles/Fiddler | Not applicable |

> **Server-only note:** Only the "Server-Side (C#)" column applies for this port. Key decisions:
> - **Token storage**: In-memory or Redis — no hardware-backed storage
> - **Refresh model**: Service-to-service calls may use client credentials grant (no refresh tokens needed). User-context calls use delegated tokens from the incoming request.
> - **Concurrency**: High concurrency is the norm — all auth components must be thread-safe (`SemaphoreSlim`, `ConcurrentDictionary`, `Interlocked`)
> - **Secret storage**: Client secrets in environment variables, Azure Key Vault, or `IConfiguration` with secret providers — never in code
> - **User context**: Multi-tenant — use `HttpContext.User.Claims` for user identity, not JWT parsing

> **Added from review:** The `AuthRequestHelper` uses string-based keys (`_acdc_retry_after_refresh`, `_acdc_auth_manager`) stored in `extras`/`options`. In C#, these should use typed keys via `HttpRequestMessage.Options` with `HttpRequestOptionsKey<T>`, not magic strings.

> **Added from review:** The `dio.auth` extension pattern has no direct C# equivalent. Options for the C# port:
> - Extension method on `HttpClient` reading from a `ConcurrentDictionary`
> - Wrapper class (e.g., `AcdcHttpClient`) exposing an `.Auth` property
> - DI approach where `IAuthManager` is injected separately (recommended for server-side)

### 11.8 Files to Create in C# Port

Based on this analysis, the C# port should include:

```
src/Auth/
  ITokenProvider.cs              # Interface (from TokenProvider)
  InMemoryTokenProvider.cs       # Default server-side implementation
  TokenRefreshResult.cs          # DTO (direct port)
  ITokenRefreshStrategy.cs       # Interface (from TokenRefreshStrategy)
  OAuthTokenRefreshStrategy.cs   # OAuth 2.1 implementation
  CustomTokenRefreshStrategy.cs  # Delegate wrapper
  AuthManager.cs                 # Logout, refresh, user change detection
  BackoffManager.cs              # Direct port

src/Handlers/
  AuthDelegatingHandler.cs       # DelegatingHandler (from AuthInterceptor)
  AuthRequestHelper.cs           # Static utilities (direct port)

src/Security/
  CertificatePinningConfig.cs    # Configuration model
  CertificatePinningHandler.cs   # HttpClientHandler setup
  SpkiUtil.cs                    # SPKI hash computation (simplified in C#)
  UserIdExtractor.cs             # JWT user ID extraction
```

> **Server-only note:** The following files from the listing are **not created** in the server port:
> - `CertificatePinningConfig.cs` — not ported (infrastructure handles TLS)
> - `CertificatePinningHandler.cs` — not ported
> - `SpkiUtil.cs` — not ported (no manual SPKI extraction needed)
>
> The `UserIdExtractor.cs` should prefer `HttpContext.User.Claims` over JWT parsing. Only use JWT parsing for outgoing calls to downstream APIs where the token is forwarded.

### 11.9 External Dependencies Mapping

| Dart Package | C# Equivalent | Notes |
|---|---|---|
| `dio` | `HttpClient` + `DelegatingHandler` | Built-in .NET |
| `flutter_secure_storage` | `DPAPI` / `SecureStorage` / `IDistributedCache` | Platform-dependent |
| `crypto` (SHA-256) | `System.Security.Cryptography.SHA256` | Built-in .NET |
| `jwt_decoder` | `System.IdentityModel.Tokens.Jwt` | Microsoft NuGet package |
| N/A | `IdentityModel` | Optional: OAuth token endpoint client |

> **Server-only note:** The following Dart dependencies are **excluded** from the C# server port:
> - `flutter_secure_storage` → Replaced by `InMemoryTokenProvider` or `IDistributedCache`
> - `crypto` (SHA-256 for SPKI) → Not needed (no certificate pinning)
> - `jwt_decoder` → Prefer `HttpContext.User.Claims`; only use `System.IdentityModel.Tokens.Jwt` for downstream API token forwarding

---

## Summary of Key Porting Decisions

1. **TokenProvider interface** maps cleanly to `ITokenProvider` with `CancellationToken` added
2. **Concurrency model** changes from `Completer<void>` to `SemaphoreSlim` + `TaskCompletionSource`
3. **Certificate pinning** is simpler in C# (no ASN.1 parser needed) via `X509Certificate2.PublicKey`
4. **DelegatingHandler** replaces Dio interceptor; proactive + reactive refresh both fit in `SendAsync`
5. **BackoffManager** is a direct port with no platform dependencies
6. **Server-side deployment** changes the security model significantly: no hardware-backed storage, multi-tenant concerns, client credentials flow may replace refresh tokens
7. **OAuth 2.1 compliance** tests should be ported as integration tests
8. **Graceful degradation** pattern (auth failures don't crash requests) must be preserved
