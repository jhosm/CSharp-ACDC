# Research Document 04: Exception Hierarchy, Test Patterns, and Dependencies

## Table of Contents

1. [Exception Hierarchy Analysis](#1-exception-hierarchy-analysis)
2. [Exception Design Philosophy](#2-exception-design-philosophy)
3. [Test Patterns and Infrastructure](#3-test-patterns-and-infrastructure)
4. [Public API Enforcement](#4-public-api-enforcement)
5. [Dependency Mapping: Dart to C#](#5-dependency-mapping-dart-to-c)
6. [Flutter-Specific Patterns Requiring Rethinking](#6-flutter-specific-patterns-requiring-rethinking)
7. [Example Code Analysis](#7-example-code-analysis)
8. [Recommendations for C# Exception Hierarchy](#8-recommendations-for-c-exception-hierarchy)

---

## 1. Exception Hierarchy Analysis

### 1.1 Class Diagram

```
DioException (from dio package)
    |
    +-- AcdcException (base)
            |
            +-- AcdcAuthException       (401, 403 - auth/authz errors)
            |
            +-- AcdcClientException     (4xx except 401/403 - client errors)
            |
            +-- AcdcServerException     (5xx - server errors)
            |
            +-- AcdcNetworkException    (timeouts, DNS, connectivity)
            |
            +-- AcdcCacheException      (cache read/write/init/clear failures)
            |
            +-- AcdcSecurityException   (certificate pinning failures)
```

> **Review correction:** `AcdcSecurityException` is listed in the hierarchy and described in the document, but it is **not exported** from the public barrel file (`lib/dart_acdc.dart`). The `public_api_test.dart` does NOT check for it in the "All exception types are exported" test. This is either a bug in the Dart library or a deliberate choice that the C# port must address — should `AcdcSecurityException` be part of the public API?

### 1.2 Supporting Enums

```
NetworkErrorType
    connectionTimeout
    sendTimeout
    receiveTimeout
    noConnection
    cancelled
    other

CacheOperation
    read
    write
    initialization
    clear
    other
```

### 1.3 Base Exception: `AcdcException`

**Source**: `lib/src/exceptions/acdc_exception.dart`

The base class extends `DioException` and adds:

| Field | Type | Purpose |
|-------|------|---------|
| `message` | `String` | Developer-focused error message with technical context |
| `originalException` | `DioException?` | Preserves the original DioException for debugging |
| `statusCode` | `int?` | HTTP status code if available |
| `responseData` | `dynamic` | Truncated response body (max 1KB) |
| `requestUrl` | `String?` | Redacted request URL (sensitive params removed) |

**Key utility methods**:

```dart
// Converts exception to structured map for logging
Map<String, dynamic> toMap() => {
  'type': runtimeType.toString(),
  'message': message,
  'statusCode': statusCode,
  'requestUrl': requestUrl,
  'responseData': responseData,
  'originalError': originalException.toString(),
};

// Truncates response body to 1KB limit
static String? truncateResponseBody(dynamic body, {int maxLength = 1024}) {
  if (body == null) return null;
  final bodyStr = body.toString();
  if (bodyStr.length <= maxLength) return bodyStr;
  return '${bodyStr.substring(0, maxLength)}... (truncated)';
}

// Redacts sensitive URL parameters (token, api_key, password, secret, etc.)
static String redactUrl(String url) { ... }
```

**Equality**: Implemented via `operator ==` and `hashCode` using `runtimeType`, `message`, `statusCode`, `requestUrl`, and `responseData.toString()`. This ensures type-safe equality (an `AcdcException` != `AcdcAuthException` even with same message) and handles complex responseData objects by using toString comparison.

> **Added from review:** `AcdcException` defaults `super.type` to `DioExceptionType.unknown` (`acdc_exception.dart:25`). Each subclass overrides this differently:
> - `AcdcAuthException` → `DioExceptionType.badResponse`
> - `AcdcClientException` → `DioExceptionType.badResponse`
> - `AcdcServerException` → `DioExceptionType.badResponse`
> - `AcdcNetworkException` → preserves `originalException.type`
> - `AcdcCacheException` → `DioExceptionType.unknown`
> - `AcdcSecurityException` → inherits default `DioExceptionType.unknown`
>
> This mapping is porting-relevant: the C# equivalent needs to decide how to represent the error type/category.

### 1.4 Specialized Exception Classes

#### AcdcAuthException (401/403)

**Source**: `lib/src/exceptions/acdc_auth_exception.dart`

- Always uses `DioExceptionType.badResponse`
- Factory `fromDioException()` with optional custom message override
- Status-specific default messages:
  - 401: "Authentication failed: Invalid or expired token"
  - 403: "Authorization failed: Insufficient permissions"
  - Other: "Authentication error (HTTP {code})"

> **Added from review:** `AcdcAuthException` also handles HTTP 403 (Forbidden) with the message "Authorization failed: Insufficient permissions". The `_defaultMessage` method generates different messages for 401 vs 403 vs other codes.

```dart
factory AcdcAuthException.fromDioException(
  DioException exception, {
  String? message,  // Optional override for custom message
})
```

#### AcdcClientException (4xx except 401/403)

**Source**: `lib/src/exceptions/acdc_client_exception.dart`

- Extra field: `retryAfter` (`Duration?`) for 429 Too Many Requests
- Parses `Retry-After` header as both integer seconds and HTTP date
- Status-specific messages for 400, 404, 422, 429
- `toMap()` includes `retryAfter` in seconds

```dart
// Retry-After parsing
static Duration? _parseRetryAfter(Headers? headers) {
  // Try parsing as seconds (integer)
  final seconds = int.tryParse(retryAfterStr);
  if (seconds != null) return Duration(seconds: seconds);
  // Try parsing as HTTP date
  final httpDate = DateTime.tryParse(retryAfterStr);
  if (httpDate != null) {
    final diff = httpDate.difference(DateTime.now());
    return diff.isNegative ? Duration.zero : diff;
  }
  return null;
}
```

> **Added from review:** The initial `Retry-After` extraction from `Headers` uses `headers['retry-after']` which returns a `List<String>?` (Dio stores headers as lists), then takes `.first`. In C#, `HttpResponseMessage.Headers.RetryAfter` provides a strongly-typed `RetryConditionHeaderValue` with `Delta` (TimeSpan) and `Date` (DateTimeOffset) properties — this is simpler than manual parsing.

#### AcdcServerException (5xx)

**Source**: `lib/src/exceptions/acdc_server_exception.dart`

- Simplest subclass - no additional fields
- Always `DioExceptionType.badResponse`
- Generic message: "Server error (HTTP {code}): The server encountered an error processing your request"

#### AcdcNetworkException (connectivity/timeout)

**Source**: `lib/src/exceptions/acdc_network_exception.dart`

- Extra field: `networkErrorType` (`NetworkErrorType` enum)
- Maps `DioExceptionType` to `NetworkErrorType`
- `originalException` is **required** (not nullable)
- `toMap()` includes `networkErrorType` name

```dart
// DioExceptionType -> NetworkErrorType mapping
connectionTimeout -> NetworkErrorType.connectionTimeout
sendTimeout       -> NetworkErrorType.sendTimeout
receiveTimeout    -> NetworkErrorType.receiveTimeout
cancel            -> NetworkErrorType.cancelled
connectionError   -> NetworkErrorType.noConnection
default           -> NetworkErrorType.other
```

> **Added from review:** `AcdcNetworkException` passes `type: originalException.type` to the `super` constructor (`acdc_network_exception.dart:42`), preserving the original Dio exception type. This is different from other subclasses that hardcode their type to `DioExceptionType.badResponse`.

#### AcdcCacheException (cache operations)

**Source**: `lib/src/exceptions/acdc_cache_exception.dart`

- Extra field: `cacheOperation` (`CacheOperation` enum)
- Always `DioExceptionType.unknown`
- Four named factory constructors for common operations:
  - `AcdcCacheException.initializationFailed()`
  - `AcdcCacheException.readFailed()`
  - `AcdcCacheException.writeFailed()`
  - `AcdcCacheException.clearFailed()`
- These create synthetic `DioException` wrappers since cache errors may not originate from Dio

> **Added from review:** The `originalException` parameter in `AcdcCacheException` is declared as `required super.originalException` (`acdc_cache_exception.dart:30`), making it non-nullable — unlike the base `AcdcException` where it is optional. The factory constructors create synthetic `DioException` wrappers to satisfy this requirement, even when the original error was not HTTP-related. In C#, this artificial wrapping can be avoided since the base exception need not extend `HttpRequestException` for cache errors.

#### AcdcSecurityException (certificate pinning)

**Source**: `lib/src/exceptions/acdc_security_exception.dart`

- Extra fields: `hostname` (String), `peerCertificates` (List<String>?)
- Custom `toString()` with detailed certificate info for debugging
- Designed for certificate pinning failures, listing the SHA-256 SPKI hashes the server actually presented

```dart
@override
String toString() {
  final buffer = StringBuffer()
    ..write('AcdcSecurityException: $message\n')
    ..write('  Hostname: $hostname\n');
  if (peerCertificates != null && peerCertificates!.isNotEmpty) {
    buffer.write('  Peer Certificates (Server presented):\n');
    for (final cert in peerCertificates!) {
      buffer.write('    - $cert\n');
    }
  }
  // ...
}
```

> **Added from review:** `AcdcSecurityException` does NOT have a `fromDioException()` factory — it is only created directly in `PinningVerifier.verify()` (`pinning_verifier.dart:73-79`). The `ErrorInterceptor` does NOT convert `DioExceptionType.badCertificate` to `AcdcSecurityException`. This means the security exception has a completely different throw path from all other exceptions. In C#, it should be handled in the certificate validation callback, not in the `DelegatingHandler` error pipeline.

> **Added from review:** `AcdcSecurityException` declares `originalException` as optional (without `required` keyword, at `acdc_security_exception.dart:11`), unlike other subclasses. The C# equivalent should similarly not require an inner `HttpRequestException`.

> **Added from review:** There is NO dedicated test file for `AcdcSecurityException` in `test/exceptions/`. It is only tested indirectly via `pinning_verifier_test.dart`, `pinning_http_client_test.dart`, and `certificate_pinning_integration_test.dart`.

---

## 2. Exception Design Philosophy

### 2.1 Extending DioException

The entire hierarchy extends `DioException` rather than implementing a separate interface. This is a deliberate design choice:

- **Backward compatibility**: Existing `catch (DioException e)` blocks still work
- **Gradual adoption**: Teams can adopt ACDC exceptions incrementally
- **Interop**: Dio interceptors that handle `DioException` work transparently

**Trade-off**: The `message` field is overridden with `@override` and `// ignore: overridden_fields` because `DioException.message` is not `final` but `AcdcException.message` is.

### 2.2 Type-Safe Error Handling

The recommended pattern uses Dart's `on Type catch` for ordered exception matching:

```dart
try {
  final response = await dio.get('/users');
} on AcdcAuthException catch (e) {
  // Handle 401/403 errors (most specific first)
} on AcdcServerException catch (e) {
  // Handle 5xx errors
} on AcdcNetworkException catch (e) {
  // Handle network failures
} on AcdcClientException catch (e) {
  // Handle 4xx errors (other than 401/403)
} on AcdcException catch (e) {
  // Catch-all for any ACDC error
}
```

### 2.3 Security-Conscious Design

- **URL redaction**: All exceptions automatically redact sensitive query parameters (token, api_key, password, secret, etc.)
- **Response body truncation**: Response data is capped at 1KB to prevent memory issues and log spam
- **Safe toString()**: Structured output that never leaks raw tokens

### 2.4 Structured Logging Support

Every exception provides `toMap()` for machine-readable logging. Subclasses extend the base map with their specific fields (e.g., `networkErrorType`, `retryAfter`, `cacheOperation`).

> **Review correction:** `AcdcServerException` and `AcdcAuthException` do NOT override `toMap()`. They inherit the base implementation, so their maps do NOT include any subclass-specific data. Only `AcdcClientException` (adds `retryAfter`), `AcdcNetworkException` (adds `networkErrorType`), and `AcdcCacheException` (adds `cacheOperation`) have `toMap()` overrides.

### 2.5 Factory Constructor Pattern

Most subclasses provide `fromDioException()` factory constructors that:
1. Extract status code, URL, response body from the DioException
2. Call `redactUrl()` on the URL
3. Call `truncateResponseBody()` on the response
4. Generate a human-readable default message
5. Construct the typed exception

This keeps the ErrorInterceptor clean -- it just calls the appropriate factory.

> **Added from review:** The `ErrorInterceptor` (`lib/src/interceptors/error_interceptor.dart`) is the central component that creates typed exceptions, but is not described in this document. Key behaviors:
> 1. Checks network errors first (lines 35-36) before HTTP status codes
> 2. Handles malformed responses as `AcdcClientException` ("Invalid response format from server") — detects `FormatException` or strings containing "format", "parse", "invalid json" (lines 103-113)
> 3. Handles 3xx redirects as `AcdcClientException` when redirects are disabled (lines 48-49)
> 4. Detects hidden network errors via string matching on `DioExceptionType.unknown` errors — patterns: `socketexception`, `failed host lookup`, `network is unreachable`, `software caused connection abort`, `connection refused`, `connection reset` (lines 86-97). This is **fragile** and relies on platform-specific strings.
> 5. Does NOT handle `DioExceptionType.badCertificate` — certificate errors fall through and are NOT converted to `AcdcSecurityException`
> 6. Falls through to return original `DioException` for unrecognized cases
>
> For C#, the equivalent `DelegatingHandler` should catch `HttpRequestException` (network errors) and `TaskCanceledException` (timeouts/cancellation) instead of string matching. .NET 8 added `HttpRequestError` enum for structured error classification.

---

## 3. Test Patterns and Infrastructure

### 3.1 Test Structure Overview

```
test/
  exceptions/
    acdc_exception_test.dart            # Base exception tests
    acdc_auth_exception_test.dart       # Auth exception tests
    acdc_client_exception_test.dart     # Client exception tests
    acdc_server_exception_test.dart     # Server exception tests
    acdc_network_exception_test.dart    # Network exception tests
    acdc_cache_exception_test.dart      # Cache exception tests
    exception_equality_test.dart        # Cross-type equality tests
  integration/
    builder_reusability_test.dart       # Builder produces independent instances
    app_lifecycle_test.dart             # Token refresh under lifecycle scenarios
    complete_client_integration_test.dart # Full end-to-end integration
    custom_logger_integration_test.dart # Custom log delegate integration
    cancel_all_integration_test.dart    # Request cancellation integration
  helpers/
    fake_token_provider.dart            # In-memory token storage for tests
    mock_network_info.dart              # Always-online network info mock
    fake_oauth_server.dart              # Shelf-based fake OAuth server
    pinning_test_server.dart            # TLS pinning test server
  public_api_test.dart                  # Verifies all public types are exported
  enforce_export_policy_test.dart       # Filesystem-level export enforcement
```

> **Added from review:** Missing from the test listing: `test/interceptors/error_interceptor_test.dart` (274 lines) — comprehensive test file covering all status code mappings (401, 403, 4xx, 5xx), all network error types, and edge cases (malformed responses, 3xx redirects, non-standard status codes like 418, 451, 599).

### 3.2 Testing Frameworks Used

| Framework | Purpose |
|-----------|---------|
| `test` (package:test/test.dart) | Standard Dart test framework |
| `flutter_test` | Flutter-specific testing with widget bindings |
| `http_mock_adapter` | DioAdapter-based HTTP mocking |
| `shelf` / `shelf_io` | Real HTTP server for integration tests |
| `mockito` | Listed as dev_dependency (^5.6.3), used elsewhere |

### 3.3 Unit Test Patterns (Exception Tests)

**Pattern: Factory constructor verification**

Each exception test verifies that `fromDioException()` correctly:
- Extracts and preserves status codes
- Generates appropriate error messages
- Truncates response bodies
- Redacts sensitive URL parameters
- Maps Dio types to ACDC types

```dart
test('fromDioException handles 401 responses', () {
  final dioException = DioException(
    requestOptions: RequestOptions(path: '/test'),
    response: Response(
      requestOptions: RequestOptions(path: '/test'),
      statusCode: 401,
      data: {'error': 'Unauthorized'},
    ),
  );
  final exception = AcdcAuthException.fromDioException(dioException);
  expect(exception.statusCode, equals(401));
  expect(exception.message, contains('Invalid or expired token'));
});
```

**Pattern: Equality testing**

Dedicated `exception_equality_test.dart` validates:
- Same properties = equal objects
- Different properties = not equal
- Different runtime types = not equal (even with same message)
- Complex responseData equality via toString comparison

```dart
test('Different exception types are not equal even with same message', () {
  final e1 = AcdcException(requestOptions: ro, message: 'Auth error');
  final e2 = AcdcAuthException(requestOptions: ro, message: 'Auth error');
  expect(e1, isNot(equals(e2)));
});
```

**Pattern: toMap() serialization testing**

```dart
test('toMap includes cacheOperation', () {
  final exception = AcdcCacheException(..., cacheOperation: CacheOperation.read);
  final map = exception.toMap();
  expect(map['cacheOperation'], 'read');
});
```

> **Added from review:** The `AcdcLogDelegate` interface defines `metadata` as a **required** non-nullable `Map<String, dynamic>`, but the example file (`example/example.dart:159-162`) uses an **optional positional** parameter with `?`. This compiles in Dart but could confuse C# port developers. The C# interface should use a required parameter.

### 3.4 Integration Test Patterns

#### Real HTTP Servers (Shelf-based)

Integration tests use **real HTTP servers** via the `shelf` package rather than mocks:

**FakeOAuthServer** (`test/helpers/fake_oauth_server.dart`):
- Runs on `localhost:0` (random port)
- Routes: `POST /token` (refresh), `POST /revoke` (revocation)
- Configurable: `respondWithSuccess()`, `respondWithOAuthError()`, `respondWithServerError()`
- Supports response delay simulation
- Request tracking: `refreshCallCount`, `revokeCallCount`, `lastRefreshRequestParams`
- Validates grant_type and refresh_token parameters

**FakeApiServer** (defined inline in test files):
- Generic API server for testing HTTP interactions
- Supports dynamic handlers: `dynamicHandler = (request) => (statusCode, data)`
- `respondWith401ThenSuccess()` for testing reactive token refresh

```dart
setUp(() async {
  oauthServer = FakeOAuthServer();
  await oauthServer.start();
  apiServer = FakeApiServer();
  await apiServer.start();
  tokenProvider = FakeTokenProvider();
});
tearDown(() async {
  await oauthServer.stop();
  await apiServer.stop();
});
```

#### Mock Adapter (http_mock_adapter)

Simpler tests use `DioAdapter` from `http_mock_adapter` for in-process HTTP mocking:

```dart
final dioAdapter = DioAdapter(dio: client);
adapter.onGet('/test', (server) => server.reply(200, {'data': 'success'}));
```

#### Test Helper: FakeTokenProvider

In-memory token storage implementing `TokenProvider`:

```dart
class FakeTokenProvider implements TokenProvider {
  String? _accessToken;
  String? _refreshToken;
  DateTime? _accessExpiry;
  DateTime? _refreshExpiry;

  void setInitialState({...}) { ... }  // Test setup helper
  // Implements all TokenProvider methods with in-memory storage
}
```

#### Test Helper: MockNetworkInfo

Always-online network info for testing:

```dart
class MockNetworkInfo implements NetworkInfo {
  @override
  bool get isConnected => true;
  @override
  Stream<NetworkStatus> get onStatusChange => const Stream.empty();
  @override
  void dispose() { _disposed = true; }
}
```

### 3.5 Integration Test Scenarios

#### App Lifecycle Tests (`app_lifecycle_test.dart`)

Tests token refresh behavior under lifecycle conditions:

| Scenario | Expected Behavior |
|----------|------------------|
| Refresh with delay (simulates backgrounding) | Completes successfully despite delay |
| Network error during refresh | Does NOT clear tokens (transient error) |
| Auth error (invalid_grant) during refresh | DOES clear tokens, allows retry after re-login |
| Concurrent requests during delayed refresh | All wait for single refresh, only 1 server call |
| Server error (5xx) during refresh | Does NOT clear tokens (transient error) |

**Key insight for C#**: The distinction between transient errors (network, server) and permanent errors (auth) is critical. Transient errors preserve tokens; auth errors clear them.

#### Complete Client Integration (`complete_client_integration_test.dart`)

End-to-end testing of:
- Authenticated request flow
- Proactive token refresh (token expiring within threshold)
- Reactive token refresh (401 response triggers refresh + retry)
- Error interceptor converting HTTP errors to typed exceptions
- Custom interceptors alongside built-in interceptors
- Concurrent request queuing during refresh
- Logout with token revocation

#### Builder Reusability (`builder_reusability_test.dart`)

Verifies immutability:
```dart
test('builds independent Dio instances', () async {
  final client1 = await builder.build();
  final client2 = await builder.build();
  expect(client1, isNot(same(client2)));  // Different instances
  client1.options.baseUrl = 'https://changed.com';
  expect(client2.options.baseUrl, 'https://api.example.com');  // Unaffected
});
```

#### Cancel All Integration (`cancel_all_integration_test.dart`)

Tests the `cancelAll()` extension method:
```dart
client.cancelAll(reason);  // Extension method on Dio
// Verifies all 5 concurrent requests get DioExceptionType.cancel
// Verifies tracker is empty after cancel
// Verifies new requests work after cancelAll
```

#### Custom Logger Integration (`custom_logger_integration_test.dart`)

Verifies custom `AcdcLogDelegate` receives formatted log messages:
```dart
class _CustomLogDelegate implements AcdcLogDelegate {
  final void Function(String, LogLevel, Map<String, dynamic>?) onLog;
  @override
  void log(String message, LogLevel level, Map<String, dynamic> metadata) =>
      onLog(message, level, metadata);
}
```

---

## 4. Public API Enforcement

### 4.1 Export Policy Test (`enforce_export_policy_test.dart`)

Uses **filesystem inspection** to enforce that `lib/` only contains:
- `dart_acdc.dart` (the barrel file)
- `src/` directory (private implementation)

```dart
test('Strict Export Policy: Only dart_acdc.dart is public in lib/', () {
  final libDir = Directory(path.join(currentDir.path, 'lib'));
  final entities = libDir.listSync();
  final allowedFiles = ['dart_acdc.dart'];
  final allowedDirs = ['src'];
  // Fails if any other file or directory exists in lib/
});
```

### 4.2 Public API Test (`public_api_test.dart`)

Verifies all public types are accessible via the barrel import:

```dart
import 'package:dart_acdc/dart_acdc.dart';

test('All exception types are exported', () {
  expect(AcdcException, isNotNull);
  expect(AcdcNetworkException, isNotNull);
  expect(AcdcAuthException, isNotNull);
  expect(AcdcServerException, isNotNull);
  expect(AcdcClientException, isNotNull);
  expect(AcdcCacheException, isNotNull);
});

test('NetworkErrorType enum is exported', () { ... });
test('CacheOperation enum is exported', () { ... });
test('CacheConfig is exported and accessible', () { ... });
test('LogLevel enum is exported', () { ... });
test('AcdcLogDelegate interface is exported', () { ... });
```

Also tests that:
- `AcdcClientBuilder` is constructible and usable
- `TokenProvider`, `TokenRefreshResult` are accessible
- `AcdcAuthManager` and `AcdcAuth` extension are accessible
- Internal files (AuthInterceptor, ErrorInterceptor) are NOT exported
- Default client setup includes correct interceptors and timeout defaults
- All builder methods chain correctly
- Zero-config, authenticated, and custom-configured client creation all work

### 4.3 Public API Surface (from `lib/dart_acdc.dart`)

The barrel file exports:

| Category | Exports |
|----------|---------|
| Builder | `AcdcClientBuilder` |
| Auth | `AcdcAuthManager`, `AcdcAuth`, `SecureTokenProvider`, `TokenProvider`, `TokenRefreshResult` |
| Cache | `AcdcCacheManager`, `AcdcCache`, `CacheConfig` |
| Exceptions | `AcdcException`, `AcdcAuthException`, `AcdcClientException`, `AcdcServerException`, `AcdcNetworkException`, `AcdcCacheException`, `CacheOperation`, `NetworkErrorType` |

> **Review correction:** The Public API table should include `CacheOperation` and `NetworkErrorType` in the Exceptions row (they are exported from the barrel file). Also note that `AcdcSecurityException` is NOT in the barrel file exports despite being in the exception hierarchy — it is effectively an internal type.

| Logging | `AcdcLogDelegate`, `LogLevel` |
| Network | `NetworkInfo`, `NetworkStatus` |
| Security | `CertificatePinningConfig` |

---

## 5. Dependency Mapping: Dart to C#

### 5.1 Runtime Dependencies

| Dart Dependency | Version | Purpose | C# (.NET 8+) Equivalent | Notes |
|----------------|---------|---------|------------------------|-------|
| `dio` | ^5.4.0 | HTTP client with interceptors | `HttpClient` + `DelegatingHandler` chain **or** `Refit` | Dio's interceptor model maps to `DelegatingHandler` pipeline. `HttpClientFactory` pattern for builder. |
| `dio_cache_interceptor` | ^4.0.5 | HTTP cache interceptor with store abstraction | `Microsoft.Extensions.Caching.Memory` / `FusionCache` | .NET has `IMemoryCache` and `IDistributedCache`. FusionCache provides stale-while-revalidate. |
| `http_cache_file_store` | ^2.0.1 | File-based cache store | Custom `IDistributedCache` with file store | Or use SQLite via `Microsoft.Data.Sqlite`. |
| `flutter_secure_storage` | ^10.0.0 | Secure token storage (Keychain/Keystore) | `Microsoft.AspNetCore.DataProtection` / `DPAPI` | For server-side: DPAPI or Azure Key Vault. For desktop: `ProtectedData` class. |
| `connectivity_plus` | ^7.0.0 | Network connectivity monitoring | `System.Net.NetworkInformation.NetworkChange` | .NET has `NetworkChange.NetworkAvailabilityChanged`. Server-side may not need this. |
| `encrypt` | ^5.0.3 | AES encryption for cache | `System.Security.Cryptography.Aes` | Direct mapping. .NET crypto is mature. |
| `crypto` | ^3.0.7 | SHA-256 hashing | `System.Security.Cryptography.SHA256` | Direct mapping. |
| `jwt_decoder` | ^2.0.1 | JWT parsing (expiry extraction) | `System.IdentityModel.Tokens.Jwt` / `Microsoft.IdentityModel.JsonWebTokens` | `JwtSecurityTokenHandler` or the newer `JsonWebTokenHandler`. |
| `path_provider` | ^2.1.5 | Platform directory paths | `Environment.GetFolderPath(Environment.SpecialFolder.*)` | Or `Path.GetTempPath()`, `AppContext.BaseDirectory`. |
| `pretty_dio_logger` | ^1.3.1 | HTTP request/response logging | `Serilog` / `Microsoft.Extensions.Logging` | Use `ILogger<T>` with structured logging. DelegatingHandler for HTTP logging. |
| `meta` | ^1.17.0 | Annotations (@immutable, etc.) | Built-in C# attributes | `[Immutable]` (custom), `readonly struct`, etc. |
| `flutter` (SDK) | >=3.38.0 | Flutter framework | **Not applicable** | C# port is server-side / library only. |

> **Added from review:** The handler ordering in the C# `IHttpClientFactory` example should match the interceptor chain order: Logging → Error → Cancellation → Auth → Cache → Custom → Deduplication (not the arbitrary order shown in the document).

> **Review correction:** The barrel file's doc comment says `Dart SDK: >=3.0.0` and `Flutter: >=3.10.0`, but `pubspec.yaml` says `sdk: ">=3.6.0 <4.0.0"` and `flutter: ">=3.38.0"`. The `pubspec.yaml` is the source of truth. The C# port should specify its .NET version requirement clearly.

### 5.2 Dev Dependencies

| Dart Dev Dependency | Version | Purpose | C# (.NET 8+) Equivalent |
|--------------------|---------|---------|------------------------|
| `test` | ^1.26.3 | Unit testing | `xUnit` or `NUnit` |
| `flutter_test` | SDK | Flutter test framework | `xUnit` |
| `mockito` | ^5.6.3 | Mock generation | `Moq` or `NSubstitute` |
| `http_mock_adapter` | ^0.6.1 | Dio HTTP mocking | `MockHttpMessageHandler` (custom) or `RichardSzalay.MockHttp` |
| `shelf` / `shelf_io` | ^1.4.0 | Fake HTTP server for integration | `Microsoft.AspNetCore.TestHost.TestServer` or `WireMock.Net` |
| `build_runner` | ^2.10.5 | Code generation | Source generators or `dotnet build` |
| `built_value` / `built_collection` | ^8.12.3 / ^5.1.1 | Immutable value types | `record` types (C# 12) |
| `flutter_lints` | ^6.0.0 | Lint rules | `.editorconfig` + Roslyn analyzers |
| `connectivity_plus_platform_interface` | any | Platform abstraction | Not needed |
| `path` | any | Path manipulation | `System.IO.Path` |

### 5.3 Key Mapping Decisions

#### HTTP Client: Dio -> HttpClient + DelegatingHandler

Dio's interceptor chain maps well to .NET's `DelegatingHandler` pipeline:

```
Dart (Dio):                         C# (HttpClient):
Interceptor.onRequest()    ->       DelegatingHandler.SendAsync() (before base.SendAsync)
Interceptor.onResponse()   ->       DelegatingHandler.SendAsync() (after base.SendAsync)
Interceptor.onError()      ->       DelegatingHandler.SendAsync() (catch block)
```

Builder pattern maps to:
```csharp
// C# equivalent of AcdcClientBuilder
services.AddHttpClient("acdc")
    .AddHttpMessageHandler<AuthHandler>()
    .AddHttpMessageHandler<CacheHandler>()
    .AddHttpMessageHandler<ErrorHandler>()
    .AddHttpMessageHandler<LoggingHandler>();
```

Or use `IHttpClientFactory` with named clients.

#### Mocking: http_mock_adapter -> MockHttpMessageHandler

```csharp
// C# equivalent of DioAdapter
var mockHandler = new MockHttpMessageHandler();
mockHandler.When("/test").Respond(HttpStatusCode.OK, new StringContent("{}"));
var client = new HttpClient(mockHandler);
```

#### Integration Testing: shelf -> TestServer or WireMock.Net

```csharp
// C# equivalent of FakeOAuthServer
var server = WireMockServer.Start();
server.Given(Request.Create().WithPath("/token").UsingPost())
    .RespondWith(Response.Create().WithStatusCode(200).WithBody("..."));
```

> **Added from review:** There are two separate `FakeApiServer` classes defined inline in different test files (`complete_client_integration_test.dart:372-439` and `app_lifecycle_test.dart:434-495`). These are NOT shared via `test/helpers/`. The C# port should consolidate test infrastructure to avoid duplication.

---

## 6. Flutter-Specific Patterns Requiring Rethinking

### 6.1 `flutter_secure_storage` -> Server-Side Secure Storage

**Dart**: `flutter_secure_storage` uses iOS Keychain and Android Keystore for token persistence.

**C# Decision Points**:
- **Server-side (web API)**: Tokens live in-memory per-request or in `IDistributedCache` (Redis). No equivalent needed.
- **Desktop app**: `ProtectedData.Protect()` (DPAPI on Windows), macOS Keychain via P/Invoke.
- **Cross-platform library**: Abstract as `ISecureStorage` interface. Ship default implementations per platform.

**Recommendation**: The `TokenProvider` interface translates directly. Let consumers provide their own `ITokenProvider`. Ship a `MemoryTokenProvider` for testing and simple cases.

### 6.2 `connectivity_plus` -> Network Monitoring

**Dart**: Used to detect online/offline state for cache fallback decisions.

**C# Decision Points**:
- **Server-side**: Network is always assumed available. Remove or make optional.
- **Desktop/mobile**: `NetworkChange.NetworkAvailabilityChanged` on .NET.
- **MAUI**: `Connectivity.Current.NetworkAccess` on .NET MAUI.

**Recommendation**: Make `INetworkInfo` optional in the builder. Default to "always online" for server scenarios. This is already how the Dart code handles testing (`MockNetworkInfo` returns `isConnected => true`).

### 6.3 `path_provider` -> File Paths

**Dart**: Gets platform-specific directories (temp, app support, etc.) for cache file storage.

**C#**: Use `Environment.GetFolderPath()` or `Path.GetTempPath()`. Much simpler.

### 6.4 Flutter Test Bindings

**Dart**: Tests require `TestWidgetsFlutterBinding.ensureInitialized()` and `setMockMethodCallHandler` for platform channels.

**C#**: Not needed. .NET dependency injection and interfaces handle this cleanly.

### 6.5 `MethodChannel` Mocking

**Dart**: The `public_api_test.dart` requires mocking the Flutter secure storage platform channel:
```dart
TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
    .setMockMethodCallHandler(
  const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
  (call) async => null,
);
```

**C#**: No equivalent needed. Interface-based DI eliminates platform coupling.

---

## 7. Example Code Analysis

**Source**: `example/example.dart`

The example demonstrates five usage patterns:

1. **Zero-config**: `AcdcClientBuilder().build()` -- minimal setup
2. **With authentication**: Builder + `withTokenRefreshEndpoint()` + `withInitialTokens()`
3. **With caching**: `withCache(CacheConfig())` -- default 1-hour TTL
4. **With custom logging**: `withLogDelegate(MyLogDelegate())` + `withLogLevel(LogLevel.debug)`
5. **With custom token provider**: `withTokenProvider(MyTokenProvider())` -- user implements TokenProvider

**Key example patterns for C# port**:

```dart
// Dart exception handling pattern
try {
  final response = await dio.get<Map<String, dynamic>>('/data');
} on AcdcException catch (e) {
  final message = e.message;
}
```

C# equivalent:
```csharp
try {
    var response = await client.GetAsync<Dictionary<string, object>>("/data");
} catch (AcdcException e) {
    var message = e.Message;
}
```

> **Added from review:** The `public_api_test.dart` verifies the default timeout is 5 seconds (`public_api_test.dart:126-129`):
> ```dart
> const defaultTimeout = Duration(seconds: 5);
> expect(dio.options.connectTimeout, defaultTimeout);
> expect(dio.options.receiveTimeout, defaultTimeout);
> expect(dio.options.sendTimeout, defaultTimeout);
> ```
> This default should be captured for the C# port to maintain behavioral parity.

---

## 8. Recommendations for C# Exception Hierarchy

### 8.1 Option A: Extend HttpRequestException (Recommended)

```csharp
// Base exception - extends HttpRequestException for compatibility
public class AcdcException : HttpRequestException
{
    public AcdcException(string message, HttpStatusCode? statusCode = null,
        Exception? innerException = null, HttpRequestException? originalException = null,
        string? responseData = null, string? requestUrl = null)
        : base(message, innerException, statusCode)
    {
        OriginalException = originalException;
        ResponseData = responseData;
        RequestUrl = requestUrl;
    }

    public HttpRequestException? OriginalException { get; }
    public string? ResponseData { get; }
    public string? RequestUrl { get; }

    public Dictionary<string, object?> ToMap() => new()
    {
        ["type"] = GetType().Name,
        ["message"] = Message,
        ["statusCode"] = StatusCode,
        ["requestUrl"] = RequestUrl,
        ["responseData"] = ResponseData,
        ["originalError"] = OriginalException?.ToString(),
    };

    public static string? TruncateResponseBody(string? body, int maxLength = 1024)
    { /* same logic */ }

    public static string RedactUrl(string url)
    { /* same logic */ }
}

// Auth exception
public class AcdcAuthException : AcdcException { }

// Client exception with RetryAfter
public class AcdcClientException : AcdcException
{
    public TimeSpan? RetryAfter { get; }
}

// Server exception
public class AcdcServerException : AcdcException { }

// Network exception with typed error
public class AcdcNetworkException : AcdcException
{
    public NetworkErrorType NetworkErrorType { get; }
}

// Cache exception with operation type
public class AcdcCacheException : AcdcException
{
    public CacheOperation CacheOperation { get; }
    // Named static factory methods
    public static AcdcCacheException InitializationFailed(...) { }
    public static AcdcCacheException ReadFailed(...) { }
    public static AcdcCacheException WriteFailed(...) { }
    public static AcdcCacheException ClearFailed(...) { }
}

// Security exception
public class AcdcSecurityException : AcdcException
{
    public string Hostname { get; }
    public IReadOnlyList<string>? PeerCertificates { get; }
}

// Enums
public enum NetworkErrorType
{
    ConnectionTimeout,
    SendTimeout,
    ReceiveTimeout,
    NoConnection,
    Cancelled,
    Other
}

public enum CacheOperation
{
    Read, Write, Initialization, Clear, Other
}
```

**Pros of extending HttpRequestException**:
- .NET 7+ `HttpRequestException` already has `StatusCode` property
- Existing `catch (HttpRequestException)` blocks still work
- Same philosophy as Dart extending DioException

**Cons**:
- `HttpRequestException` is somewhat HTTP-specific; cache and security exceptions are not HTTP errors
- `HttpRequestException.StatusCode` is `HttpStatusCode?` (enum), Dart uses `int?`

> **Added from review:** `HttpRequestException.StatusCode` was only added in .NET 5.0. The document should specify minimum .NET version. Also, `HttpRequestException` does not have a built-in `InnerException` typed as `HttpRequestException` — `OriginalException` would need to be a separate property.

> **Added from review:** For `AcdcCacheException`, extending `HttpRequestException` creates a semantic problem: a cache read failure is not an HTTP request exception. Consider having `AcdcCacheException` extend `AcdcException` which extends `Exception` (not `HttpRequestException`), breaking with the Dart pattern where this was forced by `DioException` inheritance. This supports Option B or a hybrid approach for non-HTTP exceptions.

### 8.2 Option B: Custom Base Exception

```csharp
public class AcdcException : Exception
{
    public int? StatusCode { get; }
    public string? ResponseData { get; }
    public string? RequestUrl { get; }
    public HttpRequestException? OriginalHttpException { get; }
    // ...
}
```

**Pros**: Cleaner separation; cache/security exceptions don't inherit HTTP baggage.
**Cons**: Existing `catch (HttpRequestException)` won't catch these.

### 8.3 Recommendation

**Use Option A (extend `HttpRequestException`)** for consistency with the Dart design philosophy. The backward compatibility argument is strong: teams already catching `HttpRequestException` in their code will automatically catch ACDC exceptions, enabling gradual adoption.

For `AcdcCacheException` and `AcdcSecurityException`, which are not HTTP-response errors, consider still extending `AcdcException` (which extends `HttpRequestException`) for hierarchy consistency, even though the `StatusCode` will be null. This mirrors the Dart approach where `AcdcCacheException` extends `AcdcException` which extends `DioException`.

> **Added from review:** C# exception equality considerations: Exceptions typically do NOT override `Equals()` / `GetHashCode()`. The Dart approach of using `responseData.toString()` for equality is fragile. For C#, consider whether equality is needed (primarily useful for testing). If needed, implement `IEquatable<AcdcException>` rather than overriding `object.Equals()`.

### 8.4 C# Test Strategy Recommendations

| Dart Pattern | C# Equivalent |
|-------------|---------------|
| `package:test` groups + individual tests | xUnit `[Fact]` and `[Theory]` |
| `flutter_test` with `TestWidgetsFlutterBinding` | Standard xUnit (no binding needed) |
| `http_mock_adapter` DioAdapter | `MockHttpMessageHandler` or `RichardSzalay.MockHttp` |
| `shelf`-based FakeOAuthServer | `WireMock.Net` or `Microsoft.AspNetCore.TestHost.TestServer` |
| `FakeTokenProvider` (in-memory) | `FakeTokenProvider` implementing `ITokenProvider` |
| `MockNetworkInfo` (always online) | `MockNetworkInfo` implementing `INetworkInfo` |
| Public API test (type existence checks) | Assembly reflection test scanning exported types |
| Export policy test (filesystem check) | InternalsVisibleTo + API analyzer |
| `expectLater(expr, throwsA(isA<T>()))` | `await Assert.ThrowsAsync<T>(() => expr)` |

> **Added from review:** For structured logging, the C# port should use the standard `ILogger<T>` interface rather than a custom delegate. The `toMap()` method maps well to structured logging:
> ```csharp
> logger.LogError("ACDC exception: {ExceptionType} {StatusCode}",
>     exception.GetType().Name, exception.StatusCode);
> ```

### 8.5 C# Public API Enforcement

Instead of the filesystem-based approach in Dart:

1. Use `[InternalsVisibleTo("TestProject")]` to keep internal types hidden
2. Use `internal` access modifier for implementation classes
3. Use [Microsoft.CodeAnalysis.PublicApiAnalyzers](https://github.com/dotnet/roslyn-analyzers) to track public API surface
4. Write a reflection-based test that validates all expected public types exist in the assembly

```csharp
[Fact]
public void AllExpectedTypesArePublic()
{
    var assembly = typeof(AcdcException).Assembly;
    var publicTypes = assembly.GetExportedTypes().Select(t => t.Name).ToHashSet();

    Assert.Contains("AcdcException", publicTypes);
    Assert.Contains("AcdcAuthException", publicTypes);
    Assert.Contains("AcdcClientException", publicTypes);
    Assert.Contains("AcdcServerException", publicTypes);
    Assert.Contains("AcdcNetworkException", publicTypes);
    Assert.Contains("AcdcCacheException", publicTypes);
    Assert.Contains("AcdcSecurityException", publicTypes);
    Assert.Contains("NetworkErrorType", publicTypes);
    Assert.Contains("CacheOperation", publicTypes);
}
```

---

## Appendix: File Reference

| Dart Source File | Lines | Description |
|-----------------|-------|-------------|
| `lib/src/exceptions/acdc_exception.dart` | 134 | Base exception with URL redaction, body truncation, equality |
| `lib/src/exceptions/acdc_auth_exception.dart` | 65 | 401/403 auth/authz errors |
| `lib/src/exceptions/acdc_client_exception.dart` | 116 | 4xx client errors with Retry-After parsing |
| `lib/src/exceptions/acdc_server_exception.dart` | 51 | 5xx server errors |
| `lib/src/exceptions/acdc_network_exception.dart` | 111 | Network errors with typed enum |
| `lib/src/exceptions/acdc_cache_exception.dart` | 119 | Cache errors with operation type and named factories |
| `lib/src/exceptions/acdc_security_exception.dart` | 40 | Certificate pinning failures |
| `test/exceptions/acdc_exception_test.dart` | 101 | Base exception unit tests |
| `test/exceptions/acdc_auth_exception_test.dart` | 82 | Auth exception tests |
| `test/exceptions/acdc_client_exception_test.dart` | 108 | Client exception tests (inc. Retry-After) |
| `test/exceptions/acdc_server_exception_test.dart` | 53 | Server exception tests |
| `test/exceptions/acdc_network_exception_test.dart` | 88 | Network exception tests |
| `test/exceptions/acdc_cache_exception_test.dart` | 99 | Cache exception tests |
| `test/exceptions/exception_equality_test.dart` | 81 | Cross-type equality tests |
| `test/public_api_test.dart` | 250 | Public API surface verification |
| `test/enforce_export_policy_test.dart` | 60 | Filesystem export policy enforcement |
| `test/integration/builder_reusability_test.dart` | 63 | Builder independence tests |
| `test/integration/app_lifecycle_test.dart` | 496 | Token refresh lifecycle tests |
| `test/integration/complete_client_integration_test.dart` | 440 | Full integration tests |
| `test/integration/custom_logger_integration_test.dart` | 98 | Custom logger integration |
| `test/integration/cancel_all_integration_test.dart` | 83 | Request cancellation tests |
| `test/helpers/fake_token_provider.dart` | 57 | In-memory token provider |
| `test/helpers/mock_network_info.dart` | 19 | Always-online network mock |
| `test/helpers/fake_oauth_server.dart` | 221 | Shelf-based OAuth server |
| `pubspec.yaml` | 43 | Package dependencies |
| `lib/dart_acdc.dart` | 267 | Public API barrel file |
| `example/example.dart` | 163 | Usage examples |
