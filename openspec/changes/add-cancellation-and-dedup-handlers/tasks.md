# Tasks: Add CancellationHandler, DeduplicationHandler, and Shared Utilities

## 1. Request Cloning Utility

- [ ] 1.1 Implement `HttpRequestMessageExtensions.CloneAsync()` with full deep copy of method, URL, headers, content (buffered to byte array), and `HttpRequestMessage.Options`

## 2. Active Request Tracker

- [ ] 2.1 Implement `ActiveRequestTracker` with `ConcurrentDictionary<CancellationTokenSource, byte>` backing store
- [ ] 2.2 Implement `Track()` and `Untrack()` methods
- [ ] 2.3 Implement `CancelAll()` with cancellation of all tracked sources and dictionary clearing
- [ ] 2.4 Implement `ActiveCount` property returning the current number of tracked requests

## 3. Cancellation Handler

- [ ] 3.1 Implement linked `CancellationTokenSource` pattern combining caller's `CancellationToken` with tracker's bulk cancellation
- [ ] 3.2 Implement automatic cleanup in `finally` block (untrack + dispose linked source)
- [ ] 3.3 Wire up `ActiveRequestTracker` as constructor dependency (injected via DI)

## 4. Deduplication Handler

- [ ] 4.1 Implement dedup key generation: `{method}:{url}:{SHA256-of-sorted-header-key-value-pairs}`
- [ ] 4.2 Implement `ConcurrentDictionary<string, Lazy<Task<HttpResponseMessage>>>` for in-flight request tracking
- [ ] 4.3 Implement response cloning (read content to byte array, create new `HttpResponseMessage` with copied status, headers, and content)
- [ ] 4.4 Implement per-request opt-out via `AcdcRequestOptions.Deduplicate` (skip dedup when set to `false`)
- [ ] 4.5 Implement cleanup: remove dedup entry in `finally` block after original request completes and all subscribers receive cloned response

## 5. Unit Tests

- [ ] 5.1 Test `ActiveRequestTracker` track/untrack/cancel-all basic behavior
- [ ] 5.2 Test `ActiveRequestTracker` thread safety under concurrent access (parallel Track/Untrack/CancelAll)
- [ ] 5.3 Test CancellationHandler linked token behavior (caller cancellation triggers downstream cancellation)
- [ ] 5.4 Test CancellationHandler cleanup on success and failure (untrack called in both paths)
- [ ] 5.5 Test DeduplicationHandler deduplicates identical concurrent GET requests (only one downstream call)
- [ ] 5.6 Test DeduplicationHandler passes through POST, PUT, DELETE, and PATCH requests without deduplication
- [ ] 5.7 Test DeduplicationHandler per-request opt-out (`AcdcRequestOptions.Deduplicate = false` bypasses dedup)
- [ ] 5.8 Test DeduplicationHandler response cloning correctness (each subscriber receives independent response with identical content)
- [ ] 5.9 Test DeduplicationHandler cleanup after completion (dedup entry removed from dictionary)
- [ ] 5.10 Test `HttpRequestMessageExtensions.CloneAsync()` copies all fields (method, URL, headers, content, options)
