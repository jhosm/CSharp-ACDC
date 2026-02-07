# Review: 04-exceptions-tests-dependencies.md

**Reviewer**: reviewer-cache (cross-review)
**Date**: 2026-02-07
**Verdict**: Good overall quality with several inaccuracies, notable omissions, and missing C# porting considerations.

---

## 1. Accuracy Check

### 1.1 Exception Hierarchy (Section 1.1) -- ACCURATE with caveat

The class diagram is correct. All six subclasses are listed and the hierarchy is faithfully represented. However:

**Issue**: `AcdcSecurityException` is listed in the hierarchy and described in Section 1.4, but the document does NOT flag that it is **not exported** from the public barrel file (`lib/dart_acdc.dart`). The barrel file exports `CertificatePinningConfig` (line 265-266) but does NOT export `AcdcSecurityException`. This is a significant omission because:
- It means `AcdcSecurityException` is effectively an **internal** type
- Consumers cannot catch it by type unless they import the `src/` path directly (which violates the strict export policy)
- The `public_api_test.dart` (line 57-64) does NOT check for `AcdcSecurityException` in its "All exception types are exported" test
- This is likely a bug in the Dart library, or a deliberate choice that the document should call out

**Impact on C# port**: If it is a bug, we should export it. If deliberate, the C# port should decide whether `AcdcSecurityException` belongs in the public API.

### 1.2 Base Exception Fields (Section 1.3) -- ACCURATE

The field table is correct. The `toMap()` and `truncateResponseBody()` snippets match the source code exactly (`acdc_exception.dart:52-72`).

### 1.3 AcdcException Default DioExceptionType (Section 1.3) -- MISSING

The document fails to mention that `AcdcException` defaults `super.type` to `DioExceptionType.unknown` (line 25 of `acdc_exception.dart`). This is important because each subclass overrides this default differently:
- `AcdcAuthException` -> `DioExceptionType.badResponse` (line 20)
- `AcdcClientException` -> `DioExceptionType.badResponse` (line 26)
- `AcdcServerException` -> `DioExceptionType.badResponse` (line 20)
- `AcdcNetworkException` -> uses `originalException.type` (line 42, preserves the original Dio type)
- `AcdcCacheException` -> `DioExceptionType.unknown` (line 36)
- `AcdcSecurityException` -> inherits default `DioExceptionType.unknown`

This mapping is porting-relevant: the C# equivalent needs to decide how to represent the error type/category.

### 1.4 AcdcAuthException.fromDioException Custom Message -- ACCURATE

The document correctly shows the optional `message` parameter in the factory constructor (Section 1.4, lines 111-116). Verified against `acdc_auth_exception.dart:26-29`.

### 1.5 AcdcClientException Retry-After Parsing -- MOSTLY ACCURATE, minor imprecision

The document shows the `_parseRetryAfter` code snippet but omits the initial extraction from `Headers` object. The actual code at `acdc_client_exception.dart:87-108` first checks `headers['retry-after']` which returns a `List<String>?`, then takes `.first`. This is a Dio-specific pattern where headers are stored as lists. The C# port should note that `HttpResponseMessage.Headers.RetryAfter` provides a strongly-typed `RetryConditionHeaderValue` which may be a better approach than manual parsing.

### 1.6 AcdcNetworkException `originalException` Required -- ACCURATE

The document correctly notes at Section 1.4 that `originalException` is required (not nullable) for `AcdcNetworkException`. Verified at `acdc_network_exception.dart:37` where it uses `required DioException super.originalException`.

### 1.7 AcdcSecurityException -- MOSTLY ACCURATE

The description matches the source (`acdc_security_exception.dart`). However, the document says the default message is `'Security check failed'` (line 9 of the source) but does not note that this is set via a **named parameter with a default value** (`super.message = 'Security check failed'`), not a required parameter. This means callers can override the message.

Also missing: `AcdcSecurityException` does NOT call `super.type` explicitly, so it uses the base class default of `DioExceptionType.unknown`. It also does NOT have a `fromDioException()` factory -- it is only created directly in `PinningVerifier.verify()` (`lib/src/security/pinning_verifier.dart:73-79`).

### 1.8 Code Snippets -- ACCURATE

All code snippets in the document match the actual source code. The `toMap()`, `truncateResponseBody()`, `redactUrl()` code matches. The Retry-After parsing logic matches (though abbreviated).

---

## 2. Missing Content

### 2.1 ErrorInterceptor -- Critically Incomplete

The document mentions `ErrorInterceptor` briefly in Section 2.5 ("This keeps the ErrorInterceptor clean -- it just calls the appropriate factory") but does NOT describe the `ErrorInterceptor` itself (`lib/src/interceptors/error_interceptor.dart`), which is a **central component** of the exception system. The error interceptor:

1. **Checks network errors first** (lines 35-36), before checking HTTP status codes
2. **Handles malformed responses** as `AcdcClientException` with message "Invalid response format from server" (lines 40-42, 116-137)
3. **Handles 3xx redirects** as `AcdcClientException` when redirects are disabled (lines 48-49, 140-156)
4. **Falls through to return original DioException** for unrecognized cases (line 70)
5. **Detects hidden network errors**: Checks `DioExceptionType.unknown` errors for string patterns like "socketexception", "failed host lookup", "network is unreachable", "connection refused", "connection reset" (lines 86-97)
6. **Does NOT handle `DioExceptionType.badCertificate`** -- this falls through to the string-matching check and then returns as the original DioException. This means certificate errors from Dio's built-in TLS validation are NOT converted to `AcdcSecurityException`.

This is crucial for the C# port because the `DelegatingHandler` equivalent needs to implement this same routing logic.

### 2.2 ErrorInterceptor Test Coverage -- MISSING

The document lists the exception tests in Section 3.1 but does NOT list `test/interceptors/error_interceptor_test.dart`, which is a comprehensive test file (274 lines) covering:
- All status code mappings (401, 403, 4xx, 5xx)
- All network error types (timeout, cancel, connection error)
- Edge cases: malformed responses, 3xx redirects, non-standard status codes (418, 451, 599)

This test file is important because it tests the **routing logic** that determines which exception type is created.

### 2.3 `AcdcSecurityException` Not Being Thrown by ErrorInterceptor

The document does not mention that `AcdcSecurityException` is thrown exclusively by `PinningVerifier.verify()` (`lib/src/security/pinning_verifier.dart:83`), NOT by the `ErrorInterceptor`. This means it has a completely different throw path from all other exceptions. The error interceptor does NOT convert `DioExceptionType.badCertificate` to `AcdcSecurityException`.

For C# porting, this means the security exception needs to be handled in the certificate validation callback, not in the DelegatingHandler error pipeline.

### 2.4 Missing Test File: `acdc_security_exception` Tests

There is NO dedicated test file for `AcdcSecurityException` in `test/exceptions/`. The security exception is only tested indirectly via:
- `test/security/pinning_verifier_test.dart`
- `test/security/pinning_http_client_test.dart`
- `test/integration/certificate_pinning_integration_test.dart`

The document should flag this gap.

### 2.5 `AcdcCacheException.originalException` is Required

The document correctly shows that cache exception factory constructors create synthetic `DioException` wrappers. However, it does not explicitly call out that the `originalException` parameter in `AcdcCacheException` is declared as `required super.originalException` (line 30 of `acdc_cache_exception.dart`), making it non-nullable -- unlike the base `AcdcException` where it is optional. This is important for the C# port: the cache exception always wraps a `DioException`, even if the original error was not HTTP-related.

### 2.6 `AcdcNetworkException` Passes `originalException.type` to Super

The document mentions the `NetworkErrorType` mapping but does not call out that `AcdcNetworkException` passes `type: originalException.type` to the `super` constructor (line 42 of `acdc_network_exception.dart`). This preserves the original Dio exception type in the base class, which is different from other subclasses that hardcode their type (e.g., `DioExceptionType.badResponse`).

### 2.7 `AcdcServerException` Does Not Override `toMap()`

The document says "Subclasses extend the base map with their specific fields" (Section 2.4). While true for `AcdcClientException`, `AcdcNetworkException`, and `AcdcCacheException`, it is NOT true for `AcdcServerException` and `AcdcAuthException`, which do NOT override `toMap()`. They inherit the base implementation, so their maps do NOT include any subclass-specific data.

### 2.8 Example File `MyLogDelegate.log()` Signature Mismatch

In `example/example.dart:159-162`, the `MyLogDelegate.log()` method has the signature:
```dart
void log(String message, LogLevel level, [Map<String, dynamic>? metadata])
```
Note the **optional positional** `metadata` parameter (with `?`). But the actual `AcdcLogDelegate` interface (`lib/src/logging/acdc_log_delegate.dart:25`) defines:
```dart
void log(String message, LogLevel level, Map<String, dynamic> metadata);
```
With a **required** non-nullable `metadata`. The example compiles because Dart allows a supertype signature (optional parameter matches required parameter), but this inconsistency could confuse C# port developers.

---

## 3. C# Porting Gaps

### 3.1 Exception Hierarchy Recommendation -- Needs More Consideration

**Section 8.1 (Option A: Extend `HttpRequestException`)** is reasonable but has a significant gap:

The document correctly notes that `HttpRequestException.StatusCode` is `HttpStatusCode?` (an enum) while Dart uses `int?`. However, it does not address that `HttpRequestException` in .NET does NOT have:
- A built-in `InnerException` typed as `HttpRequestException` (would need `OriginalException` as a separate property)
- The `StatusCode` property was only added in .NET 5.0. The document should specify minimum .NET version.

Additionally, the document recommends extending `HttpRequestException` for `AcdcCacheException` "for hierarchy consistency", but this creates a semantic problem: a cache read failure is not an HTTP request exception. In C#, it might be cleaner to have `AcdcCacheException` extend `AcdcException` which extends `Exception` (not `HttpRequestException`), breaking with the Dart pattern where this was forced by DioException inheritance.

### 3.2 Missing: `DelegatingHandler` Error Mapping Implementation

The document provides the high-level `DelegatingHandler` mapping (Section 5.3) but does NOT show how the ErrorInterceptor's routing logic should be implemented. In C#:

```csharp
// The ErrorInterceptor equivalent needs to handle:
// 1. HttpRequestException (network errors) -> AcdcNetworkException
// 2. TaskCanceledException (timeouts/cancellation) -> AcdcNetworkException
// 3. HttpResponseMessage with 4xx/5xx status -> AcdcClientException/AcdcServerException/AcdcAuthException
```

Key difference: In C#, `HttpClient` throws `HttpRequestException` for network errors and `TaskCanceledException` for timeouts, whereas Dio uses `DioExceptionType` enum for all cases. The document should map these C# exception types to the ACDC exception hierarchy.

### 3.3 Missing: Retry-After Header Parsing in C#

The document shows C# `TimeSpan? RetryAfter` but doesn't mention that `HttpResponseMessage.Headers.RetryAfter` already provides `RetryConditionHeaderValue` with `Delta` (TimeSpan) and `Date` (DateTimeOffset) properties. The C# port can use this directly instead of manual parsing, which is simpler than the Dart approach.

### 3.4 Missing: C# Equality Semantics

The document describes Dart's custom `operator ==` and `hashCode` on `AcdcException` but does not discuss the C# equivalent. In C#:
- Exceptions typically do NOT override `Equals()` / `GetHashCode()`
- The Dart approach of using `responseData.toString()` for equality is fragile
- For C#, consider whether equality is even needed (it is primarily useful for testing)
- If needed, implement `IEquatable<AcdcException>` rather than overriding `object.Equals()`

### 3.5 Missing: `CancellationToken` Integration

The Dart `cancelAll()` pattern (tested in `cancel_all_integration_test.dart`) uses Dio's `CancelToken`. In C#, the idiomatic equivalent is `CancellationToken` / `CancellationTokenSource`. The document mentions `cancelAll()` testing but does not discuss how `CancellationTokenSource.Cancel()` maps to this pattern.

### 3.6 Missing: Structured Logging with `ILogger`

The document maps `pretty_dio_logger` to `Serilog` / `Microsoft.Extensions.Logging` (Section 5.1) but does not discuss how `AcdcLogDelegate` maps to `ILogger<T>`. The C# port should use the standard `ILogger` interface rather than a custom delegate. The `toMap()` method maps well to structured logging with `ILogger`:

```csharp
logger.LogError("ACDC exception: {ExceptionType} {StatusCode}", exception.GetType().Name, exception.StatusCode);
```

### 3.7 Missing: `AcdcAuthException.fromDioException` Custom Message Pattern

The document correctly shows the optional `message` parameter but doesn't discuss how this is used in practice. Searching the codebase reveals this is used by the auth interceptor when refresh fails -- it passes a custom message. The C# port needs to support this pattern for the auth handler to provide context-specific error messages.

### 3.8 Dependency Version Constraints

The document lists `sdk: ">=3.6.0 <4.0.0"` and `flutter: ">=3.38.0"` from `pubspec.yaml` but the barrel file's doc comment at `lib/dart_acdc.dart:182-183` says `Dart SDK: >=3.0.0 <4.0.0` and `Flutter: >=3.10.0`. These are inconsistent. The `pubspec.yaml` is the source of truth. The C# port should specify its .NET version requirement clearly.

---

## 4. Corrections

### 4.1 Public API Surface Table (Section 4.3)

The table lists `AcdcSecurityException` under Exceptions in Section 1.1 but this type is NOT exported in `lib/dart_acdc.dart`. The barrel file does NOT contain an export for `acdc_security_exception.dart`. Only `CertificatePinningConfig` is exported from the security module (line 265-266 of `dart_acdc.dart`). The Public API table in Section 4.3 correctly omits it from the Exceptions row, but this contradicts the hierarchy diagram and the detailed description in Section 1.4.

**Recommendation**: Add a clear note that `AcdcSecurityException` is an internal type that is NOT part of the public API, despite being in the hierarchy. Or flag this as a likely bug in the Dart library.

### 4.2 `MockNetworkInfo` Signature

The document's `MockNetworkInfo` snippet (Section 3.4) shows:
```dart
bool get isConnected => true;
```
I cannot verify the exact interface signature without reading the full `NetworkInfo` class, but the `NetworkInfo` abstract class uses method signatures, not simple getters -- the actual `NetworkInfo` interface may differ. This is a minor point but should be verified.

### 4.3 Test File Line Count Table (Appendix)

Some line counts in the appendix appear to be accurate based on what I read. Spot-checking:
- `acdc_exception.dart`: listed as 134 lines -- actual is 135 lines (ends at line 135 with trailing blank). Minor discrepancy.
- `acdc_exception_test.dart`: listed as 101 lines -- matches.
- These are minor and do not affect the review.

---

## 5. Additions: New Insights from Source Code

### 5.1 ErrorInterceptor String-Based Network Error Detection

The `ErrorInterceptor` (`lib/src/interceptors/error_interceptor.dart:86-97`) performs string-matching on `exception.error.toString().toLowerCase()` to detect network errors that Dio classifies as `DioExceptionType.unknown`. The patterns checked are:
- `socketexception`
- `failed host lookup`
- `network is unreachable`
- `software caused connection abort`
- `connection refused`
- `connection reset`

This is a **fragile pattern** that relies on platform-specific error message strings. For the C# port, the equivalent would be catching specific exception types (`SocketException`, `HttpRequestException` with specific `HttpRequestError` values in .NET 8+) rather than string matching. .NET 8 added `HttpRequestError` enum to `HttpRequestException` which provides structured error classification.

### 5.2 Malformed Response Handling

The `ErrorInterceptor` (`lib/src/interceptors/error_interceptor.dart:103-113`) detects malformed responses by checking if `exception.error` is a `FormatException` OR if the error string contains "format", "parse", or "invalid json". These are converted to `AcdcClientException` with the message "Invalid response format from server".

In C#, `HttpClient` does not automatically parse response bodies, so this scenario would only occur if the library adds a deserialization layer. If using `System.Text.Json`, `JsonException` would be the equivalent to catch.

### 5.3 3xx Redirect Handling

The `ErrorInterceptor` handles 3xx responses as `AcdcClientException` (`error_interceptor.dart:48-49`). This only occurs when automatic redirects are disabled in Dio. In C#, `HttpClient` follows redirects automatically by default (up to `HttpClientHandler.MaxAutomaticRedirections`). If redirects are disabled via `HttpClientHandler.AllowAutoRedirect = false`, the response will have a 3xx status code but will NOT throw an exception -- it returns the response. The C# port needs to decide if it should throw for 3xx responses or return them.

### 5.4 `AcdcCacheException` Creates Synthetic `DioException` Wrappers

The factory constructors in `AcdcCacheException` (e.g., `readFailed` at lines 58-73) create a new `DioException` to pass as `originalException`, wrapping the actual error:
```dart
originalException: DioException(
  requestOptions: requestOptions,
  error: error,
),
```
This is because the base `AcdcException` requires `requestOptions` (from `DioException`), even though cache errors may not involve an HTTP request. In C#, this artificial wrapping can be avoided since the base exception wouldn't need to extend `HttpRequestException` for cache errors (supporting the argument for Option B or a hybrid approach).

### 5.5 `AcdcSecurityException` Has No `originalException` Requirement

Unlike other exception subclasses, `AcdcSecurityException` declares `originalException` as optional (`super.originalException` without `required` keyword, at `acdc_security_exception.dart:11`). This is because security exceptions are created directly by `PinningVerifier`, not from an existing `DioException`. The C# equivalent should similarly not require an inner `HttpRequestException`.

### 5.6 Integration Tests Use Two Different `FakeApiServer` Implementations

There are two separate `FakeApiServer` classes defined inline in different test files:
- `test/integration/complete_client_integration_test.dart:372-439` -- supports `respondWith401ThenSuccess()`
- `test/integration/app_lifecycle_test.dart:434-495` -- supports `dynamicHandler`

These are NOT shared via the `test/helpers/` directory. The C# port should consolidate test infrastructure to avoid duplication.

### 5.7 `public_api_test.dart` Tests Default Timeout

The test at `public_api_test.dart:126-129` verifies that the default timeout is 5 seconds:
```dart
const defaultTimeout = Duration(seconds: 5);
expect(dio.options.connectTimeout, defaultTimeout);
expect(dio.options.receiveTimeout, defaultTimeout);
expect(dio.options.sendTimeout, defaultTimeout);
```

This default value is not mentioned in the document and should be captured for the C# port to maintain behavioral parity.

### 5.8 Builder Immutability Pattern

The `builder_reusability_test.dart` verifies that the builder uses an immutable/copy-on-write pattern -- calling `build()` twice produces independent instances. The document mentions this briefly but doesn't flag that `AcdcClientBuilder` is constructed with `const` (`const AcdcClientBuilder()`), meaning the initial builder is a compile-time constant. The C# equivalent should use a similar pattern, possibly with `record` types or an immutable builder that returns new instances on each `With*()` call.

---

## Summary

The document is a solid foundation for the C# port. The exception hierarchy, test patterns, and dependency mapping are largely correct. The most significant gaps are:

1. **ErrorInterceptor routing logic** is barely mentioned despite being the core mechanism that creates typed exceptions
2. **`AcdcSecurityException` is NOT exported** from the public API -- this needs to be flagged as either a bug or a deliberate design choice
3. **C# HttpClient error model** differs significantly from Dio's -- `TaskCanceledException` for timeouts, `HttpRequestException` for network errors, no automatic exception for 4xx/5xx -- and the document should map these explicitly
4. **String-based network error detection** in the ErrorInterceptor is fragile and the C# port should use typed exceptions instead
5. **Several minor missing details** (default timeout values, DioExceptionType mappings per subclass, no `toMap()` override in server/auth exceptions)
