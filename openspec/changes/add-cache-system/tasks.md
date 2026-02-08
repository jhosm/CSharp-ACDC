# Tasks: Add Cache System with FusionCache Integration

## 1. Cache Key Builder

- [ ] 1.1 Implement shared key strategy (`GET:{url}`)
- [ ] 1.2 Implement user-isolated key strategy (`GET:{userId}:{url}`)
- [ ] 1.3 Implement no-caching strategy (returns null)

## 2. Cache Options

- [ ] 2.1 Create `AcdcCacheOptions` record mapped to FusionCache concepts (`Duration`, `FailSafeMaxDuration`, `FactorySoftTimeout`, `AllowTimedOutFactoryBackgroundCompletion`, `CacheKeyStrategy`, `ETagEnabled`)
- [ ] 2.2 Document mapping from Dart `CacheConfig` to FusionCache options in code comments

## 3. Cache Handler

- [ ] 3.1 Implement cache lookup before `base.SendAsync()` for GET/HEAD requests
- [ ] 3.2 Implement cache storage for successful GET/HEAD responses
- [ ] 3.3 Implement ETag storage and `If-None-Match` header injection on subsequent requests
- [ ] 3.4 Implement 304 Not Modified resolution from cached response
- [ ] 3.5 Implement SWR via FusionCache `FactorySoftTimeout` + `AllowTimedOutFactoryBackgroundCompletion`
- [ ] 3.6 Implement stale-if-error via FusionCache `IsFailSafeEnabled` + `FailSafeMaxDuration`
- [ ] 3.7 Implement mutation invalidation for POST/PUT/DELETE/PATCH (clears related GET cache entries)
- [ ] 3.8 Add `X-ACDC-From-Cache` response header and set `acdc_source` in `HttpRequestMessage.Options`
- [ ] 3.9 Implement user isolation via cache key strategy integration

## 4. Cache Manager

- [ ] 4.1 Define `IAcdcCacheManager` interface with `ClearCacheAsync()`, `ClearCacheForUrlAsync(string url)`, `ClearCacheForUserAsync(string userId)`
- [ ] 4.2 Implement `AcdcCacheManager.ClearCacheAsync()` -- clears entire cache
- [ ] 4.3 Implement `AcdcCacheManager.ClearCacheForUrlAsync()` -- clears ALL user variants for a URL
- [ ] 4.4 Implement `AcdcCacheManager.ClearCacheForUserAsync()` -- clears all cached entries for a specific user

## 5. Exception Finalization

- [ ] 5.1 Finalize `AcdcCacheException` factory methods based on FusionCache error patterns (`FusionCacheException`, Redis `ConnectionException`, serialization failures)

## 6. Unit Tests

- [ ] 6.1 Test cache hit returns cached response with `X-ACDC-From-Cache` header and `acdc_source` option
- [ ] 6.2 Test cache miss sends request downstream and stores response in FusionCache
- [ ] 6.3 Test ETag/If-None-Match round-trip (first request stores ETag, second request sends If-None-Match)
- [ ] 6.4 Test 304 Not Modified resolution returns cached content with updated headers
- [ ] 6.5 Test SWR returns stale data when refresh exceeds `FactorySoftTimeout`
- [ ] 6.6 Test stale-if-error returns cached data when downstream request fails
- [ ] 6.7 Test mutation invalidation: POST/PUT/DELETE/PATCH clears related GET cache keys
- [ ] 6.8 Test user-isolated cache keys produce separate entries per user
- [ ] 6.9 Test shared cache keys produce a single entry regardless of user
- [ ] 6.10 Test no-caching strategy bypasses cache entirely (no read, no write)
- [ ] 6.11 Test `ClearCacheForUrlAsync` clears all user variants for a given URL
- [ ] 6.12 Test `ClearCacheForUserAsync` clears all entries for a specific user
- [ ] 6.13 Test FusionCache error handling throws `AcdcCacheException` with correct `CacheOperation`
