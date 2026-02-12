# Change: Add LoggingHandler with Structured Logging and Sensitive Data Redaction

## Why

HTTP request/response logging is essential for debugging and monitoring in production. The LoggingHandler is the first handler in the pipeline (position 1 of 7) and must redact sensitive data, warn about slow requests and large payloads, and avoid recursive logging when the HTTP pipeline is used internally (e.g., for token refresh). The Dart source uses a static boolean for reentrancy prevention, which is not thread-safe; this port fixes that with `AsyncLocal<bool>`.

## What Changes

- **`LoggingHandler : DelegatingHandler`** — Structured logging via `ILogger<LoggingHandler>`
  - Logs request method, URL (redacted), headers (redacted), timing at Information level
  - Logs response status, headers (redacted), elapsed time at Information level
  - Logs exceptions at Error level with redacted request details
  - Sensitive data redaction (configurable, 16 default fields from the Dart source)
  - Slow request warnings (default 3s threshold, configurable)
  - Large payload warnings (default 1 MiB threshold, configurable)
  - `AsyncLocal<bool>` for reentrancy prevention (fixes Dart's static boolean bug for thread safety)
  - Per-request opt-out via `AcdcRequestOptions.SuppressLogging`

- **`AcdcLoggingOptions` record** — Configurable thresholds and sensitive field list via `IOptions<AcdcLoggingOptions>`

- **`SensitiveDataRedactor`** — Reusable redaction logic for headers, query parameters, and JSON body fields

## Impact

- **Affected specs:** logging-handler (new capability)
- **Affected code:**
  - `src/CSharpAcdc/Handlers/LoggingHandler.cs` (new)
  - `src/CSharpAcdc/Logging/SensitiveDataRedactor.cs` (new)
  - `src/CSharpAcdc/Configuration/AcdcLoggingOptions.cs` (new)
  - `tests/CSharpAcdc.Tests/Handlers/LoggingHandlerTests.cs` (new)
  - `tests/CSharpAcdc.Tests/Logging/SensitiveDataRedactorTests.cs` (new)
- **Depends on:** P1 (solution scaffold), P2 (exceptions and `AcdcRequestOptions`)
