# Tasks: Add Exception Hierarchy and ErrorHandler

## 1. Exception Hierarchy

- [ ] 1.1 Create `NetworkErrorType` enum (`ConnectionRefused`, `DnsResolutionFailed`, `Timeout`, `SslHandshakeFailed`, `ConnectionReset`, `Unknown`)
- [ ] 1.2 Create `CacheOperation` enum (`Read`, `Write`, `Delete`, `Clear`, `Serialize`)
- [ ] 1.3 Implement `AcdcException` base class extending `HttpRequestException` with `RedactUrl()`, `TruncateResponseBody()` (500 char limit), and `ToMap()` methods
- [ ] 1.4 Implement `AcdcAuthException` with `FromStatusCode()` factory method generating status-specific messages (401: "Authentication failed: Invalid or expired token", 403: "Authorization failed: Insufficient permissions")
- [ ] 1.5 Implement `AcdcClientException` with `RetryAfter` property parsed from `RetryConditionHeaderValue`
- [ ] 1.6 Implement `AcdcServerException` for 5xx errors
- [ ] 1.7 Implement `AcdcNetworkException` with `NetworkErrorType` property and mapping from .NET 8 `HttpRequestError` enum
- [ ] 1.8 Implement `AcdcCacheException` with `CacheOperation` property and stub factory methods (`ReadFailed`, `WriteFailed`, `DeleteFailed`, `ClearFailed`) -- finalized in P6

## 2. Error Handler

- [ ] 2.1 Implement `ErrorHandler : DelegatingHandler` with status code routing (401/403 to `AcdcAuthException`, 4xx to `AcdcClientException`, 5xx to `AcdcServerException`)
- [ ] 2.2 Implement network error mapping (`HttpRequestException` with `HttpRequestError` to `AcdcNetworkException`)
- [ ] 2.3 Implement timeout detection (`TaskCanceledException` when not user-cancelled maps to `AcdcNetworkException` with `NetworkErrorType.Timeout`)
- [ ] 2.4 Implement passthrough for existing `AcdcException` instances (re-throw without re-wrapping)

## 3. Request Options

- [ ] 3.1 Create `AcdcRequestOptions` static class with typed `HttpRequestOptionsKey<T>` constants for per-request metadata

## 4. Unit Tests

- [ ] 4.1 Test all exception type constructors and properties (`AcdcExceptionTests`, `AcdcAuthExceptionTests`, `AcdcClientExceptionTests`, `AcdcServerExceptionTests`, `AcdcNetworkExceptionTests`, `AcdcCacheExceptionTests`)
- [ ] 4.2 Test `RedactUrl()` with various URL patterns (with query params, without query params, relative paths, URLs with credentials)
- [ ] 4.3 Test `TruncateResponseBody()` edge cases (null, empty, under limit, exactly at limit, over limit)
- [ ] 4.4 Test `ToMap()` serialization (all fields present, null fields, subclass-specific fields)
- [ ] 4.5 Test `AcdcAuthException.FromStatusCode()` factory (401, 403, other status codes)
- [ ] 4.6 Test `AcdcClientException.RetryAfter` parsing (from `RetryConditionHeaderValue` with `Delta`, with `Date`, null header)
- [ ] 4.7 Test `AcdcNetworkException` `HttpRequestError` mapping (all `NetworkErrorType` values, unknown/unmapped values)
- [ ] 4.8 Test ErrorHandler status code routing (401 to auth, 403 to auth, 404 to client, 429 to client with RetryAfter, 500 to server, 503 to server)
- [ ] 4.9 Test ErrorHandler network error conversion (`HttpRequestException` to `AcdcNetworkException`)
- [ ] 4.10 Test ErrorHandler passthrough behavior (existing `AcdcException` re-thrown as-is)
- [ ] 4.11 Test ErrorHandler timeout detection (`TaskCanceledException` with non-cancelled token maps to timeout; `OperationCanceledException` with cancelled token propagates as-is)
