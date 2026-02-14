# Tasks: Add Cache System with FusionCache Integration

## 1. Cache Key Builder

- [x] 1.1 Implement shared key strategy (`GET:{url}`)
- [x] 1.2 Implement user-isolated key strategy (`GET:{userId}:{url}`)
- [x] 1.3 Implement no-caching strategy (returns null)

## 2. Cache Options

- [x] 2.1 Create `AcdcCacheOptions` record mapped to FusionCache concepts (`Duration`, `FailSafeMaxDuration`, `FactorySoftTimeout`, `AllowTimedOutFactoryBackgroundCompletion`, `CacheKeyStrategy`, `ETagEnabled`)
- [x] 2.2 Document mapping from Dart `CacheConfig` to FusionCache options in code comments

## 3. Cache Handler

- [x] 3.1 Implement cache lookup before `base.SendAsync()` for GET/HEAD requests
- [x] 3.2 Implement cache storage for successful GET/HEAD responses
- [x] 3.3 Implement ETag storage and `If-None-Match` header injection on subsequent requests
- [x] 3.4 Implement 304 Not Modified resolution from cached response
- [x] 3.5 Implement SWR via FusionCache `FactorySoftTimeout` + `AllowTimedOutFactoryBackgroundCompletion`
- [x] 3.6 Implement stale-if-error via FusionCache `IsFailSafeEnabled` + `FailSafeMaxDuration`
- [x] 3.7 Implement mutation invalidation for POST/PUT/DELETE/PATCH (clears related GET cache entries)
- [x] 3.8 Add `X-ACDC-From-Cache` response header and set `acdc_source` in `HttpRequestMessage.Options`
- [x] 3.9 Implement user isolation via cache key strategy integration

## 4. Cache Manager

- [x] 4.1 Define `IAcdcCacheManager` interface with `ClearCacheAsync()`, `ClearCacheForUrlAsync(string url)`, `ClearCacheForUserAsync(string userId)`
- [x] 4.2 Implement `AcdcCacheManager.ClearCacheAsync()` -- clears entire cache
- [x] 4.3 Implement `AcdcCacheManager.ClearCacheForUrlAsync()` -- clears ALL user variants for a URL
- [x] 4.4 Implement `AcdcCacheManager.ClearCacheForUserAsync()` -- clears all cached entries for a specific user

## 5. Exception Finalization

- [x] 5.1 Finalize `AcdcCacheException` factory methods based on FusionCache error patterns (`FusionCacheException`, Redis `ConnectionException`, serialization failures)

## 6. Unit Tests

- [x] 6.1 Test cache hit returns cached response with `X-ACDC-From-Cache` header and `acdc_source` option
- [x] 6.2 Test cache miss sends request downstream and stores response in FusionCache
- [x] 6.3 Test ETag/If-None-Match round-trip (first request stores ETag, second request sends If-None-Match)
- [x] 6.4 Test 304 Not Modified resolution returns cached content with updated headers
- [x] 6.5 Test SWR returns stale data when refresh exceeds `FactorySoftTimeout`
- [x] 6.6 Test stale-if-error returns cached data when downstream request fails
- [x] 6.7 Test mutation invalidation: POST/PUT/DELETE/PATCH clears related GET cache keys
- [x] 6.8 Test user-isolated cache keys produce separate entries per user
- [x] 6.9 Test shared cache keys produce a single entry regardless of user
- [x] 6.10 Test no-caching strategy bypasses cache entirely (no read, no write)
- [x] 6.11 Test `ClearCacheForUrlAsync` clears all user variants for a given URL
- [x] 6.12 Test `ClearCacheForUserAsync` clears all entries for a specific user
- [x] 6.13 Test FusionCache error handling throws `AcdcCacheException` with correct `CacheOperation`
