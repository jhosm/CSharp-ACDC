# cancellation-handler Specification

## Purpose
TBD - created by archiving change add-cancellation-and-dedup-handlers. Update Purpose after archive.
## Requirements
### Requirement: Active Request Tracking

`ActiveRequestTracker` SHALL track active in-flight requests using a `ConcurrentDictionary<CancellationTokenSource, byte>` as a thread-safe set. It SHALL provide `Track(CancellationTokenSource)` to register a request, `Untrack(CancellationTokenSource)` to deregister a request, `CancelAll()` to cancel all tracked requests and clear the dictionary, and an `ActiveCount` property returning the current number of tracked requests.

#### Scenario: Track and untrack a single request

- **WHEN** `Track()` is called with a `CancellationTokenSource`
- **THEN** `ActiveCount` SHALL increase by 1
- **AND** when `Untrack()` is called with the same `CancellationTokenSource`
- **THEN** `ActiveCount` SHALL decrease by 1

#### Scenario: Track multiple concurrent requests

- **WHEN** `Track()` is called with three different `CancellationTokenSource` instances
- **THEN** `ActiveCount` SHALL be 3

#### Scenario: Untrack an already-untracked source is a no-op

- **WHEN** `Untrack()` is called with a `CancellationTokenSource` that is not currently tracked
- **THEN** no exception SHALL be thrown
- **AND** `ActiveCount` SHALL remain unchanged

---

### Requirement: Cancel All Active Requests

`CancelAll()` SHALL cancel every `CancellationTokenSource` currently in the tracker and then clear the tracking dictionary. After `CancelAll()` returns, `ActiveCount` SHALL be 0 and all previously tracked `CancellationTokenSource` instances SHALL have `IsCancellationRequested` set to `true`.

#### Scenario: CancelAll cancels all tracked sources

- **WHEN** three requests are tracked via `Track()`
- **AND** `CancelAll()` is called
- **THEN** each of the three `CancellationTokenSource` instances SHALL have `IsCancellationRequested == true`
- **AND** `ActiveCount` SHALL be 0

#### Scenario: CancelAll on empty tracker is a no-op

- **WHEN** `CancelAll()` is called with no tracked requests
- **THEN** no exception SHALL be thrown
- **AND** `ActiveCount` SHALL remain 0

---

### Requirement: Linked Cancellation

`CancellationHandler` SHALL create a linked `CancellationTokenSource` via `CancellationTokenSource.CreateLinkedTokenSource()` that combines the caller's `CancellationToken` with the tracker's bulk cancellation. The linked token SHALL be passed to the downstream handler via the `HttpRequestMessage`. This ensures that a request is cancelled when either the caller cancels their token OR `CancelAll()` is invoked on the tracker.

#### Scenario: Caller cancellation propagates to downstream request

- **WHEN** a request is in-flight through `CancellationHandler`
- **AND** the caller cancels their `CancellationToken`
- **THEN** the downstream handler SHALL receive a cancellation signal via the linked token

#### Scenario: Bulk CancelAll propagates to downstream request

- **WHEN** a request is in-flight through `CancellationHandler`
- **AND** `CancelAll()` is called on the `ActiveRequestTracker`
- **THEN** the downstream handler SHALL receive a cancellation signal via the linked token

---

### Requirement: Automatic Cleanup

`CancellationHandler` SHALL untrack the `CancellationTokenSource` from the `ActiveRequestTracker` and dispose the linked `CancellationTokenSource` in a `finally` block, regardless of whether the request succeeded, failed, or was cancelled. This prevents resource leaks and ensures `ActiveCount` accurately reflects in-flight requests.

#### Scenario: Cleanup on successful request

- **WHEN** a request completes successfully through `CancellationHandler`
- **THEN** the `CancellationTokenSource` SHALL be untracked from the `ActiveRequestTracker`
- **AND** the linked `CancellationTokenSource` SHALL be disposed

#### Scenario: Cleanup on failed request

- **WHEN** a request throws an exception during processing
- **THEN** the `CancellationTokenSource` SHALL still be untracked from the `ActiveRequestTracker`
- **AND** the linked `CancellationTokenSource` SHALL still be disposed

#### Scenario: Cleanup on cancelled request

- **WHEN** a request is cancelled (either by caller or by `CancelAll()`)
- **THEN** the `CancellationTokenSource` SHALL still be untracked from the `ActiveRequestTracker`
- **AND** the linked `CancellationTokenSource` SHALL still be disposed

---

### Requirement: Thread Safety

`ActiveRequestTracker` SHALL be safe for concurrent use from multiple threads. Concurrent calls to `Track()`, `Untrack()`, and `CancelAll()` from different threads SHALL not corrupt internal state, throw exceptions, or produce incorrect `ActiveCount` values.

#### Scenario: Concurrent track and untrack operations

- **WHEN** 100 concurrent tasks each call `Track()` followed by `Untrack()`
- **THEN** after all tasks complete, `ActiveCount` SHALL be 0
- **AND** no exceptions SHALL have been thrown

