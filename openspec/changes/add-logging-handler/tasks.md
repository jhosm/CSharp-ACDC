# Tasks: Add LoggingHandler with Structured Logging and Sensitive Data Redaction

## 1. Configuration

- [ ] 1.1 Create `AcdcLoggingOptions` record with `SlowRequestThreshold` (default: 3s), `LargePayloadThreshold` (default: 1 MiB), and `SensitiveFields` (default: 16 fields from Dart source)

## 2. Sensitive Data Redaction

- [ ] 2.1 Implement `SensitiveDataRedactor` with header redaction (replace values of sensitive header names with `[REDACTED]`)
- [ ] 2.2 Add query parameter redaction (replace values of sensitive query parameter names with `[REDACTED]`)
- [ ] 2.3 Add JSON body field redaction (replace values of sensitive JSON property names with `[REDACTED]`)
- [ ] 2.4 Support custom sensitive field names via `AcdcLoggingOptions.SensitiveFields`

## 3. Logging Handler

- [ ] 3.1 Implement request logging with redacted URL, method, and headers at Information level
- [ ] 3.2 Implement response logging with status code, redacted headers, and elapsed time at Information level
- [ ] 3.3 Implement slow request warning (log Warning when elapsed time exceeds `SlowRequestThreshold`)
- [ ] 3.4 Implement large payload warning (log Warning when request or response body exceeds `LargePayloadThreshold`)
- [ ] 3.5 Implement reentrancy prevention via `AsyncLocal<bool>` to skip logging for nested/internal requests
- [ ] 3.6 Implement error logging at Error level with redacted request details
- [ ] 3.7 Implement per-request opt-out via `AcdcRequestOptions.SuppressLogging`

## 4. Unit Tests

- [ ] 4.1 Test request logging output format (method, redacted URL, redacted headers)
- [ ] 4.2 Test response logging with status code and timing
- [ ] 4.3 Test sensitive header redaction (all 16 default fields: Authorization, Cookie, Set-Cookie, X-Api-Key, password, token, secret, key, credential, access_token, refresh_token, client_secret, api_key, private_key, session_id)
- [ ] 4.4 Test custom sensitive field addition via options
- [ ] 4.5 Test query parameter redaction
- [ ] 4.6 Test slow request warning at threshold boundary (just below = no warning, at/above = warning)
- [ ] 4.7 Test large payload warning at threshold boundary (just below = no warning, at/above = warning)
- [ ] 4.8 Test reentrancy prevention (nested requests skip logging)
- [ ] 4.9 Test per-request opt-out (`SuppressLogging = true` produces no log output)
- [ ] 4.10 Test error logging with redacted request details
