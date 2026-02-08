## ADDED Requirements

### Requirement: Error Conversion
`ErrorHandler` SHALL be a `DelegatingHandler` that catches exceptions thrown by downstream handlers and converts them to typed ACDC exceptions. It SHALL wrap the call to `base.SendAsync()` in a try/catch block and map raw .NET exceptions and HTTP error responses into the appropriate `AcdcException` subclass.

#### Scenario: Successful response passthrough
- **WHEN** the downstream pipeline returns a successful response (2xx status code)
- **THEN** the `ErrorHandler` SHALL return the response unchanged
- **AND** SHALL NOT throw any exception

#### Scenario: Non-success response without exception
- **WHEN** the downstream pipeline returns a response with a non-success status code (e.g., 500)
- **AND** no exception was thrown by the downstream pipeline
- **THEN** the `ErrorHandler` SHALL convert the response into the appropriate typed `AcdcException`

### Requirement: Status Code Routing
`ErrorHandler` SHALL route HTTP responses with non-success status codes to the appropriate `AcdcException` subclass based on the following rules:
- 401 (Unauthorized) SHALL be routed to `AcdcAuthException`
- 403 (Forbidden) SHALL be routed to `AcdcAuthException`
- Other 4xx status codes SHALL be routed to `AcdcClientException`
- 5xx status codes SHALL be routed to `AcdcServerException`

#### Scenario: 401 Unauthorized response
- **WHEN** the downstream pipeline returns a response with status code 401
- **THEN** the `ErrorHandler` SHALL throw an `AcdcAuthException`
- **AND** the exception `StatusCode` SHALL be `HttpStatusCode.Unauthorized`

#### Scenario: 403 Forbidden response
- **WHEN** the downstream pipeline returns a response with status code 403
- **THEN** the `ErrorHandler` SHALL throw an `AcdcAuthException`
- **AND** the exception `StatusCode` SHALL be `HttpStatusCode.Forbidden`

#### Scenario: 404 Not Found response
- **WHEN** the downstream pipeline returns a response with status code 404
- **THEN** the `ErrorHandler` SHALL throw an `AcdcClientException`
- **AND** the exception `StatusCode` SHALL be `HttpStatusCode.NotFound`

#### Scenario: 429 Too Many Requests with Retry-After header
- **WHEN** the downstream pipeline returns a response with status code 429 and a `Retry-After` header
- **THEN** the `ErrorHandler` SHALL throw an `AcdcClientException`
- **AND** the exception `RetryAfter` property SHALL be populated from the header value

#### Scenario: 500 Internal Server Error response
- **WHEN** the downstream pipeline returns a response with status code 500
- **THEN** the `ErrorHandler` SHALL throw an `AcdcServerException`
- **AND** the exception `StatusCode` SHALL be `HttpStatusCode.InternalServerError`

#### Scenario: 503 Service Unavailable response
- **WHEN** the downstream pipeline returns a response with status code 503
- **THEN** the `ErrorHandler` SHALL throw an `AcdcServerException`
- **AND** the exception `StatusCode` SHALL be `HttpStatusCode.ServiceUnavailable`

### Requirement: Network Error Mapping
`ErrorHandler` SHALL catch `HttpRequestException` thrown by the downstream pipeline and convert it to `AcdcNetworkException` with the appropriate `NetworkErrorType` based on the `HttpRequestError` property.

#### Scenario: DNS resolution failure
- **WHEN** the downstream pipeline throws an `HttpRequestException` with `HttpRequestError.NameResolutionError`
- **THEN** the `ErrorHandler` SHALL throw an `AcdcNetworkException` with `NetworkErrorType.DnsResolutionFailed`
- **AND** the `InnerException` SHALL be the original `HttpRequestException`

#### Scenario: Connection refused
- **WHEN** the downstream pipeline throws an `HttpRequestException` with `HttpRequestError.ConnectionError`
- **THEN** the `ErrorHandler` SHALL throw an `AcdcNetworkException` with `NetworkErrorType.ConnectionRefused`

#### Scenario: SSL handshake failure
- **WHEN** the downstream pipeline throws an `HttpRequestException` with `HttpRequestError.SecureConnectionError`
- **THEN** the `ErrorHandler` SHALL throw an `AcdcNetworkException` with `NetworkErrorType.SslHandshakeFailed`

### Requirement: Timeout Handling
`ErrorHandler` SHALL map `TaskCanceledException` to `AcdcNetworkException` with `NetworkErrorType.Timeout` when the cancellation was NOT initiated by the user (i.e., the `CancellationToken` is not marked as cancelled).

When the `CancellationToken` IS marked as cancelled (user-initiated cancellation), the `ErrorHandler` SHALL re-throw the `TaskCanceledException` or `OperationCanceledException` without conversion, allowing the caller to handle cancellation directly.

#### Scenario: HTTP timeout (not user-cancelled)
- **WHEN** the downstream pipeline throws a `TaskCanceledException`
- **AND** the request's `CancellationToken.IsCancellationRequested` is `false`
- **THEN** the `ErrorHandler` SHALL throw an `AcdcNetworkException` with `NetworkErrorType.Timeout`

#### Scenario: User-initiated cancellation
- **WHEN** the downstream pipeline throws a `TaskCanceledException` or `OperationCanceledException`
- **AND** the request's `CancellationToken.IsCancellationRequested` is `true`
- **THEN** the `ErrorHandler` SHALL re-throw the original exception without conversion

### Requirement: Passthrough
`ErrorHandler` SHALL re-throw exceptions that are already instances of `AcdcException` without re-wrapping them. This prevents double-wrapping when an upstream handler (e.g., AuthHandler) throws a typed ACDC exception.

#### Scenario: AcdcAuthException passthrough
- **WHEN** the downstream pipeline throws an `AcdcAuthException`
- **THEN** the `ErrorHandler` SHALL re-throw the same `AcdcAuthException` instance
- **AND** SHALL NOT wrap it in another exception

#### Scenario: AcdcNetworkException passthrough
- **WHEN** the downstream pipeline throws an `AcdcNetworkException`
- **THEN** the `ErrorHandler` SHALL re-throw the same `AcdcNetworkException` instance

### Requirement: Error Phase Only
`ErrorHandler` SHALL NOT modify outgoing requests or successful responses. It SHALL only participate in the error handling phase by wrapping `base.SendAsync()` in a try/catch block and checking response status codes.

#### Scenario: Request headers unmodified
- **WHEN** a request passes through the `ErrorHandler`
- **THEN** the request headers, URL, method, and body SHALL be unchanged when forwarded to the downstream pipeline

#### Scenario: Successful response unmodified
- **WHEN** the downstream pipeline returns a 200 OK response
- **THEN** the `ErrorHandler` SHALL return the response with all headers, body, and status code unchanged

### Requirement: Response Body Capture
`ErrorHandler` SHALL attempt to read the response body from error responses and include it (truncated) in the thrown exception via `AcdcException.TruncateResponseBody()`. If reading the response body fails, the exception SHALL be created with a null response body.

#### Scenario: Error response with body
- **WHEN** the downstream pipeline returns a 500 response with body `{"error": "Internal Server Error"}`
- **THEN** the thrown `AcdcServerException` SHALL have a `ResponseBody` containing the response body text (truncated if necessary)

#### Scenario: Error response with body exceeding limit
- **WHEN** the downstream pipeline returns a 500 response with a body longer than 500 characters
- **THEN** the thrown `AcdcServerException` SHALL have a `ResponseBody` truncated to 500 characters with `[truncated]` suffix

#### Scenario: Error response body read failure
- **WHEN** the downstream pipeline returns an error response
- **AND** reading the response body throws an exception
- **THEN** the `ErrorHandler` SHALL still throw the appropriate typed exception
- **AND** the `ResponseBody` SHALL be null

### Requirement: URL Redaction in ErrorHandler
`ErrorHandler` SHALL redact the request URL using `AcdcException.RedactUrl()` before including it in any thrown exception.

#### Scenario: Error response includes redacted URL
- **WHEN** the downstream pipeline returns a 404 response for URL `https://api.example.com/users/123?token=secret`
- **THEN** the thrown `AcdcClientException` SHALL have `RequestUrl` set to the redacted form of the URL
- **AND** the original URL with query parameters SHALL NOT appear in the exception
