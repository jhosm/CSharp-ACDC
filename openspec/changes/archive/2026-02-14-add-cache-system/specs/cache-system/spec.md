## ADDED Requirements

### Requirement: Cache Lookup
CacheHandler SHALL check FusionCache for a cached response before sending GET or HEAD requests downstream via `base.SendAsync()`. If a valid (non-expired) cached entry exists and no ETag-based revalidation is needed, the handler SHALL return the cached response directly without invoking the downstream pipeline.

#### Scenario: Cache hit for GET request
- **WHEN** a GET request is sent for a URL that has a valid cached response in FusionCache
- **THEN** CacheHandler SHALL return the cached response without calling `base.SendAsync()`
- **AND** the response content, status code, and headers SHALL match the originally cached response

#### Scenario: Cache miss for GET request
- **WHEN** a GET request is sent for a URL that has no cached entry in FusionCache
- **THEN** CacheHandler SHALL call `base.SendAsync()` to send the request downstream
- **AND** the downstream response SHALL be returned to the caller

#### Scenario: Non-GET/HEAD request bypasses cache lookup
- **WHEN** a POST request is sent
- **THEN** CacheHandler SHALL NOT check the cache
- **AND** SHALL call `base.SendAsync()` directly

### Requirement: Cache Storage
CacheHandler SHALL store successful (2xx) GET and HEAD responses in FusionCache with the duration configured in `AcdcCacheOptions.Duration`. Only responses with success status codes SHALL be cached.

#### Scenario: Successful GET response is cached
- **WHEN** a GET request results in a 200 OK response from the downstream service
- **THEN** CacheHandler SHALL store the response in FusionCache with the configured `Duration`
- **AND** subsequent GET requests for the same URL SHALL return the cached response

#### Scenario: Error response is not cached
- **WHEN** a GET request results in a 500 Internal Server Error from the downstream service
- **THEN** CacheHandler SHALL NOT store the response in FusionCache

#### Scenario: HEAD response is cached
- **WHEN** a HEAD request results in a 200 OK response
- **THEN** CacheHandler SHALL store the response headers and status code in FusionCache

### Requirement: ETag Support
CacheHandler SHALL store ETag values from downstream responses inside the `CachedResponse` record. On subsequent requests for the same URL where a cached entry with an ETag exists, CacheHandler SHALL add an `If-None-Match` header with the stored ETag value to the outgoing request.

#### Scenario: ETag stored from downstream response
- **WHEN** a downstream response includes an `ETag` header with value `"abc123"`
- **THEN** CacheHandler SHALL store `"abc123"` as the ETag in the `CachedResponse` record alongside the response content

#### Scenario: If-None-Match header sent on subsequent request
- **WHEN** a GET request is sent for a URL that has a cached entry with ETag `"abc123"`
- **THEN** CacheHandler SHALL add the header `If-None-Match: "abc123"` to the outgoing request before calling `base.SendAsync()`

#### Scenario: ETag disabled via configuration
- **WHEN** `AcdcCacheOptions.ETagEnabled` is set to `false`
- **THEN** CacheHandler SHALL NOT send `If-None-Match` headers
- **AND** SHALL NOT store ETag values from responses

### Requirement: 304 Not Modified Resolution
CacheHandler SHALL resolve `304 Not Modified` responses from the downstream service by returning the cached response content with the original status code and updated headers. The cached entry SHALL be refreshed with the new TTL.

#### Scenario: 304 response returns cached content
- **WHEN** CacheHandler sends a request with `If-None-Match` and the downstream returns 304 Not Modified
- **THEN** CacheHandler SHALL return the cached response content with a 200 status code
- **AND** SHALL merge any updated headers from the 304 response into the cached response headers
- **AND** SHALL refresh the cached entry's TTL in FusionCache

#### Scenario: 304 response without prior cache entry
- **WHEN** CacheHandler receives a 304 Not Modified response but has no cached entry for the URL
- **THEN** CacheHandler SHALL return the 304 response as-is to the caller without attempting resolution

### Requirement: Stale-While-Revalidate
CacheHandler SHALL support stale-while-revalidate (SWR) semantics using FusionCache's `FactorySoftTimeout`. When a cached entry is expired but the downstream refresh takes longer than the configured `FactorySoftTimeout`, CacheHandler SHALL return the stale cached data immediately and complete the refresh in the background.

#### Scenario: Stale data returned when refresh is slow
- **WHEN** a GET request is sent for a URL with an expired cached entry
- **AND** the downstream service takes longer than `AcdcCacheOptions.FactorySoftTimeout` to respond
- **THEN** CacheHandler SHALL return the stale cached response immediately
- **AND** `AcdcCacheOptions.AllowTimedOutFactoryBackgroundCompletion` SHALL ensure the refresh continues in the background

#### Scenario: Fresh data returned when refresh is fast
- **WHEN** a GET request is sent for a URL with an expired cached entry
- **AND** the downstream service responds within `AcdcCacheOptions.FactorySoftTimeout`
- **THEN** CacheHandler SHALL return the fresh downstream response
- **AND** SHALL update the cached entry with the fresh response

### Requirement: Stale-If-Error
CacheHandler SHALL return stale cached data when the downstream request fails, using FusionCache's fail-safe mechanism (`IsFailSafeEnabled`). Stale data SHALL remain usable as fail-safe for the duration specified in `AcdcCacheOptions.FailSafeMaxDuration`.

#### Scenario: Stale data returned on downstream failure
- **WHEN** a GET request is sent for a URL with an expired cached entry
- **AND** the downstream service returns a 500 error or throws an exception
- **AND** `IsFailSafeEnabled` is true (derived from `FailSafeMaxDuration` being configured)
- **THEN** CacheHandler SHALL return the stale cached response instead of propagating the error

#### Scenario: No stale data available on downstream failure
- **WHEN** a GET request is sent for a URL with no cached entry (not even expired)
- **AND** the downstream service returns a 500 error
- **THEN** CacheHandler SHALL propagate the error as normal (no fail-safe data available)

#### Scenario: Stale data expired beyond FailSafeMaxDuration
- **WHEN** a GET request is sent for a URL with a cached entry that expired longer ago than `FailSafeMaxDuration`
- **AND** the downstream service fails
- **THEN** CacheHandler SHALL propagate the error (stale data is too old for fail-safe)

### Requirement: Mutation Invalidation
CacheHandler SHALL invalidate cached entries when POST, PUT, DELETE, or PATCH requests are sent. The handler SHALL remove all cached GET entries for the request URL, including all user-isolated variants.

#### Scenario: POST invalidates cached GET for same URL
- **WHEN** a POST request is sent to `https://api.example.com/products`
- **THEN** CacheHandler SHALL remove the cached GET entry for `https://api.example.com/products`
- **AND** SHALL remove all user-isolated variants of that cached entry

#### Scenario: DELETE invalidates cached GET
- **WHEN** a DELETE request is sent to `https://api.example.com/products/42`
- **THEN** CacheHandler SHALL remove the cached GET entry for `https://api.example.com/products/42`

#### Scenario: GET request does not trigger invalidation
- **WHEN** a GET request is sent
- **THEN** CacheHandler SHALL NOT invalidate any cached entries (cache lookup and storage only)

### Requirement: Cache Metadata
CacheHandler SHALL add an `X-ACDC-From-Cache` header to HTTP responses that are served from cache. The handler SHALL also set the `acdc_source` key in `HttpRequestMessage.Options` to indicate the response source.

#### Scenario: Cache hit includes metadata header
- **WHEN** a GET request results in a cache hit
- **THEN** the returned `HttpResponseMessage` SHALL include the header `X-ACDC-From-Cache: true`
- **AND** the `acdc_source` option on the request SHALL be set to `"cache"`

#### Scenario: Cache miss does not include metadata header
- **WHEN** a GET request results in a cache miss and the response comes from downstream
- **THEN** the returned `HttpResponseMessage` SHALL NOT include the `X-ACDC-From-Cache` header
- **AND** the `acdc_source` option on the request SHALL be set to `"network"`

#### Scenario: SWR stale response includes metadata
- **WHEN** a stale cached response is returned due to SWR timeout
- **THEN** the `X-ACDC-From-Cache` header SHALL be present with value `"stale"`
- **AND** the `acdc_source` option SHALL be set to `"cache-stale"`

### Requirement: User Isolation
CacheHandler SHALL support per-user cache isolation by including the user ID in cache keys when the `CacheKeyStrategy` is configured as user-isolated. User-isolated caching SHALL ensure that one user's cached responses are not returned to a different user.

#### Scenario: User-isolated cache keys separate users
- **WHEN** user "alice" makes a GET request to `https://api.example.com/profile`
- **AND** user "bob" makes a GET request to the same URL
- **AND** the `CacheKeyStrategy` is user-isolated
- **THEN** each user SHALL have a separate cache entry
- **AND** Alice's cached response SHALL NOT be returned to Bob

#### Scenario: Shared cache keys serve all users
- **WHEN** user "alice" makes a GET request to `https://api.example.com/products`
- **AND** user "bob" makes a GET request to the same URL
- **AND** the `CacheKeyStrategy` is shared
- **THEN** both users SHALL share the same cache entry
- **AND** Alice's cached response MAY be returned to Bob

### Requirement: Cache Key Strategies
`CacheKeyBuilder` SHALL provide three key strategies for generating cache keys: shared, user-isolated, and no-caching. The shared strategy SHALL generate keys in the format `{METHOD}:{url}`. The user-isolated strategy SHALL generate keys in the format `{METHOD}:{userId}:{url}`. The no-caching strategy SHALL return null to indicate that the request should not be cached.

#### Scenario: Shared key format
- **WHEN** `CacheKeyBuilder` generates a shared key for a GET request to `https://api.example.com/products`
- **THEN** the key SHALL be `"GET:https://api.example.com/products"`

#### Scenario: User-isolated key format
- **WHEN** `CacheKeyBuilder` generates a user-isolated key for user "user-42" and a GET request to `https://api.example.com/profile`
- **THEN** the key SHALL be `"GET:user-42:https://api.example.com/profile"`

#### Scenario: No-caching strategy returns null
- **WHEN** `CacheKeyBuilder` generates a key using the no-caching strategy
- **THEN** the result SHALL be null
- **AND** CacheHandler SHALL skip all cache operations for this request

#### Scenario: HEAD request key format
- **WHEN** `CacheKeyBuilder` generates a shared key for a HEAD request to `https://api.example.com/products`
- **THEN** the key SHALL be `"HEAD:https://api.example.com/products"`

### Requirement: Cache Manager
`IAcdcCacheManager` SHALL provide methods to clear the entire cache, clear cache for a specific URL across all user variants, and clear cache for a specific user across all URLs. `AcdcCacheManager` SHALL implement this interface using FusionCache's eviction APIs.

#### Scenario: Clear entire cache
- **WHEN** `ClearCacheAsync()` is called
- **THEN** all cached entries managed by the ACDC cache handler SHALL be removed from both L1 and L2

#### Scenario: Clear cache for a specific URL (all user variants)
- **WHEN** `ClearCacheForUrlAsync("https://api.example.com/products")` is called
- **AND** cache contains entries for shared key, user "alice", and user "bob" for that URL
- **THEN** all three cache entries SHALL be removed
- **AND** entries for other URLs SHALL NOT be affected

#### Scenario: Clear cache for a specific user
- **WHEN** `ClearCacheForUserAsync("user-42")` is called
- **AND** cache contains entries for user "user-42" across multiple URLs
- **THEN** all cache entries for "user-42" SHALL be removed
- **AND** cache entries for other users SHALL NOT be affected

### Requirement: Cache Configuration
`AcdcCacheOptions` SHALL be a record type that exposes FusionCache-aligned configuration properties. All properties SHALL have sensible defaults. The options SHALL be registered via the `IOptions<T>` pattern for dependency injection.

#### Scenario: Default configuration values
- **WHEN** `AcdcCacheOptions` is created with default values
- **THEN** `Duration` SHALL default to a reasonable TTL (e.g., 5 minutes)
- **AND** `ETagEnabled` SHALL default to `true`
- **AND** `CacheKeyStrategy` SHALL default to shared
- **AND** `AllowTimedOutFactoryBackgroundCompletion` SHALL default to `true`

#### Scenario: Custom SWR configuration
- **WHEN** `AcdcCacheOptions` is configured with `FactorySoftTimeout = TimeSpan.FromSeconds(2)` and `FailSafeMaxDuration = TimeSpan.FromHours(1)`
- **THEN** CacheHandler SHALL use these values when configuring FusionCache entry options
- **AND** stale-while-revalidate SHALL activate after 2 seconds
- **AND** fail-safe data SHALL remain available for up to 1 hour

#### Scenario: IOptions registration
- **WHEN** the ACDC cache system is registered in the DI container
- **THEN** `AcdcCacheOptions` SHALL be configurable via `IOptions<AcdcCacheOptions>`
- **AND** configuration SHALL be bindable from `IConfiguration` sections

### Requirement: Non-Cacheable Passthrough
CacheHandler SHALL pass through requests that use the no-caching key strategy without any cache interaction. The handler SHALL call `base.SendAsync()` directly and return the downstream response unmodified.

#### Scenario: No-cache strategy skips cache entirely
- **WHEN** a GET request is sent with the no-caching `CacheKeyStrategy` configured
- **THEN** CacheHandler SHALL NOT check FusionCache for a cached response
- **AND** SHALL NOT store the downstream response in FusionCache
- **AND** SHALL NOT add `X-ACDC-From-Cache` headers

#### Scenario: Non-GET methods with no-cache strategy
- **WHEN** a POST request is sent with the no-caching `CacheKeyStrategy`
- **THEN** CacheHandler SHALL pass the request through to `base.SendAsync()` without cache interaction
- **AND** SHALL NOT attempt mutation invalidation

### Requirement: Cache Exception Handling
CacheHandler SHALL catch exceptions originating from FusionCache operations and throw `AcdcCacheException` with the appropriate `CacheOperation` enum value. Cache failures SHALL NOT prevent the downstream request from being attempted -- if the cache read fails, the handler SHALL proceed to call `base.SendAsync()`.

#### Scenario: Cache read failure falls through to downstream
- **WHEN** FusionCache throws an exception during cache lookup (e.g., Redis connection failure)
- **THEN** CacheHandler SHALL log the error
- **AND** SHALL proceed to call `base.SendAsync()` as if the cache were empty
- **AND** SHALL NOT throw `AcdcCacheException` to the caller for read failures

#### Scenario: Cache write failure does not affect response
- **WHEN** FusionCache throws an exception during cache storage after a successful downstream response
- **THEN** CacheHandler SHALL log the error
- **AND** SHALL return the downstream response to the caller successfully
- **AND** SHALL NOT throw `AcdcCacheException` for write failures

#### Scenario: Unrecoverable cache failure throws AcdcCacheException
- **WHEN** an unrecoverable FusionCache error occurs (e.g., serialization failure that prevents any cache operation)
- **THEN** CacheHandler SHALL throw `AcdcCacheException` with the appropriate `CacheOperation` value
- **AND** the exception SHALL include the original FusionCache exception as the inner exception
