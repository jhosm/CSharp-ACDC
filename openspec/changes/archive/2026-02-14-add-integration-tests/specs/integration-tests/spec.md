# Capability: Integration Tests

End-to-end integration tests for the CSharp-ACDC library validating that the complete `DelegatingHandler` pipeline, auth lifecycle, cache flows, and error classification work correctly when all components are wired together with WireMock.Net fake servers.

## ADDED Requirements

### Requirement: Complete Pipeline Flow

The integration test suite SHALL verify that a request passes through all handlers in the correct order (Logging -> Error -> Cancellation -> Auth -> Cache -> Dedup) and returns the expected response from the downstream API server. The test MUST use a fully-configured `HttpClient` built via the ACDC builder with all handlers registered through `IHttpClientFactory`.

#### Scenario: Full pipeline request completes successfully

- **WHEN** a GET request is sent through a fully-configured ACDC `HttpClient` with all handlers registered
- **AND** the `FakeApiServer` is configured to return a 200 response with body `{"status": "ok"}`
- **THEN** the response status code SHALL be 200
- **AND** the response body SHALL contain `{"status": "ok"}`
- **AND** the request SHALL have passed through all handlers in order (verified by logged entries or header tracing)

#### Scenario: Authenticated request includes Bearer token

- **WHEN** a GET request is sent through the full pipeline with a valid access token stored in `ITokenProvider`
- **THEN** the `FakeApiServer` SHALL receive the request with an `Authorization: Bearer {token}` header matching the stored access token

#### Scenario: Error response is converted to typed exception

- **WHEN** a GET request is sent through the full pipeline
- **AND** the `FakeApiServer` returns a 500 response
- **THEN** the pipeline SHALL throw an `AcdcServerException`
- **AND** the exception SHALL contain the HTTP status code 500

### Requirement: Auth Refresh Integration

The integration test suite SHALL verify proactive token refresh, reactive 401 retry, and concurrent refresh queue behavior using a WireMock.Net OAuth server (`FakeOAuthServer`). Token refresh MUST use a separate `HttpClient` (named `"acdc-auth"`) that does not pass through the ACDC handler pipeline.

#### Scenario: Proactive token refresh before expiry

- **WHEN** a request is sent through the full pipeline
- **AND** the stored access token expires within the configured refresh threshold (e.g., 60 seconds)
- **THEN** the `AuthHandler` SHALL call the `FakeOAuthServer` `/token` endpoint to refresh the token before sending the API request
- **AND** the `FakeApiServer` SHALL receive the request with the new (refreshed) access token

#### Scenario: Reactive 401 retry with fresh token

- **WHEN** a request is sent through the full pipeline
- **AND** the `FakeApiServer` is configured via `RespondWith401ThenSuccess()` to return 401 on the first call and 200 on the second
- **THEN** the `AuthHandler` SHALL call the `FakeOAuthServer` `/token` endpoint to refresh the token
- **AND** the `AuthHandler` SHALL retry the request with a cloned `HttpRequestMessage` containing the new token
- **AND** the final response SHALL have status code 200

#### Scenario: Concurrent refresh queue triggers single refresh

- **WHEN** 5 simultaneous requests are sent through the full pipeline
- **AND** the `FakeApiServer` returns 401 for all initial requests
- **THEN** the `FakeOAuthServer` `/token` endpoint SHALL be called exactly 1 time (not 5 times)
- **AND** all 5 requests SHALL be retried with the same refreshed token
- **AND** all 5 requests SHALL complete successfully

#### Scenario: Logout during active refresh

- **WHEN** a token refresh is in progress (the `FakeOAuthServer` `/token` endpoint has configurable latency)
- **AND** `AcdcAuthManager.LogoutAsync()` is called while the refresh is pending
- **THEN** the logout SHALL complete without deadlock
- **AND** subsequent requests SHALL NOT use the stale or partially-refreshed token

### Requirement: Cache ETag Integration

The integration test suite SHALL verify the full ETag/If-None-Match flow: the initial request caches the response with the ETag value, the subsequent request sends the `If-None-Match` header, and a 304 response from the server causes the cached response to be returned to the caller.

#### Scenario: ETag round-trip caches and revalidates

- **WHEN** a GET request is sent to `/api/data` through the full pipeline
- **AND** the `FakeApiServer` returns a 200 response with header `ETag: "abc123"` and body `{"data": "value"}`
- **AND** a second GET request is sent to the same URL
- **THEN** the second request to the `FakeApiServer` SHALL include the header `If-None-Match: "abc123"`
- **AND** when the `FakeApiServer` returns 304 (Not Modified)
- **THEN** the caller SHALL receive the cached response body `{"data": "value"}` with status code 200

#### Scenario: ETag miss returns fresh response

- **WHEN** a GET request is sent with a cached ETag
- **AND** the `FakeApiServer` returns 200 with a new body and new ETag `"def456"`
- **THEN** the caller SHALL receive the new response body
- **AND** the cache SHALL be updated with the new ETag and body

### Requirement: Cache SWR Integration

The integration test suite SHALL verify stale-while-revalidate behavior: when a cached response is stale, the stale response is returned immediately to the caller while a background refresh fetches the updated response.

#### Scenario: Stale response returned while revalidation happens in background

- **WHEN** a GET request is sent to `/api/data` through the full pipeline
- **AND** the cached response for `/api/data` is stale (past its freshness lifetime but within the SWR window)
- **AND** the `FakeApiServer` is configured with a 500ms latency for the revalidation request
- **THEN** the caller SHALL receive the stale cached response immediately (without waiting 500ms)
- **AND** after the background refresh completes, a subsequent GET request SHALL return the updated response

### Requirement: Mutation Invalidation Integration

The integration test suite SHALL verify that POST, PUT, and DELETE requests invalidate related cached GET responses, ensuring subsequent GET requests fetch fresh data from the server.

#### Scenario: POST invalidates cached GET

- **WHEN** a GET request to `/api/items` returns a 200 response that is cached
- **AND** a POST request is sent to `/api/items` with a new item body
- **AND** a subsequent GET request is sent to `/api/items`
- **THEN** the second GET request SHALL NOT return the previously cached response
- **AND** the `FakeApiServer` SHALL receive the second GET request (cache miss, fresh fetch)

#### Scenario: PUT invalidates cached GET for same resource

- **WHEN** a GET request to `/api/items/1` returns a cached response
- **AND** a PUT request is sent to `/api/items/1` with updated data
- **AND** a subsequent GET request is sent to `/api/items/1`
- **THEN** the second GET SHALL fetch a fresh response from the `FakeApiServer`

### Requirement: Error Classification Integration

The integration test suite SHALL verify that HTTP status codes (401, 403, 4xx, 5xx) and network errors are correctly converted to typed ACDC exceptions through the full pipeline. Each status code range MUST produce the correct exception subtype.

#### Scenario: 401 produces AcdcAuthException after failed refresh

- **WHEN** a request receives a 401 response
- **AND** the token refresh also fails (e.g., `FakeOAuthServer` returns `invalid_grant`)
- **THEN** the pipeline SHALL throw an `AcdcAuthException`
- **AND** the exception SHALL have HTTP status code 401

#### Scenario: 403 produces AcdcAuthException

- **WHEN** a request receives a 403 Forbidden response through the full pipeline
- **THEN** the pipeline SHALL throw an `AcdcAuthException`
- **AND** the exception SHALL have HTTP status code 403

#### Scenario: 429 produces AcdcClientException with RetryAfter

- **WHEN** a request receives a 429 Too Many Requests response with a `Retry-After: 60` header
- **THEN** the pipeline SHALL throw an `AcdcClientException`
- **AND** the exception SHALL have HTTP status code 429
- **AND** the exception `RetryAfter` property SHALL be 60 seconds

#### Scenario: 500 produces AcdcServerException

- **WHEN** a request receives a 500 Internal Server Error response through the full pipeline
- **THEN** the pipeline SHALL throw an `AcdcServerException`
- **AND** the exception SHALL have HTTP status code 500

#### Scenario: Network timeout produces AcdcNetworkException

- **WHEN** a request is sent with a timeout of 1 second
- **AND** the `FakeApiServer` is configured with a 5-second latency
- **THEN** the pipeline SHALL throw an `AcdcNetworkException`
- **AND** the exception `NetworkErrorType` SHALL indicate a timeout

#### Scenario: DNS failure produces AcdcNetworkException

- **WHEN** a request is sent to an unresolvable hostname
- **THEN** the pipeline SHALL throw an `AcdcNetworkException`
- **AND** the exception `NetworkErrorType` SHALL indicate a DNS or connection failure

### Requirement: Cancel All Integration

The integration test suite SHALL verify that `CancelAll()` cancels all in-flight requests and that new requests succeed after cancellation. The cancellation tracker MUST be cleaned up after cancellation so it does not leak memory or affect subsequent requests.

#### Scenario: CancelAll cancels all in-flight requests

- **WHEN** 3 requests are in flight (the `FakeApiServer` is configured with a 5-second latency)
- **AND** `CancelAll()` is invoked
- **THEN** all 3 requests SHALL throw `OperationCanceledException` or `TaskCanceledException`
- **AND** the cancellation SHALL occur within a reasonable time (not waiting for the 5-second server latency)

#### Scenario: New requests succeed after CancelAll

- **WHEN** `CancelAll()` has been invoked and all in-flight requests have been cancelled
- **AND** a new GET request is sent through the pipeline
- **AND** the `FakeApiServer` is configured to return 200
- **THEN** the new request SHALL complete successfully with status code 200

#### Scenario: Cancellation tracker is cleaned up

- **WHEN** `CancelAll()` has been invoked
- **THEN** the internal cancellation tracker SHALL NOT retain references to the cancelled requests
- **AND** repeated `CancelAll()` calls SHALL not throw exceptions

### Requirement: Builder Reusability Integration

The integration test suite SHALL verify that the same ACDC builder configuration produces independent, correctly-configured `HttpClient` instances. Each client MUST have its own handler pipeline and MUST NOT share mutable state with other clients produced from the same builder.

#### Scenario: Two clients from same builder are independent

- **WHEN** two `HttpClient` instances are created from the same ACDC builder configuration via `IHttpClientFactory`
- **AND** the first client sends a request that triggers auth token refresh
- **THEN** the second client SHALL NOT observe the first client's refreshed token (each has independent `ITokenProvider` state or appropriately shared state as configured)
- **AND** both clients SHALL have the full handler pipeline registered

#### Scenario: Builder produces working client after reconfiguration

- **WHEN** an ACDC builder is configured with auth options
- **AND** a client is created and used successfully
- **AND** a second client is created from the same `IHttpClientFactory` registration
- **THEN** the second client SHALL be fully functional with all handlers in the correct order

### Requirement: Test Server Helpers

The test suite SHALL provide reusable `FakeOAuthServer` and `FakeApiServer` helpers built on WireMock.Net with configurable responses and call tracking. These helpers MUST implement `IAsyncDisposable` for reliable cleanup in test teardown.

#### Scenario: FakeOAuthServer tracks token endpoint calls

- **WHEN** the `FakeOAuthServer` is started with a success response configured for `/token`
- **AND** 3 token refresh requests are sent to the `/token` endpoint
- **THEN** `FakeOAuthServer.TokenCallCount` SHALL be 3
- **AND** `FakeOAuthServer.CapturedTokenRequests` SHALL contain the 3 request bodies

#### Scenario: FakeApiServer supports dynamic response configuration

- **WHEN** the `FakeApiServer` is configured with `RespondWith401ThenSuccess()` for path `/api/data`
- **AND** a first request is sent to `/api/data`
- **THEN** the first response SHALL have status code 401
- **AND** a second request to `/api/data` SHALL have status code 200

#### Scenario: Fake servers dispose cleanly

- **WHEN** a test completes and `DisposeAsync()` is called on both `FakeOAuthServer` and `FakeApiServer`
- **THEN** the WireMock.Net servers SHALL be stopped and their ports released
- **AND** no exceptions SHALL be thrown during disposal
