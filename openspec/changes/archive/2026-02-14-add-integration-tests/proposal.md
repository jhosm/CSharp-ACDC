# Change: Add End-to-End Integration Tests

## Why

Unit tests (in P2-P6) test individual components in isolation with mocked dependencies. Integration tests are needed to validate that the complete pipeline works end-to-end: handlers cooperate correctly in the proper order (Logging -> Error -> Cancellation -> Auth -> Cache -> Dedup), auth refresh triggers under realistic conditions with a fake OAuth server, cache/ETag flows work through the full pipeline including stale-while-revalidate and mutation invalidation, and error conversion happens at the right layer. Without integration tests, subtle interaction bugs between handlers -- such as auth retry interfering with deduplication, or error conversion swallowing cache headers -- would go undetected until production.

## What Changes

### Test Server Helpers
- **`FakeOAuthServer`** (WireMock.Net) -- `/token` endpoint with configurable responses (success with valid tokens, `invalid_grant` error, server error), `/revoke` endpoint, call tracking for assertions (number of calls, request bodies received)
- **`FakeApiServer`** (WireMock.Net) -- dynamic request handlers that can be configured per-test, `RespondWith401ThenSuccess()` helper for testing reactive auth refresh, ETag/304 response support, configurable latency for timeout tests

### Test Suites
- **Complete client integration** -- full pipeline request flow through all handlers, authenticated calls with token injection, error conversion from HTTP status codes to typed ACDC exceptions
- **Auth lifecycle** -- proactive refresh before expiry, reactive 401 retry with request cloning, concurrent refresh queue (single server call for N simultaneous requests), logout during active refresh
- **Cache integration** -- ETag/If-None-Match round-trip, stale-while-revalidate with slow downstream, mutation invalidation (POST/PUT/DELETE clearing cached GETs), user isolation with different JWT tokens
- **Builder reusability** -- independent `HttpClient` instances from the same builder configuration, verifying they do not share handler state
- **Cancel all** -- bulk cancellation of in-flight requests, tracker cleanup, post-cancel recovery (new requests succeed after cancellation)
- **Error classification** -- all status code ranges (401, 403, 4xx, 5xx), network errors, timeouts through the full pipeline producing correctly-typed ACDC exceptions

## Impact

- **Affected specs:** integration-tests (new)
- **Depends on:** P7 (builder and DI -- needs complete pipeline to test end-to-end)
- **Parallel with:** P10, P11

### Files to be created

**Test helpers:**
- `tests/CSharpAcdc.IntegrationTests/Helpers/FakeOAuthServer.cs`
- `tests/CSharpAcdc.IntegrationTests/Helpers/FakeApiServer.cs`

**Test suites:**
- `tests/CSharpAcdc.IntegrationTests/CompleteClientIntegrationTests.cs`
- `tests/CSharpAcdc.IntegrationTests/AuthLifecycleTests.cs`
- `tests/CSharpAcdc.IntegrationTests/CacheIntegrationTests.cs`
- `tests/CSharpAcdc.IntegrationTests/BuilderReusabilityTests.cs`
- `tests/CSharpAcdc.IntegrationTests/CancelAllTests.cs`
