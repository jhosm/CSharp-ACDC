# Tasks: Add End-to-End Integration Tests

## 1. Test Helpers
- [ ] 1.1 Implement `FakeOAuthServer` with `/token` endpoint supporting configurable responses (success, `invalid_grant`, server error) and `/revoke` endpoint
- [ ] 1.2 Implement `FakeApiServer` with dynamic request handlers, `RespondWith401ThenSuccess()` helper, ETag/304 support, and configurable latency
- [ ] 1.3 Add call tracking for assertion support -- expose call count and captured request bodies on both fake servers

## 2. Complete Pipeline Tests
- [ ] 2.1 Test full request flow through all handlers in correct order (Logging -> Error -> Cancellation -> Auth -> Cache -> Dedup) and verify expected response
- [ ] 2.2 Test authenticated request with valid token -- verify `Authorization: Bearer` header reaches the API server
- [ ] 2.3 Test error conversion through pipeline -- verify HTTP status codes produce correctly-typed ACDC exceptions

## 3. Auth Lifecycle Tests
- [ ] 3.1 Test proactive refresh before expiry -- verify token is refreshed when remaining lifetime falls below threshold
- [ ] 3.2 Test reactive 401 retry -- verify request is retried with fresh token after receiving 401 from API server
- [ ] 3.3 Test concurrent refresh queue -- send N simultaneous requests that all receive 401, verify only 1 token refresh call is made to the OAuth server
- [ ] 3.4 Test logout during active refresh -- verify graceful handling when logout is triggered while a token refresh is in progress

## 4. Cache Integration Tests
- [ ] 4.1 Test ETag/If-None-Match round-trip -- initial request caches response with ETag, subsequent request sends `If-None-Match`, server returns 304, client returns cached response
- [ ] 4.2 Test SWR with slow downstream -- verify stale response returned immediately while background refresh completes
- [ ] 4.3 Test mutation invalidation -- verify POST/PUT/DELETE requests invalidate related cached GET responses
- [ ] 4.4 Test user isolation with different tokens -- verify cached responses are scoped per-user identity extracted from JWT

## 5. Other Integration Tests
- [ ] 5.1 Test builder reusability -- create multiple `HttpClient` instances from the same builder and verify they are independent (do not share handler state)
- [ ] 5.2 Test cancel-all with recovery -- verify `CancelAll()` cancels all in-flight requests and that new requests succeed afterward
- [ ] 5.3 Test error classification for all status code ranges -- 401 -> `AcdcAuthException`, 403 -> `AcdcAuthException`, 4xx -> `AcdcClientException`, 5xx -> `AcdcServerException`
- [ ] 5.4 Test timeout through full pipeline -- verify request timeout produces `AcdcNetworkException` with correct `NetworkErrorType`
