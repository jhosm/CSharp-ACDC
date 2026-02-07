# Review: 03-caching-and-offline.md

**Reviewer**: reviewer-tests
**Date**: 2026-02-07
**Verdict**: GOOD -- thorough and well-structured, with some corrections and additions needed.

---

## 1. Accuracy Check

### 1.1 Two-Tier Cache Architecture -- ACCURATE

The document correctly describes:
- L1 = `MemCacheStore`, L2 = `EncryptedCacheStore` (wrapping `FileCacheStore`).
- Read path: memory-first, promote on L2 hit (`two_tier_cache_store.dart:107-130`).
- Write path: `memoryStore.set()` awaited, `persistentStore.set()` fire-and-forget via `unawaited()` (`two_tier_cache_store.dart:163-173`).
- Graceful degradation with `.catchError((_) => null)`.

The code snippets are exact copies from the source. No inaccuracies found.

### 1.2 CacheStoreFactory -- ACCURATE

The platform decision table (Web vs Native, inMemory true/false) is correct per `cache_store_factory.dart:25-57`. The code snippet matches.

### 1.3 CacheConfig -- ACCURATE

All 11 parameters, types, and defaults match `cache_config.dart:22-35` exactly. The document correctly notes the `const` constructor.

### 1.4 HTTP Cache Semantics -- MOSTLY ACCURATE, ONE ISSUE

**Issue (Minor)**: The document states at Section 3.1:

> ```dart
> maxStale: (config.staleIfError || config.staleWhileRevalidate)
>     ? const Duration(days: 7)
>     : null,
> ```

This is the code from the *first* `CacheOptions` block (`cache_interceptor.dart:54-57`). However, the `_dioCacheInterceptor` is actually constructed with a *second* `CacheOptions` block (`cache_interceptor.dart:83-113`) that uses a **different** `maxStale` condition:

```dart
// cache_interceptor.dart:88
maxStale: config.staleIfError ? const Duration(days: 7) : null,
```

The second block does NOT include `staleWhileRevalidate` in the `maxStale` condition. Since `_dioCacheInterceptor` is the one that actually handles non-SWR requests via `onRequest`/`onResponse`/`onError` delegation, the document slightly overstates the maxStale behavior for SWR-only scenarios. In practice, this is likely a minor bug or inconsistency in the Dart source itself (the first `_cacheOptions` is only used for key building and SWR cache lookups, not for the delegated interceptor).

### 1.5 ETag Support -- ACCURATE

The 304 Not Modified handling code snippet at Section 3.2 matches `cache_interceptor.dart:427-450`. The test flow described (store ETag -> send If-None-Match -> resolve 304 with cached content) matches `etag_cache_test.dart`.

### 1.6 Response Metadata Table -- ACCURATE

All four metadata entries (`X-ACDC-From-Cache`, `acdc_source`, `fromOfflineCache`, `from_cache`) are correct and documented with proper values.

### 1.7 JWT/UserIdExtractor -- ACCURATE

Claim priority order (sub > user_id > uid), fallback to JWT on custom provider failure, `UserIdResult` structure -- all match the source code at `jwt_utils.dart:27-69` and `user_id_extractor.dart:43-77`.

### 1.8 Cache Key Generation -- ACCURATE

The three-state decision (shared/isolated/empty) and the `buildCacheKeyWithUserIsolation` code match `cache_interceptor.dart:141-176`.

### 1.9 Encrypted Cache Store -- ACCURATE

AES-256-GCM, 12-byte IV, serialization format, key management via `FlutterSecureStorage`, lazy initialization, decryption failure handling -- all correct per `encrypted_cache_store.dart`.

### 1.10 Offline Interceptor -- ACCURATE

Flow diagram, fail-fast behavior, GET/HEAD-only restriction, `forceNetwork` bypass, `AcdcNetworkException` wrapping -- all match `offline_interceptor.dart:42-86` and the tests in `offline_interceptor_test.dart`.

### 1.11 SWR Pattern -- ACCURATE

The SWR flow (check cache -> resolve stale -> background refresh -> prevent loop via `swr_refresh` marker) is correctly described per `cache_interceptor.dart:187-247`.

---

## 2. Missing Content

### 2.1 MISSING: Duplicate `CacheOptions` Construction

The `AcdcCacheInterceptor` constructor builds **two** `CacheOptions` objects:
1. `_cacheOptions` (line 48-80) -- used for SWR cache lookups and key building.
2. A second one passed to `DioCacheInterceptor` (line 82-113) -- used for all standard request/response/error handling.

These two have subtly different `maxStale` conditions (see Section 1.4 above). The document does not mention this duplication. This is important for the C# port because it reveals that the SWR logic and the standard cache logic use different policy configurations.

### 2.2 MISSING: `_CacheAwareRequestHandler` Details

The document briefly mentions `_CacheAwareErrorHandler` (Section 6.3) but does not describe `_CacheAwareRequestHandler` (`cache_interceptor.dart:483-539`). This handler intercepts cache hits from `dio_cache_interceptor.onRequest()` and:
- Marks cache hit responses with `from_cache = true`, `acdc_source = 'cache'`, and `X-ACDC-From-Cache: true` (line 514-516).
- Logs cache misses (line 495-504) and cache hits (line 518-527).

For the C# port, this pattern translates to a `DelegatingHandler` or middleware that decorates responses with provenance metadata.

### 2.3 MISSING: `_CacheAwareErrorHandler` Network Error Enhancement

The document shows the `resolve()` method of `_CacheAwareErrorHandler` (serving stale cache) but omits the `next()` method (`cache_interceptor.dart:555-572`), which detects network errors (connection timeout, send timeout, receive timeout, connection error) and enhances them into `AcdcNetworkException.fromDioException()`. This is a significant behavior: the cache interceptor itself produces typed network exceptions when `dio_cache_interceptor` cannot serve from cache during errors.

### 2.4 MISSING: `hitCacheOnErrorCodes` Configuration

The document mentions `hitCacheOnErrorCodes: config.staleIfError ? [401, 403] : []` in the code snippet (Section 3.1) but does not discuss its implications. When `staleIfError` is true, receiving a 401 or 403 response will trigger serving stale cache. This is a notable behavior -- it means cache is served on auth failures, which could mask auth issues. The C# port should consider whether this is desired.

### 2.5 MISSING: `onResponse` Logic for `acdc_source` Assignment

The document does not describe the logic in `_proceedWithResponse()` (`cache_interceptor.dart:325-378`) that determines the `acdc_source` value for responses. Specifically:
- If `from_cache` or `extraCacheKey` is present -> `acdc_source = 'cache'`
- If `swr_refresh` was true -> `acdc_source = 'network_fresh'`
- Otherwise -> `acdc_source = 'network'`

This is important for the C# port to replicate the full provenance metadata system.

### 2.6 MISSING: SWR `swr_callback` Mechanism

The SWR code includes a `swr_callback` mechanism (`cache_interceptor.dart:234-239`) that allows callers to capture the background refresh `Future`:

```dart
final swrCallback = options.extra['swr_callback'] as void Function(Future<dynamic>)?;
if (swrCallback != null) {
  swrCallback(refreshFuture);
}
```

This is used in testing (`swr_offline_test.dart`, `cache_interceptor_test.dart:789`) and could be useful for streaming/reactive scenarios. The C# port equivalent would be returning a `Task` or using an event/callback for the background refresh completion.

### 2.7 MISSING: `NetworkInfo` Implementation Details

The document mentions `NetworkInfo` as a service but does not describe its implementation in `network_info.dart`. Key details:
- Uses `connectivity_plus` package.
- Defaults to `isConnected = true` on startup to avoid false positives.
- Uses `StreamController.broadcast()` for `onStatusChange` events.
- Checks `List<ConnectivityResult>` where any non-`none` result means connected.

For C#, the equivalent would be `System.Net.NetworkInformation.NetworkInterface` or `Xamarin.Essentials.Connectivity` (MAUI). On server-side, this concept doesn't apply.

### 2.8 MISSING: `EncryptedCacheStore.maxSize` Not Enforced

The document states `maxSize` as "Maximum disk cache size in bytes" but `encrypted_cache_store.dart:53` has a comment: "Maximum cache size (not strictly enforced by FileCacheStore wrapper currently)." The encrypted store accepts `maxSize` but does not implement size-based eviction. The test file also notes at line 92-93: "LRU tests removed as FileCacheStore wrapper does not currently implement strict size-based eviction." This is an important gap for the C# port to be aware of.

### 2.9 MISSING: `TwoTierCacheStore.getFromPath()` Merge/Dedup Logic

The `getFromPath()` method (`two_tier_cache_store.dart:132-160`) merges results from both tiers and deduplicates by key, preferring the memory tier's version (since it's iterated first). This deduplication behavior is tested in `two_tier_cache_store_test.dart:112-129` but not documented.

### 2.10 MISSING: `EncryptedCacheStore` has its own `pathExists()` implementation

The `EncryptedCacheStore` implements `pathExists()` directly (`encrypted_cache_store.dart:264-283`) rather than delegating to `FileCacheStore`. This is a synchronous check that regex-matches the URL and optionally validates query parameters. Both `TwoTierCacheStore` and `EncryptedCacheStore` implement this identically. Worth noting for the C# port that this is a non-async URL matching utility, not actual cache lookup.

---

## 3. C# Porting Gaps

### 3.1 FusionCache Recommendation -- SOUND BUT INCOMPLETE

The FusionCache recommendation is solid. However:

- **Missing**: FusionCache's `FactoryHardTimeout` and `FactorySoftTimeout` options, which are the real SWR equivalents. The document shows `AllowTimedOutFactoryBackgroundCompletion` which is correct, but `FactorySoftTimeout` controls when to start serving stale while the factory continues in the background.

- **Missing**: FusionCache's **backplane** support for multi-instance cache invalidation. If the C# server runs multiple instances, cache invalidation on one instance must propagate. FusionCache supports this via Redis backplane.

- **Missing**: The document shows `SizeLimit = 5 * 1024 * 1024` for `MemoryCacheOptions`, but `IMemoryCache`'s `SizeLimit` requires each entry to declare its size via `SetSize()`. FusionCache handles this automatically, but the code example could be misleading.

### 3.2 DelegatingHandler vs Middleware -- NEEDS CLARIFICATION

The document mentions both `DelegatingHandler` and middleware for cache implementation but doesn't clarify when to use which:
- **`DelegatingHandler`**: For outgoing HTTP requests (HttpClient pipeline). This is the correct analog to Dio interceptors.
- **Middleware**: For incoming HTTP requests (ASP.NET Core pipeline). This is for caching responses your server sends.

The Dart ACDC is a *client-side* library, so the direct C# equivalent is `DelegatingHandler` in the `HttpClient` pipeline, not ASP.NET Core middleware.

### 3.3 Missing: `IHttpClientFactory` Integration

For C# server-side where `HttpClient` is used to call downstream APIs, the cache `DelegatingHandler` should integrate with `IHttpClientFactory`:

```csharp
services.AddHttpClient("CachedClient")
    .AddHttpMessageHandler<CachingHandler>();
```

This is not mentioned in the document.

### 3.4 Cache Key Builder -- MINOR ISSUE

The C# `CacheKeyBuilder.Build()` example (Section 10.4) uses `request.Method` and `request.RequestUri` but the Dart implementation's `defaultCacheKeyBuilder` also considers headers and body. The C# example is simplified, which is fine, but the document should note that the Dart key builder includes headers.

### 3.5 Polly for Circuit Breaker -- GOOD BUT NEEDS CONTEXT

Polly recommendation for replacing `OfflineInterceptor` is correct. However, the document should note that Polly v8+ (via `Microsoft.Extensions.Http.Resilience`) integrates directly with `IHttpClientFactory` and provides:
- Circuit breaker (replaces offline detection)
- Retry with exponential backoff
- Timeout handling

All three are relevant to replacing both `OfflineInterceptor` and the network error handling in `_CacheAwareErrorHandler`.

### 3.6 Missing: MAUI SecureStorage for Client Scenario

For .NET MAUI client (Section 10.3), the document mentions DPAPI and Keychain but doesn't reference `SecureStorage` from `Microsoft.Maui.Storage`, which is the direct equivalent of `FlutterSecureStorage`.

---

## 4. Corrections

### 4.1 Section 3.1 `maxStale` Code Snippet

The code shown:
```dart
maxStale: (config.staleIfError || config.staleWhileRevalidate)
    ? const Duration(days: 7)
    : null,
```

This is from the first `CacheOptions` block (`cache_interceptor.dart:54-57`), but the `DioCacheInterceptor` that handles actual request delegation uses the second block (`cache_interceptor.dart:88`):
```dart
maxStale: config.staleIfError ? const Duration(days: 7) : null,
```

The document should clarify which CacheOptions block controls which behavior, or at minimum note the discrepancy.

### 4.2 Section 6.2 SWR Simplified Code

The document's simplified SWR code at Section 6.2 shows:
```dart
Future.microtask(() => onRefresh!(refreshOptions)).catchError((e) {}).ignore();
```

But the actual code (`cache_interceptor.dart:230-244`) first captures the `refreshFuture`, passes it to `swr_callback` if present, and *then* wraps in `Future.microtask`. The simplified version omits the `swr_callback` mechanism and the direct `onRefresh!()` invocation before the microtask wrapper. Not a factual error (the document says "simplified"), but the ordering matters for understanding the async flow.

### 4.3 Section 8 AcdcCacheManager -- MINOR OMISSION

The document says "It returns a no-op manager when cache is disabled." Looking at `acdc_cache_manager.dart:25-29`, the `clearCache()` method checks `if (_cacheInterceptor != null)` before delegating. When cache is disabled, the manager is constructed with a `null` interceptor, making it effectively a no-op. This is correct but could be stated more precisely: the manager is not "no-op" per se, but its operations are guarded by null checks.

### 4.4 Section 9 Test Coverage -- ONE MISSING TEST FILE

The test coverage table at Section 9.1 lists 7 test files but does not include `cache_invalidation_test.mocks.dart` (generated Mockito mocks for `FlutterSecureStorage`). This is a generated file and typically wouldn't be documented, so this is a very minor point. More importantly, the table does list `user_isolation_test.dart` which is in `test/cache/`, not `test/interceptors/`. The file organization is correctly implied.

---

## 5. Additions (New Insights from Source Review)

### 5.1 The `CachePolicy.request` is Used for Both SWR and Non-SWR

At `cache_interceptor.dart:51-53`, the policy is:
```dart
policy: config.staleWhileRevalidate
    ? CachePolicy.request
    : CachePolicy.request,
```

This is a ternary that returns `CachePolicy.request` in both branches -- effectively dead code. The comment says "Always use request policy if SWR is enabled, as we handle SWR manually." This means SWR is *entirely* custom logic, not delegated to `dio_cache_interceptor` at all. The C# port should implement SWR as a custom layer on top of whatever cache library is chosen, not rely on the cache library's SWR support (though FusionCache does have built-in SWR).

### 5.2 `EncryptedCacheStore` Initialization is Not Thread-Safe in a Naive Port

The lazy initialization pattern in `encrypted_cache_store.dart:76-84` uses `_initFuture` as a guard:
```dart
Future<void> _ensureInitialized() async {
  if (_initFuture != null) {
    await _initFuture;
    return;
  }
  _initFuture = _initialize();
  await _initFuture;
}
```

In Dart's single-threaded async model, this is safe. In C#, a direct translation with `Task` would have a race condition. The C# port should use `Lazy<Task>` or `SemaphoreSlim` for thread-safe lazy initialization:
```csharp
private readonly Lazy<Task> _initTask;
// or
private readonly SemaphoreSlim _initLock = new(1, 1);
```

### 5.3 `TwoTierCacheStore.set()` Semantics: Memory is Awaited, Persistent is Not

A subtle but critical detail: `memoryStore.set(response)` is `await`ed but `persistentStore.set()` is `unawaited()`. This means if the process exits immediately after a write, L2 may not have the data. For the C# server port with Redis as L2, this could mean lost cache entries on app shutdown. Consider whether the C# port should await the L2 write or provide a graceful shutdown mechanism.

### 5.4 Offline Interceptor Catches `Object`, Not `Exception`

At `offline_interceptor.dart:121`:
```dart
} on Object catch (_) {
```

This catches everything including errors (not just exceptions). The C# equivalent should catch `Exception` (which in .NET already covers all catchable exceptions).

### 5.5 `clearCacheForUrl` Does Not Account for User Isolation

At `cache_interceptor.dart:458-463`:
```dart
Future<void> clearCacheForUrl(String url) async {
  final key = CacheOptions.defaultCacheKeyBuilder(url: Uri.parse(url));
  await _cacheOptions.store?.delete(key);
}
```

This uses `defaultCacheKeyBuilder` without user isolation. So `clearCacheForUrl()` only clears the *shared* cache key for that URL. If there are user-isolated entries (`baseKey:user123`, `baseKey:user456`), those are NOT cleared. This is a potential bug in the Dart source, or at least a surprising API behavior. The `AcdcCacheManager.clearCacheForUrl()` delegates to this same method.

The C# port should consider whether `clearCacheForUrl()` should clear all user variants of a URL or just the shared key. If clearing all variants is desired, it would need pattern-based deletion (e.g., `deleteFromPath` with a regex).

### 5.6 `hitCacheOnErrorCodes` Includes Auth Failures

As noted in Section 2.4, `hitCacheOnErrorCodes: [401, 403]` means that 401 Unauthorized and 403 Forbidden responses will trigger serving stale cache instead of propagating the error. This could mask authentication/authorization issues. The C# port should carefully decide whether to replicate this behavior or restrict `hitCacheOnErrorCodes` to 5xx errors only.

---

## Summary

The research document is comprehensive and well-written. Code snippets are accurate, the architecture is correctly described, and the C# porting recommendations are sound. The main areas for improvement are:

1. **Clarify the dual `CacheOptions` construction** and its implications.
2. **Add the `_CacheAwareRequestHandler`/`_CacheAwareErrorHandler` details** for complete interceptor behavior documentation.
3. **Note `maxSize` is not enforced** on `EncryptedCacheStore`.
4. **Expand C# FusionCache guidance** with `FactorySoftTimeout`, backplane, and `IHttpClientFactory` integration.
5. **Flag the `clearCacheForUrl` user isolation gap** as a potential Dart source bug.
6. **Add thread-safety notes** for the C# port of lazy initialization patterns.
