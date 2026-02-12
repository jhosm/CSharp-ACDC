# Capability: Logging Handler

Structured HTTP request/response logging with sensitive data redaction for the CSharp-ACDC `DelegatingHandler` pipeline.

## ADDED Requirements

### Requirement: Request Logging

LoggingHandler SHALL log the HTTP request method, redacted URL, and redacted headers at Information level before sending the request via `base.SendAsync()`. The log entry MUST include the request method (e.g., GET, POST), the full URL with sensitive query parameters redacted, and all headers with sensitive header values redacted.

#### Scenario: Standard GET request is logged

- **WHEN** a GET request is sent to `https://api.example.com/users?token=abc123`
- **THEN** LoggingHandler SHALL emit an Information-level log entry containing the method `GET`, the URL with the `token` query parameter value replaced by `[REDACTED]`, and all request headers with sensitive values redacted
- **AND** the log entry SHALL be emitted before the request is sent downstream

#### Scenario: POST request with headers is logged

- **WHEN** a POST request is sent with an `Authorization: Bearer eyJ...` header
- **THEN** LoggingHandler SHALL emit an Information-level log entry containing the method `POST`, the request URL, and the `Authorization` header value replaced by `[REDACTED]`

### Requirement: Response Logging

LoggingHandler SHALL log the HTTP response status code, redacted response headers, and elapsed time in milliseconds at Information level after receiving the response from `base.SendAsync()`.

#### Scenario: Successful response is logged with timing

- **WHEN** a request completes with status code 200 after 150ms
- **THEN** LoggingHandler SHALL emit an Information-level log entry containing the status code `200` and the elapsed time `150ms`

#### Scenario: Error response is logged with timing

- **WHEN** a request completes with status code 500 after 2000ms
- **THEN** LoggingHandler SHALL emit an Information-level log entry containing the status code `500` and the elapsed time `2000ms`

### Requirement: Sensitive Data Redaction

LoggingHandler SHALL redact sensitive fields in headers, query parameters, and request/response bodies by replacing their values with the literal string `[REDACTED]`. The default sensitive field list MUST include: `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key`, `password`, `token`, `secret`, `key`, `credential`, `access_token`, `refresh_token`, `client_secret`, `api_key`, `private_key`, `session_id`. Field name matching SHALL be case-insensitive.

#### Scenario: Authorization header is redacted

- **WHEN** a request contains the header `Authorization: Bearer eyJhbGciOiJSUzI1Ni...`
- **THEN** LoggingHandler SHALL log the header as `Authorization: [REDACTED]`

#### Scenario: Multiple sensitive headers are redacted

- **WHEN** a request contains headers `Authorization: Bearer abc`, `Cookie: session=xyz`, and `Content-Type: application/json`
- **THEN** LoggingHandler SHALL log `Authorization: [REDACTED]`, `Cookie: [REDACTED]`, and `Content-Type: application/json` (non-sensitive headers are preserved)

#### Scenario: Sensitive query parameters are redacted

- **WHEN** a request URL is `https://api.example.com/auth?client_secret=s3cr3t&redirect_uri=https://app.example.com`
- **THEN** LoggingHandler SHALL log the URL with `client_secret=[REDACTED]` and `redirect_uri=https://app.example.com` preserved

#### Scenario: Sensitive JSON body fields are redacted

- **WHEN** a request body contains JSON `{"username": "alice", "password": "hunter2", "remember": true}`
- **THEN** LoggingHandler SHALL log the body with `password` value replaced by `[REDACTED]` and other fields preserved

### Requirement: Configurable Sensitive Fields

`AcdcLoggingOptions` SHALL allow consumers to add custom sensitive field names to the default redaction list via the `SensitiveFields` collection. Custom fields SHALL be merged with the default 16 fields. Field name matching SHALL be case-insensitive.

#### Scenario: Custom sensitive field is added

- **WHEN** `AcdcLoggingOptions.SensitiveFields` includes `"X-Internal-Token"` in addition to the defaults
- **AND** a request contains the header `X-Internal-Token: tok_12345`
- **THEN** LoggingHandler SHALL log the header as `X-Internal-Token: [REDACTED]`

#### Scenario: Default fields remain active with custom additions

- **WHEN** `AcdcLoggingOptions.SensitiveFields` includes `"X-Custom-Secret"`
- **AND** a request contains headers `Authorization: Bearer abc` and `X-Custom-Secret: xyz`
- **THEN** both `Authorization` and `X-Custom-Secret` header values SHALL be redacted

### Requirement: Slow Request Warning

LoggingHandler SHALL log a Warning-level message when the total request duration (from sending the request to receiving the response) exceeds the configurable threshold defined in `AcdcLoggingOptions.SlowRequestThreshold`. The default threshold MUST be 3 seconds. The warning MUST include the elapsed time and the redacted request URL.

#### Scenario: Request exceeding threshold triggers warning

- **WHEN** a request to `https://api.example.com/data` takes 3500ms to complete
- **AND** the `SlowRequestThreshold` is configured as 3 seconds (default)
- **THEN** LoggingHandler SHALL emit a Warning-level log entry indicating the request was slow, including the elapsed time `3500ms` and the redacted URL

#### Scenario: Request within threshold does not trigger warning

- **WHEN** a request takes 2900ms to complete
- **AND** the `SlowRequestThreshold` is configured as 3 seconds (default)
- **THEN** LoggingHandler SHALL NOT emit a Warning-level slow request log entry

#### Scenario: Custom threshold is respected

- **WHEN** `AcdcLoggingOptions.SlowRequestThreshold` is configured as 1 second
- **AND** a request takes 1200ms to complete
- **THEN** LoggingHandler SHALL emit a Warning-level slow request log entry

### Requirement: Large Payload Warning

LoggingHandler SHALL log a Warning-level message when the request body size or response body `Content-Length` exceeds the configurable threshold defined in `AcdcLoggingOptions.LargePayloadThreshold`. The default threshold MUST be 1 MiB (1,048,576 bytes). The warning MUST include the payload size and the redacted request URL.

#### Scenario: Large request body triggers warning

- **WHEN** a POST request has a body of 1,200,000 bytes
- **AND** the `LargePayloadThreshold` is configured as 1 MiB (default)
- **THEN** LoggingHandler SHALL emit a Warning-level log entry indicating a large request payload, including the size and the redacted URL

#### Scenario: Large response body triggers warning

- **WHEN** a response has a `Content-Length` of 2,000,000 bytes
- **AND** the `LargePayloadThreshold` is configured as 1 MiB (default)
- **THEN** LoggingHandler SHALL emit a Warning-level log entry indicating a large response payload

#### Scenario: Payload within threshold does not trigger warning

- **WHEN** a request body is 500,000 bytes
- **AND** the `LargePayloadThreshold` is configured as 1 MiB (default)
- **THEN** LoggingHandler SHALL NOT emit a Warning-level large payload log entry

### Requirement: Reentrancy Prevention

LoggingHandler SHALL use `AsyncLocal<bool>` to track whether logging is already active in the current async context. When the handler detects it is being invoked from within an already-logged request (e.g., a token refresh HTTP call triggered by AuthHandler), it SHALL skip all logging and pass the request directly to `base.SendAsync()`. This prevents duplicate or recursive log entries for internal pipeline requests.

#### Scenario: Nested request from token refresh skips logging

- **WHEN** AuthHandler triggers an internal HTTP request for token refresh during the processing of an outer request
- **AND** the outer request has already set the `AsyncLocal<bool>` logging flag to `true`
- **THEN** LoggingHandler SHALL NOT emit any log entries for the nested token refresh request
- **AND** LoggingHandler SHALL pass the nested request directly to `base.SendAsync()`

#### Scenario: Independent requests are logged normally

- **WHEN** two independent HTTP requests are sent concurrently from different async contexts
- **THEN** LoggingHandler SHALL log both requests independently, each with their own timing and details

#### Scenario: Logging flag is reset after outer request completes

- **WHEN** an outer request completes (successfully or with an error)
- **THEN** the `AsyncLocal<bool>` logging flag SHALL be reset to `false`
- **AND** subsequent requests in the same async context SHALL be logged normally

### Requirement: Error Logging

LoggingHandler SHALL log exceptions at Error level when `base.SendAsync()` throws. The error log entry MUST include the exception type, exception message, elapsed time, and redacted request details (method, URL, headers). The original exception MUST be re-thrown after logging.

#### Scenario: Network timeout exception is logged

- **WHEN** a request to `https://api.example.com/data` throws a `TaskCanceledException` after 30 seconds
- **THEN** LoggingHandler SHALL emit an Error-level log entry containing the exception type `TaskCanceledException`, the elapsed time, and the redacted request URL
- **AND** LoggingHandler SHALL re-throw the original exception

#### Scenario: HttpRequestException is logged with redacted details

- **WHEN** a request with an `Authorization` header throws an `HttpRequestException`
- **THEN** LoggingHandler SHALL emit an Error-level log entry with the `Authorization` header value redacted as `[REDACTED]`
- **AND** LoggingHandler SHALL re-throw the original exception

### Requirement: Per-Request Logging Opt-Out

LoggingHandler SHALL skip all logging (request, response, error, warnings) when `AcdcRequestOptions.SuppressLogging` is set to `true` on the `HttpRequestMessage.Options`. When logging is suppressed, the handler MUST still pass the request to `base.SendAsync()` and return the response unchanged.

#### Scenario: Request with SuppressLogging produces no log output

- **WHEN** a request has `AcdcRequestOptions.SuppressLogging` set to `true`
- **THEN** LoggingHandler SHALL NOT emit any log entries (Information, Warning, or Error level)
- **AND** LoggingHandler SHALL pass the request to `base.SendAsync()` and return the response

#### Scenario: Request without SuppressLogging is logged normally

- **WHEN** a request does not have `AcdcRequestOptions.SuppressLogging` set (or it is `false`)
- **THEN** LoggingHandler SHALL log the request and response as specified by the Request Logging and Response Logging requirements
