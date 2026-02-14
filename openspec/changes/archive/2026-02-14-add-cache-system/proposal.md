# Change: Add Cache System with FusionCache Integration

## Why

HTTP response caching with stale-while-revalidate (SWR), ETag support, and user isolation is essential for reducing downstream service load and improving response times. FusionCache provides two-tier caching (memory L1 + Redis L2) with built-in SWR and fail-safe semantics, replacing Dart-ACDC's custom `TwoTierCacheStore`. This is P6 -- the caching layer that completes the core handler pipeline.

## What Changes

### CacheHandler : DelegatingHandler
FusionCache L1 (memory) + L2 (Redis) integration as a `DelegatingHandler` in the pipeline:
- Cache lookup for GET/HEAD requests before `base.SendAsync()`
- ETag/If-None-Match conditional request support + 304 Not Modified resolution
- SWR via FusionCache `FactorySoftTimeout` + `AllowTimedOutFactoryBackgroundCompletion`
- Stale-if-error via FusionCache `IsFailSafeEnabled`
- Mutation invalidation (POST/PUT/DELETE/PATCH clear related cached keys)
- Response metadata: `X-ACDC-From-Cache` response header, `acdc_source` in request options
- User isolation via cache key strategy (includes user ID in key)

### CacheKeyBuilder
Static class with key strategies:
- **Shared:** `GET:{url}` (no user isolation)
- **User-isolated:** `GET:{userId}:{url}` (per-user caching)
- **No-caching:** returns null to skip caching

### AcdcCacheOptions
Record type mapped to FusionCache concepts:
- `Duration` (default entry lifetime)
- `FailSafeMaxDuration` (how long stale data is kept)
- `FactorySoftTimeout` (SWR timeout -- return stale data if refresh takes longer)
- `AllowTimedOutFactoryBackgroundCompletion` (continue refresh in background)
- `CacheKeyStrategy` (shared / user-isolated / no-cache)
- `ETagEnabled` (default: true)

### IAcdcCacheManager + AcdcCacheManager
Cache management interface and implementation:
- `ClearCacheAsync()` -- clears entire cache
- `ClearCacheForUrlAsync(string url)` -- clears ALL user variants for a URL (fixes Dart bug that only clears current user's cache)
- `ClearCacheForUserAsync(string userId)` -- clears all cached entries for a specific user

### AcdcCacheException Finalization
Finalize `AcdcCacheException` factory methods from P2 based on actual FusionCache error patterns (e.g., `FusionCacheException`, Redis `ConnectionException`).

## Impact

- **Affected specs:** cache-system (new)
- **Depends on:** P2 (exceptions, `AcdcRequestOptions`)
- **Parallel with:** P3, P4, P5
- **Affected code:**
  - `src/CSharpAcdc/Handlers/CacheHandler.cs` -- cache pipeline handler
  - `src/CSharpAcdc/Cache/CacheKeyBuilder.cs` -- key generation strategies
  - `src/CSharpAcdc/Cache/IAcdcCacheManager.cs` -- cache management interface
  - `src/CSharpAcdc/Cache/AcdcCacheManager.cs` -- cache management implementation
  - `src/CSharpAcdc/Configuration/AcdcCacheOptions.cs` -- cache configuration record
  - `tests/CSharpAcdc.Tests/Handlers/CacheHandlerTests.cs` -- core handler tests
  - `tests/CSharpAcdc.Tests/Handlers/CacheHandlerETagTests.cs` -- ETag round-trip tests
  - `tests/CSharpAcdc.Tests/Handlers/CacheHandlerSwrTests.cs` -- SWR and fail-safe tests
  - `tests/CSharpAcdc.Tests/Cache/CacheKeyBuilderTests.cs` -- key strategy tests
  - `tests/CSharpAcdc.Tests/Cache/AcdcCacheManagerTests.cs` -- cache manager tests
