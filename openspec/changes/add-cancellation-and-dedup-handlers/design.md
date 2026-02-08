# Design: Add CancellationHandler, DeduplicationHandler, and Shared Utilities

## Context

Dart is single-threaded; C# server handles concurrent requests. Both the CancellationHandler and DeduplicationHandler manage request lifecycle and must be thread-safe. The `DelegatingHandler` instances are pooled by `IHttpClientFactory` (2-minute default lifetime), so they must not store per-request state in instance fields -- per-request state goes in `HttpRequestMessage.Options` or local variables.

The Dart source has a `CancellationInterceptor` that tracks active requests for bulk cancel-all, and a `DeduplicationInterceptor` that prevents duplicate in-flight GET requests. Both are thin handlers (~50-80 lines each) that share a need for request cloning utilities.

## Goals

- **Thread-safe cancellation tracking:** Enable bulk cancellation of all active requests via `ActiveRequestTracker.CancelAll()`, while respecting each caller's individual `CancellationToken`.
- **Efficient GET deduplication:** When multiple concurrent callers request the same GET endpoint with the same headers, send only one downstream HTTP request and clone the response for all subscribers.
- **Shared request cloning utility:** Provide `HttpRequestMessageExtensions.CloneAsync()` for deep-copying `HttpRequestMessage`, since .NET does not allow sending the same `HttpRequestMessage` instance twice. This utility is also needed by the AuthHandler (P5) for retry after token refresh.

## Non-Goals

- No deduplication of POST, PUT, DELETE, or PATCH requests. Only GET and HEAD are deduplicated.
- No distributed deduplication across multiple server instances. Dedup is in-memory, per-process only.
- No configurable dedup key strategy. The key format is fixed as `{method}:{url}:{sorted-headers-hash}`.
- No TTL-based expiry for dedup entries. Entries are removed immediately after the original request completes.

## Decisions

### 1. `ConcurrentDictionary<CancellationTokenSource, byte>` for Active Request Tracking

**Decision:** Use `ConcurrentDictionary<CancellationTokenSource, byte>` as a concurrent hash set. The `byte` value is unused (always `0`).

**Why:** .NET does not have a built-in `ConcurrentHashSet<T>`. Using `ConcurrentDictionary` with a dummy value is the standard pattern. `ConcurrentBag<T>` does not support efficient removal. `HashSet<T>` with a lock would work but is less granular.

**Alternatives considered:**
- `ConcurrentBag<T>`: Rejected -- no `Remove()` method; would require iteration to find and remove a specific item.
- `HashSet<T>` + `lock`: Rejected -- coarser locking would serialize all track/untrack operations unnecessarily.

### 2. Linked CancellationTokenSource Pattern

**Decision:** `CancellationHandler` creates a `CancellationTokenSource.CreateLinkedTokenSource(callerToken)` and passes the linked token downstream. The tracker can cancel the linked source via `CancelAll()`, and the caller can cancel via their own token. Both trigger cancellation of the downstream request.

**Why:** This is the standard .NET pattern for combining multiple cancellation signals. It ensures that:
- Individual request cancellation works as expected (caller cancels their token).
- Bulk cancellation works (tracker calls `CancelAll()` which cancels all tracked sources).
- The linked source is disposed in the `finally` block to avoid resource leaks.

### 3. Dedup Key Includes Sorted Headers Hash

**Decision:** The deduplication key is `{method}:{url}:{SHA256-of-sorted-header-key-value-pairs}`. Headers are sorted by key name, then values are concatenated, and the result is hashed with SHA256.

**Why:** Different requests to the same URL may have different `Authorization` headers (different users), different `Accept` headers (different content types), or different custom headers. Without including headers in the key, requests from different users could be incorrectly deduplicated, returning one user's data to another.

**Trade-off:** Sorting and hashing headers adds CPU overhead per request. This is negligible compared to the network round-trip saved by deduplication.

### 4. `Lazy<Task<HttpResponseMessage>>` for Dedup In-Flight Tracking

**Decision:** Use `ConcurrentDictionary<string, Lazy<Task<HttpResponseMessage>>>` where the `Lazy<T>` wraps the async operation. `GetOrAdd()` with a `Lazy` factory ensures exactly one request executes even when multiple threads call `GetOrAdd()` simultaneously.

**Why:** `ConcurrentDictionary.GetOrAdd()` may invoke the factory multiple times under contention, but `Lazy<T>` ensures the inner delegate executes at most once. This is the standard double-checked pattern for concurrent deduplication in .NET.

**Alternatives considered:**
- `GetOrAdd()` without `Lazy`: Rejected -- factory can execute multiple times under contention, defeating deduplication.
- `SemaphoreSlim` per key: Rejected -- more complex, requires managing semaphore lifecycle.

### 5. Response Cloning via Byte Array Buffering

**Decision:** Clone a response by reading the content into a byte array, then creating a new `HttpResponseMessage` with a new `ByteArrayContent` for each subscriber. Status code, reason phrase, headers, and content headers are copied.

**Why:** `HttpResponseMessage.Content` is a stream that can only be read once. To serve the same response to N subscribers, we must buffer the content and create independent copies. Byte array is the simplest and most predictable approach.

**Trade-off:** Large response bodies are buffered in memory and cloned N times for N subscribers. For typical GET responses this is acceptable. If response sizes become a concern, a size limit option can be added later.

### 6. `HttpRequestMessageExtensions.CloneAsync()` Deep Copy

**Decision:** `CloneAsync()` creates a new `HttpRequestMessage` and copies: HTTP method, `RequestUri`, all headers (request headers and content headers), content (buffered to byte array then wrapped in `ByteArrayContent`), and all entries from `HttpRequestMessage.Options`.

**Why:** .NET does not allow sending the same `HttpRequestMessage` instance twice (throws `InvalidOperationException`). Any handler that needs to retry (CancellationHandler cleanup is not a retry, but AuthHandler in P5 needs it for token refresh retry, and DeduplicationHandler needs to clone responses). Having a shared utility avoids duplicating this logic.

## Risks / Trade-offs

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Memory leak if tracked `CancellationTokenSource` not cleaned up | Low | Medium | `Untrack()` is called in `finally` block; linked source is disposed in `finally` |
| Dedup map grows unbounded under sustained load | Low | Low | Entries are removed in `finally` block immediately after the original request completes; map only holds in-flight requests |
| Large response bodies cloned N times for N subscribers | Low | Medium | Acceptable for typical GET responses; add configurable max response size for dedup if needed later |
| SHA256 hash collision causes incorrect dedup | Negligible | High | SHA256 collision probability is astronomically low (~1 in 2^128); not a practical concern |
| `Lazy<Task>` exception caching -- if the first request fails, all subscribers get the same exception | Medium | Low | This is correct behavior: the downstream service returned an error, all identical requests should see the same error. The entry is cleaned up so the next wave of requests retries. |

## Open Questions

None. Both handlers are straightforward ports of the Dart source with well-established .NET concurrency patterns.
