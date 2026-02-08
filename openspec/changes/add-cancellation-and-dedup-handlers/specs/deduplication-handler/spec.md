# Capability: Deduplication Handler

## ADDED Requirements

### Requirement: GET and HEAD Deduplication

`DeduplicationHandler` SHALL deduplicate concurrent identical GET and HEAD requests. When multiple concurrent requests have the same deduplication key, only one downstream HTTP request SHALL be sent. All other concurrent requests with the same key SHALL receive a cloned copy of the response from the single downstream request.

#### Scenario: Two identical concurrent GET requests produce one downstream call

- **WHEN** two concurrent GET requests with the same URL and headers are sent through `DeduplicationHandler`
- **THEN** only one downstream HTTP request SHALL be sent
- **AND** both callers SHALL receive a response with identical status code and content

#### Scenario: Two identical concurrent HEAD requests produce one downstream call

- **WHEN** two concurrent HEAD requests with the same URL and headers are sent through `DeduplicationHandler`
- **THEN** only one downstream HTTP request SHALL be sent
- **AND** both callers SHALL receive a response with identical status code and headers

#### Scenario: Sequential identical GET requests are not deduplicated

- **WHEN** a GET request completes and then another identical GET request is sent
- **THEN** two separate downstream HTTP requests SHALL be sent (dedup only applies to concurrent in-flight requests)

---

### Requirement: Deduplication Key

The deduplication key SHALL be composed of `{method}:{url}:{sorted-headers-hash}` where the headers hash is the SHA256 hash of the sorted header key-value pairs. Sorting SHALL be by header name (ordinal, case-insensitive). This ensures that requests with different headers (e.g., different `Authorization` tokens) are NOT incorrectly deduplicated.

#### Scenario: Same URL but different Authorization headers produce different keys

- **WHEN** two concurrent GET requests target the same URL but have different `Authorization` headers
- **THEN** they SHALL have different deduplication keys
- **AND** two separate downstream HTTP requests SHALL be sent

#### Scenario: Same URL and same headers produce the same key

- **WHEN** two concurrent GET requests target the same URL with identical headers
- **THEN** they SHALL have the same deduplication key
- **AND** only one downstream HTTP request SHALL be sent

---

### Requirement: Response Cloning

`DeduplicationHandler` SHALL clone the response for each subscriber since `HttpResponseMessage` content can only be read once. Each subscriber SHALL receive an independent `HttpResponseMessage` instance with the same status code, reason phrase, response headers, content headers, and content body. Modifying one subscriber's response SHALL NOT affect other subscribers' responses.

#### Scenario: Each subscriber receives independent response content

- **WHEN** three concurrent identical GET requests are deduplicated
- **THEN** each of the three callers SHALL receive an independent `HttpResponseMessage`
- **AND** reading the content of one response SHALL NOT affect the content of the other responses

#### Scenario: Cloned response preserves status code and headers

- **WHEN** a deduplicated response is cloned for a subscriber
- **THEN** the cloned response SHALL have the same `StatusCode`, `ReasonPhrase`, response headers, and content headers as the original

---

### Requirement: Per-Request Opt-Out

`DeduplicationHandler` SHALL skip deduplication when `AcdcRequestOptions.Deduplicate` is set to `false` on the `HttpRequestMessage.Options`. When deduplication is skipped, the request SHALL be sent directly to the downstream handler without checking or updating the in-flight tracking dictionary.

#### Scenario: Request with Deduplicate=false bypasses dedup

- **WHEN** a GET request has `AcdcRequestOptions.Deduplicate` set to `false`
- **AND** another identical GET request (without opt-out) is in-flight
- **THEN** the opted-out request SHALL be sent as a separate downstream request
- **AND** it SHALL NOT participate in deduplication (neither as a source nor as a subscriber)

#### Scenario: Default behavior is to deduplicate

- **WHEN** a GET request does not set `AcdcRequestOptions.Deduplicate`
- **THEN** it SHALL participate in deduplication (deduplication is enabled by default)

---

### Requirement: Non-GET Passthrough

`DeduplicationHandler` SHALL pass through POST, PUT, DELETE, and PATCH requests without deduplication. Only GET and HEAD requests SHALL be eligible for deduplication. Non-GET/HEAD requests SHALL be forwarded directly to the downstream handler.

#### Scenario: POST request is not deduplicated

- **WHEN** two concurrent identical POST requests are sent through `DeduplicationHandler`
- **THEN** both POST requests SHALL be sent as separate downstream HTTP requests

#### Scenario: PUT request is not deduplicated

- **WHEN** a PUT request is sent through `DeduplicationHandler`
- **THEN** it SHALL be forwarded directly to the downstream handler without deduplication logic

#### Scenario: DELETE request is not deduplicated

- **WHEN** a DELETE request is sent through `DeduplicationHandler`
- **THEN** it SHALL be forwarded directly to the downstream handler without deduplication logic

---

### Requirement: Deduplication Cleanup

`DeduplicationHandler` SHALL remove the dedup entry from the in-flight tracking dictionary after the original request completes (whether successfully or with an error) and all subscribers have received their cloned response. This cleanup SHALL occur in a `finally` block to prevent the dictionary from growing unbounded.

#### Scenario: Entry removed after successful request

- **WHEN** a deduplicated GET request completes successfully
- **THEN** the dedup entry SHALL be removed from the in-flight dictionary
- **AND** a subsequent identical GET request SHALL result in a new downstream request

#### Scenario: Entry removed after failed request

- **WHEN** a deduplicated GET request fails with an exception
- **THEN** the dedup entry SHALL still be removed from the in-flight dictionary
- **AND** all subscribers SHALL receive the same exception

---

### Requirement: Request Cloning Utility

`HttpRequestMessageExtensions.CloneAsync()` SHALL create a deep copy of an `HttpRequestMessage` including the HTTP method, `RequestUri`, all request headers, content (buffered to a byte array and wrapped in new `ByteArrayContent`), content headers, and all entries from `HttpRequestMessage.Options`. The original `HttpRequestMessage` SHALL NOT be modified by the clone operation.

#### Scenario: Clone preserves all request fields

- **WHEN** `CloneAsync()` is called on an `HttpRequestMessage` with method `POST`, a URL, custom headers, content, and options
- **THEN** the cloned message SHALL have the same method, URL, headers, content bytes, and options
- **AND** the cloned message SHALL be a separate instance (not the same object reference)

#### Scenario: Clone produces a sendable request

- **WHEN** `CloneAsync()` is called on an `HttpRequestMessage` that has already been sent
- **THEN** the cloned message SHALL be sendable via `HttpClient` without throwing `InvalidOperationException`

#### Scenario: Clone of request with no content

- **WHEN** `CloneAsync()` is called on a GET `HttpRequestMessage` with no content
- **THEN** the cloned message SHALL have `Content` set to `null`
- **AND** all other fields SHALL be copied correctly
