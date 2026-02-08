# Design: Exception Hierarchy and ErrorHandler

## Context

This is the P2 foundational proposal for CSharp-ACDC, porting Dart-ACDC's exception hierarchy (which extends `DioException`) to C# (extending `HttpRequestException`). The exception types and ErrorHandler are cross-cutting: every subsequent handler (P3-P6) depends on these types for error classification, retry decisions, token refresh logic, and cache fallback behavior.

The Dart source defines 6 exception subclasses + 2 enums. The ErrorInterceptor is the central routing component that converts raw Dio errors into typed ACDC exceptions. In C#, the equivalent is a `DelegatingHandler` that wraps `base.SendAsync()` in a try/catch and converts .NET exceptions and HTTP error responses into typed ACDC exceptions.

Key difference from Dart: C#'s `HttpClient` does NOT throw exceptions for 4xx/5xx responses by default -- it returns the response. The ErrorHandler must check response status codes explicitly and throw the appropriate exception. This is fundamentally different from Dio, which throws `DioException` for non-2xx responses.

## Goals

- Typed exception hierarchy matching Dart-ACDC semantics (auth vs client vs server vs network vs cache)
- ErrorHandler as the single conversion point -- no other handler needs to catch raw .NET exceptions
- Shared request options constants via `AcdcRequestOptions` for per-request metadata
- Thread-safe design (all types are immutable or use thread-safe patterns)
- Ecosystem compatibility -- `catch (HttpRequestException)` catches all ACDC exceptions

## Non-Goals

- No `AcdcSecurityException` -- server cert validation is handled by `HttpClientHandler.ServerCertificateCustomValidationCallback`, not a pipeline handler. In the Dart source, `AcdcSecurityException` is not exported from the public API and is thrown by `PinningVerifier`, not by `ErrorInterceptor`.
- No exception equality (`Equals`/`GetHashCode`) -- C# exceptions typically do not override equality. The Dart pattern of `responseData.toString()` comparison is fragile. Tests should use property assertions instead.
- No custom serialization format -- `ToMap()` returns `Dictionary<string, object?>` for structured logging compatibility with `ILogger<T>`.

## Decisions

### 1. Extend `HttpRequestException` (not `Exception`)

**Decision:** `AcdcException` extends `HttpRequestException`.

**Rationale:** Ecosystem compatibility with ASP.NET Core error middleware. Teams already catching `HttpRequestException` for downstream API failures will automatically catch ACDC exceptions, enabling gradual adoption. This mirrors the Dart design philosophy of extending `DioException`.

**Trade-off:** `AcdcCacheException` semantically is not an HTTP request error, but hierarchy consistency outweighs the semantic mismatch. The `StatusCode` property will be null for cache exceptions, which is acceptable. This matches the Dart approach where `AcdcCacheException` extends `AcdcException` (which extends `DioException`) despite not being a Dio-originated error.

**Alternatives considered:**
- Custom `AcdcException : Exception` -- breaks `catch (HttpRequestException)` compatibility
- Hybrid (HTTP exceptions extend `HttpRequestException`, cache extends `Exception`) -- splits the hierarchy, complicates catch blocks

### 2. Use `HttpRequestError` enum for network error classification

**Decision:** Map `HttpRequestException.HttpRequestError` to `NetworkErrorType` enum values.

**Rationale:** Replaces Dart's fragile string matching on error messages (e.g., `"socketexception"`, `"failed host lookup"`). `HttpRequestError` on `HttpRequestException` provides structured error classification:

```
HttpRequestError.NameResolutionError  -> NetworkErrorType.DnsResolutionFailed
HttpRequestError.ConnectionError      -> NetworkErrorType.ConnectionRefused
HttpRequestError.SecureConnectionError -> NetworkErrorType.SslHandshakeFailed
HttpRequestError.HttpProtocolError    -> NetworkErrorType.Unknown
HttpRequestError.Unknown              -> NetworkErrorType.Unknown
```

**Risk mitigation:** `NetworkErrorType.Unknown` as fallback for any unmapped `HttpRequestError` values (future .NET versions may add new values).

### 3. ErrorHandler only overrides error phase

**Decision:** ErrorHandler wraps `base.SendAsync()` in try/catch. It does NOT modify requests or successful responses.

**Rationale:** Mirrors the Dart `ErrorInterceptor` which only overrides `onError` -- it does not participate in request/response phases. The handler:
1. Calls `base.SendAsync()` to invoke the rest of the pipeline
2. Checks the response status code (C#'s `HttpClient` does not throw for 4xx/5xx)
3. Routes to the appropriate exception type based on status code
4. Catches `HttpRequestException` (network errors) and maps to `AcdcNetworkException`
5. Catches `TaskCanceledException` and distinguishes timeout from user cancellation
6. Re-throws existing `AcdcException` instances without re-wrapping

**Key difference from Dart:** In Dio, 4xx/5xx responses automatically throw `DioException`. In C#, `HttpClient` returns the response with the status code. ErrorHandler must explicitly check `response.IsSuccessStatusCode` and throw the appropriate exception for non-success responses.

### 4. URL redaction strategy

**Decision:** Strip query parameters entirely and mask path segments after the domain.

**Rationale:** Prevents PII leakage in logs. Query parameters often contain tokens, API keys, and user identifiers. Path segments may contain user IDs or resource identifiers.

**Implementation:**
- Input: `https://api.example.com/users/12345/orders?token=abc&page=1`
- Output: `https://api.example.com/***`
- Edge cases: relative URLs, URLs without query params, malformed URLs -- all handled gracefully with fallback to the input string

### 5. Response body truncation at 500 characters

**Decision:** Truncate response bodies at 500 characters with `[truncated]` suffix.

**Rationale:** Prevents memory issues with large error responses in exception objects. The Dart source uses 1024 characters (1KB). We reduce this to 500 for server-side where exceptions may be serialized into structured logs, APM traces, and distributed tracing spans. Lower limit reduces log storage costs without losing diagnostic value.

**Note:** The Dart source appends `... (truncated)`. We use `[truncated]` for consistency with .NET logging conventions.

### 6. `AcdcRequestOptions` as static class with `HttpRequestOptionsKey<T>` fields

**Decision:** Type-safe per-request metadata using .NET's `HttpRequestMessage.Options` dictionary with typed keys.

**Rationale:** `DelegatingHandler` instances are pooled by `IHttpClientFactory` (2-min default lifetime). They MUST NOT store per-request state in instance fields. `HttpRequestMessage.Options` provides a per-request dictionary that survives the entire handler pipeline. Typed keys prevent string key collisions and provide compile-time safety.

**Maps from Dart:** `Dio.options.extra` (untyped `Map<String, dynamic>`) to `HttpRequestMessage.Options` with `HttpRequestOptionsKey<T>` (typed).

### 7. `AcdcCacheException` factory methods left as stubs

**Decision:** Define factory method signatures (`ReadFailed`, `WriteFailed`, `DeleteFailed`, `ClearFailed`) but leave implementation as simple constructors. Finalize in P6 when FusionCache error patterns are known.

**Rationale:** The Dart source creates synthetic `DioException` wrappers in cache factory constructors because `AcdcException` requires `DioException` as `originalException`. In C#, we do not need this artificial wrapping. The factory methods will be refined in P6 to capture actual FusionCache exception details (`FusionCacheException`, Redis `ConnectionException`, etc.).

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| Exception hierarchy too deep (3 levels: `HttpRequestException` -> `AcdcException` -> specific) | Only 1 level of custom inheritance; .NET handles 3-level hierarchies well in catch blocks |
| `HttpRequestError` enum may not cover all network failure cases | `NetworkErrorType.Unknown` fallback; handler logs unmapped values for debugging |
| `HttpClient` does not throw for 4xx/5xx (unlike Dio) | ErrorHandler explicitly checks `response.IsSuccessStatusCode` and throws -- well-tested pattern |
| Response body reading in ErrorHandler may fail | Wrap `response.Content.ReadAsStringAsync()` in try/catch; use null on failure |
| `AcdcCacheException` extending `HttpRequestException` is semantically imprecise | Accepted for hierarchy consistency; `StatusCode` will be null for cache exceptions |

## Open Questions

- Should `AcdcCacheException.CacheOperation` include a `Serialize` variant for serialization failures, or is that covered by `Read`/`Write`? (Current decision: include `Serialize` as distinct operation, can be revised in P6)
- Should `ToMap()` include a stack trace key? (Current decision: no -- stack traces are captured by `ILogger` exception logging, not by `ToMap()`)
