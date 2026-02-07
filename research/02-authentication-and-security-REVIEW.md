# Review: 02-authentication-and-security.md

> Reviewer: reviewer-arch (cross-review)
> Date: 2026-02-07
> Reviewed against: Dart-ACDC source code at `/Users/joaomiranda/dev/Dart-ACDC/`

---

## Overall Assessment

The document is **thorough and well-structured**. It accurately describes the architecture, covers most key patterns, and provides useful C# porting guidance. The code snippets are largely correct. However, there are several factual inaccuracies, missing behaviors, and porting gaps identified below.

**Rating: 8/10** -- solid foundation with corrections needed before use as a porting reference.

---

## 1. Accuracy Issues

### 1.1 Clock Skew Handling Uses `DateTime.parse()` -- Not RFC 1123

**File:** `oauth_token_refresh_strategy.dart:86-87`

The document correctly shows the clock skew code, but the description says "Parse HTTP date format (RFC 1123)" (matching the source comment at line 85). However, the actual code uses `DateTime.parse(dateHeader)` which parses **ISO 8601 format**, NOT RFC 1123 (e.g., `"Tue, 07 Feb 2026 12:00:00 GMT"`). Standard HTTP `Date` headers are RFC 1123 and `DateTime.parse()` will **fail** to parse them, causing the code to always fall back to local time.

This is a **bug in the Dart source** that the document should call out explicitly for the C# port. In C#, the porting guide should use `DateTimeOffset.ParseExact` with `"R"` format (RFC 1123) for the server Date header, or `HttpDateParser`.

**Recommendation:** Add a note in the porting guide flagging this as a Dart-side bug to fix correctly in the C# implementation.

### 1.2 `setTokens` Partial Update Semantics -- Document Slightly Misleading

**Document Section 2** states: "`setTokens` uses optional parameters for partial updates (e.g., rotation may only update refresh token)."

The actual behavior in `secure_token_provider.dart:60-74` is:
- `accessToken` is **always** written (it is required)
- `refreshToken`, `accessExpiry`, `refreshExpiry` are only written when **non-null**
- Critically, if `refreshToken` is null, the **old refresh token is preserved** in storage (it is not deleted)

However, the `TestTokenProvider` in `logout_during_refresh_test.dart:240-249` has **different semantics**: `setTokens` **always overwrites** `_refreshToken` with the parameter value (even if null), which means it would delete the old refresh token on a non-rotation refresh. The `TestTokenProvider` in `oauth_21_compliance_test.dart:293-305` correctly preserves old refresh tokens when null (using `if (refreshToken != null)`).

This inconsistency between test implementations is worth noting as it reveals a subtle contract ambiguity that the C# port must define clearly.

### 1.3 Builder Defaults to `SecureTokenProvider` When No Provider Is Given

**File:** `acdc_client_builder.dart:540`

The document does not mention that if no `TokenProvider` is explicitly configured via `withTokenProvider()`, the builder **defaults to creating a `SecureTokenProvider()`**:

```dart
final tokenProvider = _tokenProvider ?? const SecureTokenProvider();
```

This means auth is always active by default (unless `disableAuth()` is called). The C# port needs to decide if this default-on behavior is appropriate for a server-side context.

### 1.4 `refreshNow()` Implementation Detail -- Slightly Misleading Description

**File:** `acdc_auth_manager.dart:167-178`

The document does not explain the actual mechanism of `refreshNow()`. It works by creating a **synthetic request** and pushing it through `_authInterceptor.onRequest()`:

```dart
final options = RequestOptions(path: '/refresh-trigger');
await _authInterceptor.onRequest(options, RequestInterceptorHandler());
```

This is a clever but fragile approach -- it depends on the interceptor's proactive refresh logic (which only triggers if the token is near expiry). If the token is NOT near expiry, `refreshNow()` will just inject the existing token and complete without actually refreshing. This is an important behavioral detail that the C# port should handle differently, perhaps with a dedicated `ForceRefresh()` method on the handler.

---

## 2. Missing Content

### 2.1 Builder Wiring -- How Components Connect

The document does not describe how the builder (`acdc_client_builder.dart`) wires together the `AuthInterceptor`, `AcdcAuthManager`, and `TokenProvider`. Key details:

- **File:** `acdc_client_builder.dart:553-563` -- `AuthInterceptor` is created only when `_authDisabled` is false
- **File:** `acdc_client_builder.dart:575-582` -- `AcdcAuthManager` receives the auth interceptor, revocation endpoint, client ID, **and cache manager**
- **File:** `acdc_client_builder.dart:585` -- Auth manager is stored in `dio.options.extra['_acdc_auth_manager']`
- **File:** `acdc_client_builder.dart:509-524` -- Certificate pinning creates a custom `IOHttpClientAdapter` with an **empty SecurityContext** (forces all certs through verification)

The empty `SecurityContext()` approach at line 512 is critical for security -- it means the OS trust store is bypassed entirely, so ALL certificates must match a pin. This is not documented and is an important porting consideration.

### 2.2 Interceptor Order Is Not Documented

**File:** `acdc_client_builder.dart:614-653`

The interceptor chain order is:
1. LoggingInterceptor (outermost)
2. ErrorInterceptor
3. CancellationInterceptor
4. OfflineInterceptor
5. AuthInterceptor
6. CacheInterceptor
7. Custom interceptors
8. DeduplicationInterceptor (innermost)

The document mentions "Must run before ErrorInterceptor in response chain" for AuthInterceptor but does not explain the full chain. For the C# port (DelegatingHandler chain), the order of handlers matters significantly.

### 2.3 `_retryClient` Has No Interceptors -- Not Just "Separate"

**File:** `auth_interceptor.dart:221-222`

The document says the retry client is "separate" but doesn't emphasize that `Dio()` creates a client with **no interceptors at all**. This means:
- No logging on retry requests
- No error mapping on retry responses
- No caching of retry responses
- No offline detection on retry

For the C# port, the retry `HttpClient` needs careful consideration -- should it share the handler pipeline minus the auth handler?

### 2.4 `PinningVerifier.verify()` -- Hash Extraction Error Handling

**File:** `pinning_verifier.dart:55-69`

The document's pseudocode for verification omits an important detail: if `spkiExtractor` throws for a cert, the verifier catches the exception with `on Object` and continues to the next cert in the chain. This means a single bad cert in the chain does not cause verification failure -- only if ALL certs fail extraction AND none match does it fail. The document simplifies this away.

### 2.5 `PinningHttpClient` -- `badCertificateCallback` Only Gets Leaf Certificate

The document mentions this limitation but does not explain the full implication: the verifier's `verify()` method only gets called via `badCertificateCallback` for certificates the OS considers "bad" (untrusted root, hostname mismatch, etc). For certificates the OS considers valid, `badCertificateCallback` is **not called at all**. Combined with the empty SecurityContext in the builder (which makes all certs "bad"), this works correctly. But this architectural dependency is fragile and should be documented for the C# port.

In C#, `ServerCertificateCustomValidationCallback` is **always called** regardless of certificate validity, which is a simpler and more reliable model.

### 2.6 `AcdcAuthManager._initializeUserTracking()` Is Fire-and-Forget

**File:** `acdc_auth_manager.dart:38-40, 63-66`

The constructor calls `_initializeUserTracking()` which calls `_updateCurrentUserId()` (an async method) **without awaiting it**. This means user ID tracking initialization is fire-and-forget, and the `_currentUserId` may not be set by the time the first request completes. This is intentional ("best-effort") but is not documented and matters for the C# port.

### 2.7 `AcdcAuthException` Handles Both 401 and 403

**File:** `acdc_auth_exception.dart:6, 56-64`

The document mentions `AcdcAuthException` for 401 but does not note that it also handles 403 (Forbidden) with a separate message. The `_defaultMessage` method generates different messages for 401 vs 403:
- 401: "Authentication failed: Invalid or expired token"
- 403: "Authorization failed: Insufficient permissions"

### 2.8 URL Redaction in Auth Exceptions

**File:** `acdc_auth_exception.dart:32-37`

When creating `AcdcAuthException.fromDioException`, the URL is redacted via `AcdcException.redactUrl()` and the response body is truncated via `AcdcException.truncateResponseBody()`. These security-conscious patterns should be carried over to the C# port.

### 2.9 `FakeOAuthServer` Returns `Date` Header in `HttpDate.format`

**File:** `test/helpers/fake_oauth_server.dart:172`

The fake OAuth server returns the Date header using `HttpDate.format(DateTime.now().toUtc())`, which formats in RFC 1123 format. This confirms the bug noted in 1.1 -- the server sends RFC 1123, but the client tries to parse with `DateTime.parse()` (ISO 8601). The tests pass because the `FormatException` is silently caught and falls back to local time.

---

## 3. C# Porting Gaps

### 3.1 SPKI Hash Computation in C# Is More Complex Than Shown

**Document Section 11.6** shows:
```csharp
var spkiBytes = cert.PublicKey.EncodedKeyValue.RawData;
```

This is **incorrect**. `EncodedKeyValue.RawData` gives only the public key value bytes, NOT the full SubjectPublicKeyInfo (SPKI) structure. The SPKI includes the algorithm identifier + public key. To match the Dart implementation's behavior (which hashes the full SPKI TLV from the DER), the C# code needs:

```csharp
// Full SPKI bytes from the certificate
var spkiBytes = cert.PublicKey.ExportSubjectPublicKeyInfo();
```

Or parse the certificate DER to extract the SPKI field. The document's comment "No need for manual ASN.1 parsing" is only partially correct -- you don't need to parse DER manually, but you DO need the correct API.

### 3.2 `SemaphoreSlim` + `TaskCompletionSource` Pattern Has a Race Condition

**Document Section 11.4** shows:
```csharp
if (_refreshTcs != null)
{
    // Wait for in-progress refresh...
    return;
}
await _refreshLock.WaitAsync(ct);
```

There is a race condition: between checking `_refreshTcs != null` and calling `_refreshLock.WaitAsync()`, another thread could complete the refresh and set `_refreshTcs = null`. The correct pattern should check `_refreshTcs` **inside** the lock, or use a different approach:

```csharp
await _refreshLock.WaitAsync(ct);
try
{
    // Double-check: another thread may have refreshed while we waited
    if (!NeedsRefresh()) return;

    _refreshTcs = new TaskCompletionSource<bool>();
    // ... perform refresh ...
}
finally
{
    _refreshLock.Release();
}
```

Or better yet, use `Lazy<Task>` / `AsyncLazy<T>` pattern for simpler thread-safe initialization.

### 3.3 Missing: `CancellationToken` Propagation in Backoff Wait

**Document Section 11.5** correctly adds `CancellationToken` to `WaitIfNeededAsync` but the Dart source has no cancellation support in the backoff wait (`backoff_manager.dart:40`). The C# port should pass the cancellation token to `Task.Delay()` to allow cancellation during backoff waits. This is correctly shown in the document but should be explicitly called out as an improvement over the Dart implementation.

### 3.4 Missing: Thread Safety for `BackoffManager` in C#

The Dart `BackoffManager` is not thread-safe (Dart is single-threaded per isolate). The C# port operates in a multi-threaded environment, so `_backoffSeconds`, `_lastAttempt`, and `_waitSatisfied` all need synchronization. Options:
- Use `lock` around all state access
- Use `Interlocked` for atomic operations
- Make the class immutable and return new instances

### 3.5 Missing: `IHttpClientFactory` Recommendation

For the retry client and OAuth refresh client (both of which create separate `Dio()` / `HttpClient` instances in Dart), the C# port should use `IHttpClientFactory` to avoid socket exhaustion. The document mentions this briefly ("or use `IHttpClientFactory`") but should make it a firm recommendation, not an alternative.

### 3.6 Missing: DelegatingHandler Lifecycle Considerations

In Dart, the `AuthInterceptor` holds a `_retryClient` field that persists for the interceptor's lifetime. In C#, `DelegatingHandler` instances are typically managed by `IHttpClientFactory` and may be pooled/reused. The C# `AuthDelegatingHandler` should not hold its own `HttpClient` for retries -- it should request one from the factory.

### 3.7 Missing: `AuthRequestHelper` Static Key Names

**File:** `auth_request_helper.dart:44, 50`

The retry flag key `_acdc_retry_after_refresh` and the auth manager key `_acdc_auth_manager` are string-based keys stored in `extras`/`options`. In C#, these should be typed via `HttpRequestMessage.Properties` (or `HttpRequestMessage.Options` in .NET 5+) with a proper typed key, not magic strings.

### 3.8 Missing: `AcdcAuth` Extension -- C# Equivalent

The document does not discuss how the `dio.auth` extension pattern maps to C#. In C#, there is no direct equivalent of Dart extensions on `HttpClient`. Options:
- Extension method on `HttpClient` reading from a `ConcurrentDictionary`
- Wrapper class (e.g., `AcdcHttpClient`) that exposes `.Auth` property
- Service locator / DI approach where `IAuthManager` is injected separately

### 3.9 Missing: Certificate Pinning Empty Trust Store Pattern

**File:** `acdc_client_builder.dart:512`

The document does not explain that the Dart implementation creates an `HttpClient` with an **empty `SecurityContext()`** to force ALL certificates through the `badCertificateCallback`. In C#, `ServerCertificateCustomValidationCallback` is always invoked, so this workaround is not needed. But the document should explicitly note this Dart-specific workaround and explain why it is unnecessary in C#.

---

## 4. Corrections

### 4.1 Section 8 -- Verification Pseudocode Missing Error Handling

The verification pseudocode in Section 8 shows:
```dart
for (final cert in chain) {
    final hash = spkiExtractor(cert);
    if (matchedPins.contains(hash)) return;  // Match found -> pass
}
```

But the actual source (`pinning_verifier.dart:55-69`) wraps the extraction in try/catch:
```dart
for (final cert in chain) {
    try {
        final hash = spkiExtractor(cert);
        peerSpkiHashes.add(hash);
        if (matchedPins.contains(hash)) return;
    } on Object {
        continue;  // Skip certs that fail extraction
    }
}
```

The `peerSpkiHashes` list is also populated during iteration and passed to the failure callback/exception. The document's pseudocode omits both the error handling and hash collection.

### 4.2 Section 7 -- Revocation Request Missing `Accept` Header

The document's revocation code snippet shows:
```dart
options: Options(contentType: 'application/x-www-form-urlencoded')
```

But the actual source (`acdc_auth_manager.dart:253-256`) also includes:
```dart
options: Options(
    contentType: 'application/x-www-form-urlencoded',
    headers: {'Accept': 'application/json'},
),
```

The `Accept: application/json` header is present in the source but omitted from the document snippet.

### 4.3 Section 4 -- `refreshThreshold` Validation

**File:** `auth_interceptor.dart:57-59`

The document does not mention that `AuthInterceptor` validates `refreshThreshold` must be positive:
```dart
if (refreshThreshold.inSeconds <= 0) {
    throw ArgumentError('refreshThreshold must be positive');
}
```

This validation should be ported to C#.

### 4.4 Section 5 -- `_refreshCompleter` Guarded by `catchError`

The document correctly includes the `unawaited(_refreshCompleter!.future.catchError((_) {}))` line (line 278 in `auth_interceptor.dart`) and explains its purpose. This is good -- but should note that in C#, `TaskCompletionSource` does not have this problem because `TrySetException` does not throw if no one is awaiting.

---

## 5. Additional Insights From Source Code

### 5.1 `logout()` Clears Cache BEFORE Clearing Tokens

**File:** `acdc_auth_manager.dart:122`

The ordering is intentional: `_clearCache()` is called before `_revokeTokens()` and `clearTokens()` because cache clearing may need the current user ID (derived from the access token). If tokens were cleared first, the cache manager might not know which user's cache to clear.

### 5.2 `_revokeTokens()` Gets Both Tokens Before Revoking Either

**File:** `acdc_auth_manager.dart:203-213`

Both `refreshToken` and `accessToken` are retrieved from the provider **before** any revocation begins. If `getRefreshToken()` succeeds but `getAccessToken()` throws, the entire revocation is skipped (the catch returns early). This means a failure to read one token prevents revocation of the other. The C# port could improve on this by fetching them independently.

### 5.3 `AuthInterceptor.onError` -- Non-DioException Handling

**File:** `auth_interceptor.dart:224-230`

When a refresh attempt throws a `DioException`, it is passed to `handler.next(e)`. But when a non-DioException occurs (line 227-229), the **original** error `err` is passed through, not the new exception. This means the original 401 response is returned to the caller, masking the actual refresh failure. The C# port should consider whether this is the desired behavior.

### 5.4 `_needsProactiveRefresh()` Catches ALL Exceptions From `getAccessTokenExpiry()`

**File:** `auth_interceptor.dart:240-257`

If `_tokenProvider.getAccessTokenExpiry()` throws any exception, proactive refresh is silently skipped (returns false). This is documented in the `token_provider_exception_test.dart` integration test. The C# port should preserve this resilience pattern.

### 5.5 The `refreshQueueTimeout` Default Is 10 Seconds

**File:** `auth_interceptor.dart:50`

The document mentions this correctly but does not highlight that this timeout is separate from the HTTP request timeout. A slow token refresh endpoint could cause queued requests to timeout after 10 seconds even if the actual refresh is still in progress. The C# port should consider making this configurable relative to the HTTP timeout.

### 5.6 `UserIdExtractor._extractToken()` Is Case-Insensitive for "Bearer"

**File:** `user_id_extractor.dart:88`

```dart
if (trimmed.toLowerCase().startsWith('bearer ')) {
```

The "Bearer" prefix check is case-insensitive per RFC 6750. The C# port should preserve this behavior.

### 5.7 `JwtUtils` Lives in `cache/` Not `auth/` or `security/`

**File:** `lib/src/cache/jwt_utils.dart`

The document lists `JwtUtils` under Section 9 (User ID Extraction and JWT Utilities) but does not note that it is in the `cache/` directory, not `auth/` or `security/`. This is because JWT user ID extraction is primarily used for cache key isolation. The C# port should consider the correct namespace placement.

---

## 6. Summary of Required Changes Before Porting

| Priority | Item | Section |
|----------|------|---------|
| **High** | Fix SPKI hash computation in C# (use correct API for full SPKI) | 3.1 |
| **High** | Fix race condition in SemaphoreSlim/TCS concurrency pattern | 3.2 |
| **High** | Flag clock skew `DateTime.parse()` bug for correct C# implementation | 1.1 |
| **High** | Add thread safety to BackoffManager for C# | 3.4 |
| **Medium** | Document builder wiring and interceptor order for handler chain | 2.1, 2.2 |
| **Medium** | Use `IHttpClientFactory` for retry and refresh clients | 3.5 |
| **Medium** | Document empty SecurityContext pattern and C# equivalent | 2.5, 3.9 |
| **Medium** | Add `refreshNow()` behavioral caveat | 1.4 |
| **Medium** | Fix verification pseudocode (add error handling, hash collection) | 4.1 |
| **Medium** | Add Accept header to revocation snippet | 4.2 |
| **Low** | Note `setTokens` contract ambiguity between implementations | 1.2 |
| **Low** | Document fire-and-forget user tracking initialization | 2.6 |
| **Low** | Use typed keys instead of magic strings for request properties | 3.7 |
| **Low** | Design C# equivalent of `dio.auth` extension | 3.8 |
