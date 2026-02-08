## ADDED Requirements

### Requirement: Exception Base Class
`AcdcException` SHALL extend `HttpRequestException` and serve as the base class for all ACDC domain exceptions. It SHALL expose the following properties:
- `StatusCode` (`HttpStatusCode?`) -- inherited from `HttpRequestException`
- `ResponseBody` (`string?`) -- truncated response body for diagnostics
- `RequestUrl` (`string?`) -- redacted request URL for diagnostics

It SHALL provide the following methods:
- `ToMap()` -- returns a `Dictionary<string, object?>` with keys `type`, `message`, `statusCode`, `requestUrl`, `responseBody`, and `originalError` for structured logging
- `RedactUrl(string url)` -- static method that strips query parameters and masks path segments
- `TruncateResponseBody(string? body, int maxLength = 500)` -- static method that truncates body to the specified limit

#### Scenario: Create AcdcException with all properties
- **WHEN** an `AcdcException` is created with message "Test error", status code 500, response body "error details", and request URL "https://api.example.com/users?token=abc"
- **THEN** the `Message` property SHALL be "Test error"
- **AND** the `StatusCode` property SHALL be `HttpStatusCode.InternalServerError`
- **AND** the `ResponseBody` property SHALL be "error details"
- **AND** the `RequestUrl` property SHALL be the redacted URL

#### Scenario: Create AcdcException with null optional properties
- **WHEN** an `AcdcException` is created with only a message
- **THEN** `StatusCode` SHALL be null
- **AND** `ResponseBody` SHALL be null
- **AND** `RequestUrl` SHALL be null

#### Scenario: ToMap serialization
- **WHEN** `ToMap()` is called on an `AcdcException` instance
- **THEN** the returned dictionary SHALL contain keys `type`, `message`, `statusCode`, `requestUrl`, `responseBody`, and `originalError`
- **AND** the `type` key SHALL contain the runtime type name of the exception

### Requirement: URL Redaction
`AcdcException.RedactUrl()` SHALL strip query parameters and mask path segments after the domain to prevent PII leakage in logs and exception messages.

#### Scenario: URL with query parameters
- **WHEN** `RedactUrl("https://api.example.com/users/12345/orders?token=abc&page=1")` is called
- **THEN** the result SHALL be `"https://api.example.com/***"`
- **AND** no query parameters SHALL be present in the output

#### Scenario: URL without query parameters
- **WHEN** `RedactUrl("https://api.example.com/users/12345")` is called
- **THEN** the result SHALL be `"https://api.example.com/***"`

#### Scenario: URL with only domain (no path)
- **WHEN** `RedactUrl("https://api.example.com")` is called
- **THEN** the result SHALL be `"https://api.example.com"`

#### Scenario: Malformed URL
- **WHEN** `RedactUrl()` is called with a malformed URL string
- **THEN** the method SHALL NOT throw an exception
- **AND** SHALL return a safe fallback string

### Requirement: Response Body Truncation
`AcdcException.TruncateResponseBody()` SHALL limit response body length to prevent memory issues in exception objects and log entries.

#### Scenario: Body under the limit
- **WHEN** `TruncateResponseBody("short body", maxLength: 500)` is called
- **THEN** the result SHALL be `"short body"` unchanged

#### Scenario: Body over the limit
- **WHEN** `TruncateResponseBody()` is called with a body longer than 500 characters
- **THEN** the result SHALL be the first 500 characters followed by `[truncated]`

#### Scenario: Null body
- **WHEN** `TruncateResponseBody(null)` is called
- **THEN** the result SHALL be null

#### Scenario: Empty body
- **WHEN** `TruncateResponseBody("")` is called
- **THEN** the result SHALL be an empty string

### Requirement: Auth Exception
`AcdcAuthException` SHALL extend `AcdcException` and represent authentication (401) and authorization (403) failures. It SHALL provide a static `FromStatusCode()` factory method that generates status-specific messages.

#### Scenario: 401 Unauthorized
- **WHEN** `AcdcAuthException.FromStatusCode(HttpStatusCode.Unauthorized, ...)` is called
- **THEN** the exception message SHALL contain "Authentication failed" or "Invalid or expired token"
- **AND** the `StatusCode` SHALL be `HttpStatusCode.Unauthorized`

#### Scenario: 403 Forbidden
- **WHEN** `AcdcAuthException.FromStatusCode(HttpStatusCode.Forbidden, ...)` is called
- **THEN** the exception message SHALL contain "Authorization failed" or "Insufficient permissions"
- **AND** the `StatusCode` SHALL be `HttpStatusCode.Forbidden`

#### Scenario: Custom message override
- **WHEN** `AcdcAuthException` is created with a custom message
- **THEN** the custom message SHALL be used instead of the default status-specific message

### Requirement: Client Exception
`AcdcClientException` SHALL extend `AcdcException` and represent client errors (4xx status codes excluding 401 and 403). It SHALL expose a nullable `RetryAfter` property of type `TimeSpan?`.

#### Scenario: 429 Too Many Requests with Retry-After header
- **WHEN** an `AcdcClientException` is created from a 429 response that includes a `Retry-After` header with a delta value of 60 seconds
- **THEN** the `RetryAfter` property SHALL be a `TimeSpan` of 60 seconds
- **AND** `ToMap()` SHALL include a `retryAfter` key with the value in seconds

#### Scenario: 404 Not Found without Retry-After
- **WHEN** an `AcdcClientException` is created from a 404 response
- **THEN** the `RetryAfter` property SHALL be null

#### Scenario: Client exception ToMap includes RetryAfter
- **WHEN** `ToMap()` is called on an `AcdcClientException` with a non-null `RetryAfter`
- **THEN** the dictionary SHALL include a `retryAfter` key with the value in total seconds

### Requirement: Server Exception
`AcdcServerException` SHALL extend `AcdcException` and represent server errors (5xx status codes).

#### Scenario: 500 Internal Server Error
- **WHEN** an `AcdcServerException` is created from a 500 response
- **THEN** the `StatusCode` SHALL be `HttpStatusCode.InternalServerError`
- **AND** the message SHALL indicate a server error

#### Scenario: 503 Service Unavailable
- **WHEN** an `AcdcServerException` is created from a 503 response
- **THEN** the `StatusCode` SHALL be `HttpStatusCode.ServiceUnavailable`

### Requirement: Network Exception
`AcdcNetworkException` SHALL extend `AcdcException` and classify network failures using the `NetworkErrorType` enum. It SHALL provide a mapping from `HttpRequestException.HttpRequestError` to `NetworkErrorType`.

#### Scenario: DNS resolution failure
- **WHEN** an `HttpRequestException` with `HttpRequestError.NameResolutionError` is caught
- **THEN** the resulting `AcdcNetworkException` SHALL have `NetworkErrorType.DnsResolutionFailed`

#### Scenario: Connection refused
- **WHEN** an `HttpRequestException` with `HttpRequestError.ConnectionError` is caught
- **THEN** the resulting `AcdcNetworkException` SHALL have `NetworkErrorType.ConnectionRefused`

#### Scenario: SSL handshake failure
- **WHEN** an `HttpRequestException` with `HttpRequestError.SecureConnectionError` is caught
- **THEN** the resulting `AcdcNetworkException` SHALL have `NetworkErrorType.SslHandshakeFailed`

#### Scenario: Unknown network error
- **WHEN** an `HttpRequestException` with an unmapped `HttpRequestError` value is caught
- **THEN** the resulting `AcdcNetworkException` SHALL have `NetworkErrorType.Unknown`

#### Scenario: Network exception ToMap includes NetworkErrorType
- **WHEN** `ToMap()` is called on an `AcdcNetworkException`
- **THEN** the dictionary SHALL include a `networkErrorType` key with the enum value name

### Requirement: Cache Exception
`AcdcCacheException` SHALL extend `AcdcException` and represent cache operation failures using the `CacheOperation` enum. It SHALL provide static factory methods for common failure modes.

#### Scenario: Cache read failure
- **WHEN** `AcdcCacheException.ReadFailed(...)` is called
- **THEN** the `CacheOperation` property SHALL be `CacheOperation.Read`
- **AND** the message SHALL indicate a cache read failure

#### Scenario: Cache write failure
- **WHEN** `AcdcCacheException.WriteFailed(...)` is called
- **THEN** the `CacheOperation` property SHALL be `CacheOperation.Write`

#### Scenario: Cache exception ToMap includes CacheOperation
- **WHEN** `ToMap()` is called on an `AcdcCacheException`
- **THEN** the dictionary SHALL include a `cacheOperation` key with the enum value name

### Requirement: NetworkErrorType Enum
`NetworkErrorType` SHALL define the following values for classifying network failures: `ConnectionRefused`, `DnsResolutionFailed`, `Timeout`, `SslHandshakeFailed`, `ConnectionReset`, `Unknown`.

#### Scenario: All enum values defined
- **WHEN** the `NetworkErrorType` enum is inspected
- **THEN** it SHALL contain exactly the values `ConnectionRefused`, `DnsResolutionFailed`, `Timeout`, `SslHandshakeFailed`, `ConnectionReset`, and `Unknown`

### Requirement: CacheOperation Enum
`CacheOperation` SHALL define the following values for classifying cache operations: `Read`, `Write`, `Delete`, `Clear`, `Serialize`.

#### Scenario: All enum values defined
- **WHEN** the `CacheOperation` enum is inspected
- **THEN** it SHALL contain exactly the values `Read`, `Write`, `Delete`, `Clear`, and `Serialize`

### Requirement: Request Options
`AcdcRequestOptions` SHALL be a static class that defines typed `HttpRequestOptionsKey<T>` constants for per-request metadata. These options SHALL be used by handlers to pass metadata through the `HttpRequestMessage.Options` dictionary without storing state in handler instance fields.

#### Scenario: Set and retrieve a typed option
- **WHEN** a handler sets an option value on an `HttpRequestMessage` using an `AcdcRequestOptions` key
- **AND** another handler retrieves the value using the same key
- **THEN** the retrieved value SHALL match the value that was set
- **AND** type safety SHALL be enforced at compile time
