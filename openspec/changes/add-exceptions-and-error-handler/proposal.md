# Change: Add Exception Hierarchy and ErrorHandler

## Why

All handlers in the CSharp-ACDC pipeline need a shared exception hierarchy for typed error handling, and the ErrorHandler converts raw .NET exceptions into domain-specific ACDC exceptions. This is the foundational layer (P2) that P3 through P6 all depend on. Without typed exceptions, downstream handlers (Auth, Cache, Logging, Deduplication) cannot distinguish between transient network failures, auth errors, and client/server errors -- a distinction that is critical for correct retry, token refresh, and cache fallback behavior.

## What Changes

### Exception Enums
- **`NetworkErrorType`** enum -- `ConnectionRefused`, `DnsResolutionFailed`, `Timeout`, `SslHandshakeFailed`, `ConnectionReset`, `Unknown`
- **`CacheOperation`** enum -- `Read`, `Write`, `Delete`, `Clear`, `Serialize`

### Exception Hierarchy
- **`AcdcException`** base class extending `HttpRequestException` -- provides `ToMap()`, `RedactUrl()`, `TruncateResponseBody()` (max 500 chars)
- **`AcdcAuthException`** -- represents 401/403 failures with status-specific messages and `FromStatusCode()` factory method
- **`AcdcClientException`** -- represents 4xx errors with nullable `RetryAfter` property parsed from `RetryConditionHeaderValue`
- **`AcdcServerException`** -- represents 5xx errors
- **`AcdcNetworkException`** -- classifies network failures using `NetworkErrorType`, maps from .NET 8's `HttpRequestException.HttpRequestError`
- **`AcdcCacheException`** -- represents cache operation failures using `CacheOperation` enum with stub factory methods (finalized in P6)

### ErrorHandler
- **`ErrorHandler : DelegatingHandler`** -- pure error-phase handler that catches exceptions in the downstream pipeline and maps them to typed ACDC exceptions. Routes HTTP 401/403 to `AcdcAuthException`, 4xx to `AcdcClientException`, 5xx to `AcdcServerException`. Maps `HttpRequestException` network errors to `AcdcNetworkException`. Maps `TaskCanceledException` (when not user-cancelled) to `AcdcNetworkException` with `NetworkErrorType.Timeout`. Re-throws existing `AcdcException` instances without re-wrapping.

### Request Options
- **`AcdcRequestOptions`** -- static class with typed `HttpRequestOptionsKey<T>` constants for per-request metadata used by all subsequent handlers

### Excluded
- **`AcdcSecurityException`** is NOT ported -- server-side cert validation uses `HttpClientHandler.ServerCertificateCustomValidationCallback`, not a pipeline handler. This matches the Dart source where `AcdcSecurityException` is not exported from the public API and is thrown by `PinningVerifier`, not the ErrorInterceptor.

### Unit Tests
- Tests for ALL exception type constructors and properties
- Tests for `RedactUrl()` with various URL patterns
- Tests for `TruncateResponseBody()` edge cases
- Tests for `ToMap()` serialization
- Tests for `AcdcAuthException.FromStatusCode()` factory
- Tests for `AcdcClientException.RetryAfter` parsing
- Tests for `AcdcNetworkException` `HttpRequestError` mapping
- Tests for ErrorHandler status code routing, network error conversion, passthrough, and timeout detection

## Impact

- **Affected specs:** exceptions (new), error-handler (new)
- **Affected code:**
  - `src/CSharpAcdc/Exceptions/` -- all exception types and enums
  - `src/CSharpAcdc/Handlers/ErrorHandler.cs` -- error conversion handler
  - `src/CSharpAcdc/Extensions/AcdcRequestOptions.cs` -- typed request option keys
  - `tests/CSharpAcdc.Tests/Exceptions/` -- 7+ test files
  - `tests/CSharpAcdc.Tests/Handlers/ErrorHandlerTests.cs` -- handler tests
- **Enables:** P3 (LoggingHandler), P4 (AuthHandler), P5 (CancellationHandler + DeduplicationHandler), P6 (CacheHandler) -- all Layer 2 handlers depend on these exception types for error classification

### Files to be created

**Source files:**
- `src/CSharpAcdc/Exceptions/NetworkErrorType.cs`
- `src/CSharpAcdc/Exceptions/CacheOperation.cs`
- `src/CSharpAcdc/Exceptions/AcdcException.cs`
- `src/CSharpAcdc/Exceptions/AcdcAuthException.cs`
- `src/CSharpAcdc/Exceptions/AcdcClientException.cs`
- `src/CSharpAcdc/Exceptions/AcdcServerException.cs`
- `src/CSharpAcdc/Exceptions/AcdcNetworkException.cs`
- `src/CSharpAcdc/Exceptions/AcdcCacheException.cs`
- `src/CSharpAcdc/Handlers/ErrorHandler.cs`
- `src/CSharpAcdc/Extensions/AcdcRequestOptions.cs`

**Test files:**
- `tests/CSharpAcdc.Tests/Exceptions/AcdcExceptionTests.cs`
- `tests/CSharpAcdc.Tests/Exceptions/AcdcAuthExceptionTests.cs`
- `tests/CSharpAcdc.Tests/Exceptions/AcdcClientExceptionTests.cs`
- `tests/CSharpAcdc.Tests/Exceptions/AcdcServerExceptionTests.cs`
- `tests/CSharpAcdc.Tests/Exceptions/AcdcNetworkExceptionTests.cs`
- `tests/CSharpAcdc.Tests/Exceptions/AcdcCacheExceptionTests.cs`
- `tests/CSharpAcdc.Tests/Exceptions/ExceptionEqualityTests.cs`
- `tests/CSharpAcdc.Tests/Handlers/ErrorHandlerTests.cs`
