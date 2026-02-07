# Review: 01-architecture-and-interceptors.md

**Reviewer:** reviewer-auth (cross-review from auth/security perspective)
**Date:** 2026-02-07
**Verdict:** GOOD with corrections needed -- the document is comprehensive and well-structured, but contains several inaccuracies, missing details, and incomplete C# porting guidance.

---

## 1. Accuracy Check

### 1.1 CORRECT -- Interceptor Chain Order

The document correctly identifies the 8-interceptor chain and its exact order (Section 4.1). Verified against `acdc_client_builder.dart:610-653`:

```
1. LoggingInterceptor   (line 618)
2. ErrorInterceptor     (line 627)
3. CancellationInterceptor (line 631)
4. OfflineInterceptor   (line 633)
5. AuthInterceptor      (line 637)
6. CacheInterceptor     (line 642)
7. Custom Interceptors  (line 647)
8. DeduplicationInterceptor (line 652)
```

This matches the document exactly.

### 1.2 CORRECT -- Builder Immutability Pattern

The `_copyWith()` pattern is accurately described. The builder is indeed `const`-constructible and returns new instances on each `with*` call.

### 1.3 INACCURACY -- ErrorInterceptor Has No `onResponse` Handler

The sequence diagram in Section 4.1 shows the ErrorInterceptor participating in the response phase ("Err-->>Log: next(response)"). However, examining `error_interceptor.dart`, the `ErrorInterceptor` class only overrides `onError` -- it does **not** override `onRequest` or `onResponse`. In Dio, interceptors that don't override `onResponse` simply pass through. The diagram is technically correct in terms of data flow (responses do flow through all interceptors), but the depiction implies ErrorInterceptor actively processes responses, which it does not. The ErrorInterceptor only activates in the error phase.

**Source:** `error_interceptor.dart:19-157` -- only `onError()` is overridden.

### 1.4 INACCURACY -- ErrorInterceptor Also Has No `onRequest` Handler

Similar to above, the sequence diagram shows "Err->>Cancel: next(options)" implying active participation in request phase. The ErrorInterceptor has no `onRequest` override. It is a pure error-phase interceptor.

### 1.5 INACCURACY -- LoggingInterceptor Default Large Payload Threshold

Section 3.2 states the default large payload warning is "1MB". The actual default in the code is `1048576` bytes (which is 1 MiB). While the document says "1MB", the code comment at `logging_interceptor.dart:26` says `// 1 MB in bytes`. This is technically correct but the document should clarify this is 1 MiB (1,048,576 bytes), not 1 MB (1,000,000 bytes). Minor nit but worth noting for accuracy.

### 1.6 INACCURACY -- Default Slow Request Threshold Comment

Section 4.2.1 states the slow request threshold default is 3s. This aligns with `logging_interceptor.dart:25`: `slowRequestThreshold = const Duration(seconds: 3)`. CORRECT.

However, the `LoggingInterceptor` constructor default for `largePayloadThreshold` is `1048576` (line 26), which represents 1 MiB, **not** 100 KB as mentioned in `acdc_client_builder.dart:261` doc comment ("Defaults to 100 KB"). The builder doc comment is wrong in the Dart source itself, but the research document correctly reports the actual default from the interceptor constructor. This discrepancy in the Dart source should be noted.

### 1.7 INACCURACY -- BackoffManager Progression

Section 4.2.5 states the backoff progression is "1s -> 2s -> 4s -> 8s -> 16s -> 30s (clamped)". I could not verify this from the interceptor files read, as the `BackoffManager` implementation was not included in the interceptor directory. The `auth_interceptor.dart:90` imports `BackoffManager` from `src/auth/backoff_manager.dart`. The document's description looks plausible based on the code snippet shown, but since I did not read the actual `BackoffManager` source, I cannot fully confirm the progression or the `maxSeconds` default.

### 1.8 INACCURACY -- AuthInterceptor `_refreshQueueTimeout` Default

The document does not mention the `_refreshQueueTimeout` default value. Looking at `auth_interceptor.dart:50`: `Duration refreshQueueTimeout = const Duration(seconds: 10)`. This 10-second queue timeout for waiting requests is an important operational detail that the document omits.

### 1.9 INACCURACY -- `unawaited()` Call on Refresh Completer

The document's code snippet for `_refreshTokenWithQueue()` omits an important line from the actual source. At `auth_interceptor.dart:278`:
```dart
unawaited(_refreshCompleter!.future.catchError((_) {}));
```
This prevents unhandled exception warnings when the completer's future has an error but nobody is awaiting it. This is a subtle but important Dart-specific pattern that has implications for C# porting -- in C#, `TaskCompletionSource` faults are handled differently.

### 1.10 INACCURACY -- AuthInterceptor Retry Uses Separate `Dio` Instance

The document mentions the retry mechanism but does not detail that `AuthInterceptor` uses a separate `_retryClient` (`Dio()` instance) for retries at `auth_interceptor.dart:221-222`:
```dart
_retryClient ??= Dio();
final response = await _retryClient!.fetch<dynamic>(requestOptions);
```
This is important because the retry client has **no interceptors**, meaning the retried request bypasses the entire interceptor chain. This has significant implications for C# porting -- the retry request in C# would need to use a bare `HttpClient` without the delegating handler pipeline.

### 1.11 CORRECT -- DeduplicationInterceptor Behavior

The document accurately describes the deduplication key generation, the subscriber pattern, the secondary cancellation behavior, and the rules for which requests are deduplicated. Verified against `deduplication_interceptor.dart:80-98` and the test file.

### 1.12 CORRECT -- CacheInterceptor User Isolation

The user isolation logic (`buildCacheKeyWithUserIsolation`) is accurately described in the document. The three states (no auth / auth + user ID / auth but no user ID) are correctly identified.

---

## 2. Missing Content

### 2.1 MISSING -- `AuthRequestHelper` Utility Class

The document does not mention `auth_request_helper.dart` at all. This is a static utility class that provides:
- `injectBearerToken()` -- sets `Authorization: Bearer {token}`
- `hasManualAuthHeader()` -- checks if Authorization header already exists
- `markAsRetry()` / `isRetryRequest()` -- uses `_acdc_retry_after_refresh` extra key
- `createEmptyRequestOptions()` -- for creating exceptions without a request context

**Source:** `auth_request_helper.dart:1-52`

This is relevant for C# porting because these helpers would need to be equivalent methods on `HttpRequestMessage`.

### 2.2 MISSING -- AuthInterceptor Error Handling Differentiation

The document does not describe the nuanced error handling in `_performTokenRefresh()` at `auth_interceptor.dart:293-339`:
- `AcdcAuthException` --> clears tokens (permanent failure)
- `AcdcNetworkException` --> does NOT clear tokens (transient failure, allow retry later)
- `AcdcServerException` --> applies exponential backoff increment (server issue)

This differentiation is critical for correct behavior. Clearing tokens on network errors would be a severe bug, and the document should highlight this.

### 2.3 MISSING -- AuthInterceptor `cancelRefresh()` Method

The `AuthInterceptor` has a public `cancelRefresh()` method at `auth_interceptor.dart:402-409` that can cancel an in-progress refresh. This is not mentioned in the document and could be useful for C# porting (e.g., during logout or app shutdown).

### 2.4 MISSING -- LoggingInterceptor `printLogs` Default is `false`

The document mentions "Console printing (optional)" but doesn't specify that `printLogs` defaults to `false` (`logging_interceptor.dart:24`). This means by default, the LoggingInterceptor only outputs via the delegate, not to console. Important for understanding the default behavior.

### 2.5 MISSING -- LoggingInterceptor Default Sensitive Fields List

The document mentions "Configurable list of field names (default: password, token, secret, etc.)" but doesn't list all 16 defaults. The full list from `logging_interceptor.dart:29-46` includes:
```
password, token, secret, access_token, refresh_token, client_secret,
authorization, apikey, api_key, accesstoken, refreshtoken, pin, ssn,
creditcard, cvv, privatekey, private_key
```
This matters for C# porting to ensure parity.

### 2.6 MISSING -- LoggingInterceptor `_isLogging` is Static

The circular dependency prevention flag `_isLogging` is **static** (`logging_interceptor.dart:79`), meaning it is shared across ALL `LoggingInterceptor` instances. This is a subtle design decision. If multiple Dio clients are used concurrently, logging from one could suppress logging from another. This should be noted for C# porting.

### 2.7 MISSING -- CacheInterceptor Creates TWO `DioCacheInterceptor` Instances

Looking at `cache_interceptor.dart:48-113`, the constructor creates BOTH a `_cacheOptions` object AND a separate `_dioCacheInterceptor` with duplicated configuration. This is unusual -- the `_cacheOptions` is used for manual SWR lookups, while `_dioCacheInterceptor` handles standard cache flow. The document doesn't note this dual-instance pattern, which has implications for cache consistency and is worth investigating whether this causes any cache key mismatches.

### 2.8 MISSING -- CacheInterceptor `onResponse` Calls `_dioCacheInterceptor.onResponse` THEN Modifies Response

At `cache_interceptor.dart:332-333`, `_dioCacheInterceptor.onResponse(response, handler)` is called, and then the code continues to modify `response.extra` fields (lines 339-378). Since `handler` was already called by the inner interceptor, these modifications happen after the response has been passed through. This is a potential ordering issue and should be documented.

### 2.9 MISSING -- DeduplicationInterceptor Uses `HashMap` Not Regular `Map`

`deduplication_interceptor.dart:21`: `final _activeRequests = HashMap<String, _ActiveRequest>()`. The use of `HashMap` (from `dart:collection`) is an intentional performance choice over the default `LinkedHashMap`. For C# porting, `ConcurrentDictionary` is already the recommended approach, which is correct.

### 2.10 MISSING -- `Completer.future.ignore()` Pattern in DeduplicationInterceptor

`deduplication_interceptor.dart:43`: `completer.future.ignore()` -- this prevents unhandled async exceptions when no duplicate subscribers exist. The C# equivalent would need to handle `TaskCompletionSource` faults similarly (e.g., using `_ = task.ContinueWith(t => { }, TaskContinuationOptions.OnlyOnFaulted)`).

### 2.11 MISSING -- OfflineInterceptor Cache Key Reuse

The `OfflineInterceptor` at `offline_interceptor.dart:96` directly calls `AcdcCacheInterceptor.buildCacheKeyWithUserIsolation()` to look up cached data. This is a direct coupling between interceptors that the document doesn't mention. For C# porting, a shared cache key generation strategy should be extracted.

### 2.12 MISSING -- `NetworkInfoImpl` Defaults to Online on Startup

`network_info.dart:39`: `bool _isConnected = true`. The document mentions this briefly ("Defaults to online to avoid false positives on startup") but doesn't fully explain the implication: the very first request after app start will **never** be intercepted by OfflineInterceptor as offline, even if the device is actually offline, until the `connectivity_plus` check completes asynchronously. This race condition is important for C# porting.

---

## 3. C# Porting Gaps

### 3.1 GAP -- DelegatingHandler Pipeline is Reversed vs Dio

The document correctly maps Dio `Interceptor` to C# `DelegatingHandler`, but **critically undersells** the difference in pipeline construction:

- **Dio**: Interceptors are added to a list. Request flows forward (index 0 to N), response flows backward (N to 0).
- **C#**: DelegatingHandlers are nested (handler wraps inner handler). The outermost handler executes first for requests AND first for responses (it wraps the entire call).

This means the C# handler ordering is:
```
LoggingHandler -> ErrorHandler -> CancellationHandler -> OfflineHandler -> AuthHandler -> CacheHandler -> DeduplicationHandler -> HttpClientHandler
```
But in C#, `LoggingHandler.SendAsync()` sees both the outgoing request AND the incoming response first. The Dart model where response flows in reverse through the interceptor chain is naturally achieved by the nesting of `await base.SendAsync()` calls.

The document's C# sketch in Section 3.4 actually shows the correct nesting, but Section 12's mapping table oversimplifies by saying "onResponse = after base.SendAsync" without explaining that in C# the response also flows outward through the same nesting, which means the outermost handler (Logging) sees responses last naturally -- matching Dio's behavior.

### 3.2 GAP -- C# Auth Retry Needs Request Cloning

The document mentions this in Section 13.3 ("HttpRequestMessage cannot be sent twice") but the C# sketch in Section 4.2.5 does not show how to clone the request. This is a non-trivial operation in .NET because `HttpContent` streams may have been consumed. A concrete cloning utility should be recommended:

```csharp
private static async Task<HttpRequestMessage> CloneRequest(HttpRequestMessage request)
{
    var clone = new HttpRequestMessage(request.Method, request.RequestUri);
    if (request.Content != null)
    {
        var ms = new MemoryStream();
        await request.Content.CopyToAsync(ms);
        ms.Position = 0;
        clone.Content = new StreamContent(ms);
        foreach (var header in request.Content.Headers)
            clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }
    foreach (var header in request.Headers)
        clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
    foreach (var prop in request.Options)
        clone.Options.TryAdd(prop.Key, prop.Value);
    return clone;
}
```

### 3.3 GAP -- Missing Guidance on `HttpRequestMessage.Options` vs `extra`

The document maps `Dio.options.extra` to `HttpRequestMessage.Options` in the mapping table (Section 12), but doesn't explain how to use it. In .NET 5+, `HttpRequestMessage.Options` is an `IDictionary<string, object?>` that can carry per-request metadata (like `_acdc_retry_after_refresh` or `acdc_request_start_time`). This should be detailed more.

### 3.4 GAP -- C# Concurrent Refresh Queue Sketch Has a Race Condition

The C# sketch in Section 4.2.5 has a race condition:
```csharp
if (_activeRefresh != null)
{
    await _activeRefresh;  // What if _activeRefresh becomes null here?
    return;
}
```
Between checking `_activeRefresh != null` and awaiting it, another thread could set it to null. The correct pattern needs proper synchronization:

```csharp
private readonly SemaphoreSlim _refreshLock = new(1, 1);
private TaskCompletionSource<bool>? _refreshTcs;

private async Task RefreshTokenWithQueue(CancellationToken ct)
{
    TaskCompletionSource<bool>? existingTcs;
    await _refreshLock.WaitAsync(ct);
    try
    {
        if (_refreshTcs != null)
        {
            existingTcs = _refreshTcs;
        }
        else
        {
            _refreshTcs = new TaskCompletionSource<bool>();
            existingTcs = null;
        }
    }
    finally
    {
        _refreshLock.Release();
    }

    if (existingTcs != null)
    {
        await existingTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        return;
    }

    try
    {
        await PerformTokenRefresh(ct);
        _refreshTcs!.SetResult(true);
    }
    catch (Exception ex)
    {
        _refreshTcs!.SetException(ex);
        throw;
    }
    finally
    {
        _refreshTcs = null;
    }
}
```

### 3.5 GAP -- No Mention of `IHttpClientFactory` Integration

The document recommends using `IHttpClientFactory` (Section 3.4) but doesn't explore how ACDC's builder pattern maps to .NET's `IServiceCollection.AddHttpClient()` fluent API. This is the idiomatic way to configure `HttpClient` pipelines in .NET:

```csharp
services.AddHttpClient("acdc")
    .AddHttpMessageHandler<LoggingHandler>()
    .AddHttpMessageHandler<ErrorHandler>()
    .AddHttpMessageHandler<CancellationHandler>()
    // etc.
    .ConfigureHttpClient(client => {
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(5);
    });
```

### 3.6 GAP -- No Mention of `Polly` for Retry/Backoff

Section 4.2.5 mentions Polly in passing ("Consider using Polly for retry/backoff") but doesn't detail how it integrates. In modern .NET, `Microsoft.Extensions.Http.Resilience` (built on Polly v8) provides first-class support via `AddStandardResilienceHandler()` or custom policies. The auth retry + backoff could potentially use this instead of a custom `BackoffManager`.

### 3.7 GAP -- Wrapper Class Pattern Not Fully Explored

Section 5.3 suggests an `AcdcHttpClient` wrapper class as an alternative to extension methods, which is a good recommendation. However, it should also show how the builder would produce this wrapper:

```csharp
public class AcdcHttpClient : IDisposable
{
    public HttpClient Http { get; }
    public AcdcAuthManager Auth { get; }
    public AcdcCacheManager Cache { get; }
    public INetworkInfo Network { get; }
    public ActiveRequestTracker Tracker { get; }

    public void Dispose()
    {
        Http.Dispose();
        Network.Dispose();
        Tracker.CancelAll();
    }
}
```

---

## 4. Corrections

### 4.1 Section 4.2.1 -- LoggingInterceptor `_safeLog` Description Incomplete

The document describes circular dependency prevention but doesn't mention that the `_safeLog` method also catches **all exceptions** (using `on Object catch`) at `logging_interceptor.dart:107`, including non-Exception types. This is more aggressive error handling than typical Dart code, which usually catches `on Exception`. The C# equivalent should use `catch (Exception)` which already catches all managed exceptions.

### 4.2 Section 4.2.5 -- Error Phase Description for Auth

The sequence diagram shows:
```
Net-->>Auth: 401 Error
Auth->>Auth: Refresh token, retry
```

This is slightly misleading. In the actual implementation, the 401 flows through the interceptor chain in reverse: from Network -> DeduplicationInterceptor -> CacheInterceptor -> AuthInterceptor. The AuthInterceptor's `onError` handler catches it at that point. The diagram skips the intermediate interceptors for clarity, which is acceptable but should be noted.

### 4.3 Section 6 -- ActiveRequestTracker Missing `isTracked` Method

The document's code snippet for `ActiveRequestTracker` omits the `isTracked(CancelToken token)` method at `active_request_tracker.dart:37`. While minor, this method is used in tests and could be useful for C# porting diagnostics.

### 4.4 Section 9 -- Public API Missing `CacheOperation` Export

The document's public API table lists `AcdcCacheException` but misses that `CacheOperation` enum is also exported alongside it. From `dart_acdc.dart:233-234`:
```dart
export 'src/exceptions/acdc_cache_exception.dart'
    show AcdcCacheException, CacheOperation;
```

### 4.5 Section 9 -- Public API Missing `NetworkInfoImpl`

The `NetworkInfoImpl` class is NOT exported from the barrel file. Only `NetworkInfo` (abstract) and `NetworkStatus` (enum) are exported. This is correct for the library's encapsulation -- consumers only need the interface. But it means in C# porting, the `NetworkInfoImpl` equivalent should be internal.

---

## 5. Additional Insights from Source Code Review

### 5.1 Builder Default for `_offlineFailFast` and `_deduplicationEnabled`

From `acdc_client_builder.dart:94-95`:
```dart
_offlineFailFast = offlineFailFast ?? true,
_deduplicationEnabled = deduplicationEnabled ?? true,
```
Both default to `true`. The document correctly notes this for deduplication but should be explicit that `failFast` also defaults to `true`, meaning offline errors will immediately throw rather than attempt the request.

### 5.2 AuthInterceptor Strategy Resolution Priority

From `auth_interceptor.dart:62-74`, the strategy resolution order is:
1. Explicit `refreshStrategy` parameter (highest priority)
2. `refreshEndpointUrl` + `clientId` -> `OAuthTokenRefreshStrategy`
3. `customRefreshFn` -> `CustomTokenRefreshStrategy`
4. No strategy (no refresh, only token injection)

The document mentions strategies but doesn't specify this priority. If both `refreshEndpointUrl` and `customRefreshFn` are provided, OAuth wins. This should be documented.

### 5.3 Builder Validates Base URL But Not Other URLs

The `build()` method validates `_baseUrl` format at `acdc_client_builder.dart:476-486` but does NOT validate `_tokenRefreshEndpointUrl` or `_tokenRevocationEndpoint`. This is a potential porting consideration -- the C# builder may want stricter validation.

### 5.4 Certificate Pinning Sets Connection Timeout and Idle Timeout

When certificate pinning is configured, the builder at `acdc_client_builder.dart:514-515` sets:
```dart
..connectionTimeout = timeout
..idleTimeout = const Duration(seconds: 10)
```
The idle timeout of 10 seconds is hardcoded and not configurable. This should be noted for C# porting.

### 5.5 CacheInterceptor SWR Has Infinite Loop Prevention

At `cache_interceptor.dart:226`, the SWR refresh options include `'swr_refresh': true` to prevent the background refresh from triggering another SWR cycle:
```dart
..['swr_refresh'] = true, // Prevent infinite loop
```
And at line 189, the check `options.extra['swr_refresh'] != true` guards against this. This is an important detail for C# SWR implementation.

### 5.6 `closeAcdc()` Also Cancels All Active Requests

From `acdc_client_extensions.dart:24`: `activeRequestTracker?.cancelAll()` is called during `closeAcdc()`. This means closing the client cancels all in-flight requests, not just disposes resources. The C# `Dispose()` pattern should replicate this.

### 5.7 The `streamRequest()` Extension Uses SWR Callback Mechanism

The `streamRequest()` at `acdc_client_extensions.dart:44-94` injects an `swr_callback` function into `options.extra`, which the `CacheInterceptor` calls to pass the background refresh future back. This callback-based communication between the extension and interceptor is a creative pattern that would need equivalent implementation in C# (perhaps via a custom `HttpRequestMessage.Options` entry containing an `Action<Task>`).

---

## 6. Summary of Required Changes

| Priority | Issue | Section |
|----------|-------|---------|
| **HIGH** | Document AuthInterceptor's differentiated error handling (auth vs network vs server) | 4.2.5 |
| **HIGH** | Fix C# concurrent refresh queue race condition in sketch | 4.2.5 |
| **HIGH** | Note that AuthInterceptor retry uses a BARE Dio instance (no interceptors) | 4.2.5 |
| **HIGH** | Add `HttpRequestMessage` cloning utility for auth retry | C# porting |
| **MEDIUM** | Add `AuthRequestHelper` utility class description | New section |
| **MEDIUM** | Document `_refreshQueueTimeout` default (10s) | 4.2.5 |
| **MEDIUM** | Note `_isLogging` is static (shared across instances) | 4.2.1 |
| **MEDIUM** | Add `IHttpClientFactory` integration guidance | 3.4 |
| **MEDIUM** | Add missing `CacheOperation` to public API table | 9 |
| **MEDIUM** | Document strategy resolution priority in AuthInterceptor | 4.2.5 |
| **LOW** | Note `printLogs` defaults to `false` | 4.2.1 |
| **LOW** | List all 16 default sensitive fields | 4.2.1 |
| **LOW** | Note dual DioCacheInterceptor instance pattern | 4.2.6 |
| **LOW** | Document `closeAcdc()` cancels all active requests | 5.1 |
| **LOW** | Note `NetworkInfoImpl` startup race condition | 8 |
