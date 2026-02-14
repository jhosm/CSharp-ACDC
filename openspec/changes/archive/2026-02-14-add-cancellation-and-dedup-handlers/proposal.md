# Change: Add CancellationHandler, DeduplicationHandler, and Shared Utilities

## Why

Request lifecycle management requires both cancellation tracking (for bulk cancel-all operations) and deduplication (to prevent redundant identical GET requests hitting downstream services). These two small handlers are grouped because they both manage request lifecycle and share the `HttpRequestMessageExtensions` utility. This is P4 in the implementation plan, and both handlers are thin enough to implement and review as a single change.

## What Changes

### Active Request Tracker
- **`ActiveRequestTracker`** -- thread-safe tracker using `ConcurrentDictionary<CancellationTokenSource, byte>` (byte value unused, used as a concurrent set). Methods: `Track()`, `Untrack()`, `CancelAll()`, `ActiveCount` property.

### Cancellation Handler
- **`CancellationHandler : DelegatingHandler`** -- links the caller's `CancellationToken` with a new `CancellationTokenSource` tracked by `ActiveRequestTracker`. Uses the linked `CancellationTokenSource` pattern so cancellation is triggered by either the caller's token OR the tracker's bulk `CancelAll()`. Automatically unregisters the token source in a `finally` block regardless of request outcome.

### Deduplication Handler
- **`DeduplicationHandler : DelegatingHandler`** -- deduplicates concurrent identical GET and HEAD requests, sending only one downstream request.
  - **Key:** `{method}:{url}:{sorted-headers-hash}` where the headers hash is SHA256 of sorted header key-value pairs
  - **In-flight tracking:** `ConcurrentDictionary<string, Lazy<Task<HttpResponseMessage>>>` ensures exactly one request executes even under concurrent access
  - **Response cloning:** reads content to byte array, creates new `HttpResponseMessage` for each subscriber (since `HttpResponseMessage` content stream can only be read once)
  - **Per-request opt-out:** via `AcdcRequestOptions.Deduplicate` (set to `false` to skip deduplication for a specific request)
  - **Non-GET passthrough:** POST, PUT, DELETE, and PATCH requests are never deduplicated

### Request Cloning Utility
- **`HttpRequestMessageExtensions.CloneAsync()`** -- deep copy of `HttpRequestMessage` including method, URL, headers, content, and options. Required because `HttpRequestMessage` cannot be sent twice. Used by dedup response cloning and by auth retry in P5.

## Impact

- **Affected specs:** cancellation-handler (new), deduplication-handler (new)
- **Affected code:**
  - `src/CSharpAcdc/Cancellation/ActiveRequestTracker.cs` (new)
  - `src/CSharpAcdc/Handlers/CancellationHandler.cs` (new)
  - `src/CSharpAcdc/Handlers/DeduplicationHandler.cs` (new)
  - `src/CSharpAcdc/Extensions/HttpRequestMessageExtensions.cs` (new)
  - `tests/CSharpAcdc.Tests/Cancellation/ActiveRequestTrackerTests.cs` (new)
  - `tests/CSharpAcdc.Tests/Handlers/CancellationHandlerTests.cs` (new)
  - `tests/CSharpAcdc.Tests/Handlers/DeduplicationHandlerTests.cs` (new)
- **Depends on:** P2 (exceptions, `AcdcRequestOptions`)
- **Parallel with:** P3 (LoggingHandler), P5 (AuthHandler), P6 (CacheHandler)

### Files to be created

**Source files:**
- `src/CSharpAcdc/Cancellation/ActiveRequestTracker.cs`
- `src/CSharpAcdc/Handlers/CancellationHandler.cs`
- `src/CSharpAcdc/Handlers/DeduplicationHandler.cs`
- `src/CSharpAcdc/Extensions/HttpRequestMessageExtensions.cs`

**Test files:**
- `tests/CSharpAcdc.Tests/Cancellation/ActiveRequestTrackerTests.cs`
- `tests/CSharpAcdc.Tests/Handlers/CancellationHandlerTests.cs`
- `tests/CSharpAcdc.Tests/Handlers/DeduplicationHandlerTests.cs`
