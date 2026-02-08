# Design: Cache System with FusionCache Integration

## Context

Dart-ACDC uses a custom `TwoTierCacheStore` with L1 (`MemCacheStore`, in-memory LRU) and L2 (`EncryptedCacheStore`, AES-encrypted file-based storage). It implements stale-while-revalidate (SWR) and ETag/If-None-Match manually in `CacheInterceptor`, with user isolation via JWT-extracted user IDs baked into cache keys.

The C# port targets server-side only (ASP.NET Core / .NET 8+). On server, file-based encrypted cache is inappropriate -- Redis via `IDistributedCache` is the standard L2. FusionCache is a mature .NET library that provides L1 (`IMemoryCache`) + L2 (`IDistributedCache`/Redis) with built-in SWR (`FactorySoftTimeout`), fail-safe (`IsFailSafeEnabled`), stampede prevention (single-flight), and automatic L1/L2 synchronization. Using FusionCache replaces hundreds of lines of custom Dart cache management code with a battle-tested library.

Key stakeholders: any ASP.NET Core service consuming downstream HTTP APIs through the ACDC pipeline. The cache handler sits at position 5 in the handler chain (after Auth, before Custom and Deduplication handlers).

## Goals

- Replace Dart's manual two-tier cache (`TwoTierCacheStore`) with FusionCache's native L1+L2 support
- Maintain SWR, ETag/If-None-Match, and user isolation semantics from the Dart source
- Fix Dart bug: `ClearCacheForUrlAsync` must clear ALL user variants, not just the current user's entry
- Provide `IAcdcCacheManager` for administrative cache operations (clear all, clear by URL, clear by user)
- Thread-safe design -- `DelegatingHandler` instances are pooled by `IHttpClientFactory`

## Non-Goals

- No encrypted cache store -- Redis handles its own security (TLS, ACLs). The Dart `EncryptedCacheStore` with AES encryption is a mobile concern.
- No offline support -- servers do not go offline. Transient downstream failures are handled by FusionCache fail-safe and Polly resilience policies (outside ACDC scope).
- No custom eviction policies -- FusionCache manages L1 eviction via `IMemoryCache` size limits and L2 via Redis TTL.
- No cache warming or preloading -- out of scope for the handler; can be layered on top via `IAcdcCacheManager`.

## Decisions

### 1. FusionCache over manual two-tier implementation

**Decision:** Use `ZiggyCreatures.FusionCache` as the caching engine instead of implementing custom L1+L2 synchronization.

**Rationale:** FusionCache provides:
- L1 (`IMemoryCache`) + L2 (`IDistributedCache`/Redis) with automatic synchronization
- Built-in SWR via `FactorySoftTimeout` + `AllowTimedOutFactoryBackgroundCompletion`
- Built-in fail-safe via `IsFailSafeEnabled` + `FailSafeMaxDuration` (stale-if-error)
- Stampede prevention (only one factory call per key, others wait -- equivalent to Dart's deduplication within the cache layer)
- Backplane support for multi-instance L1 invalidation
- Graceful L2 degradation (if Redis is unavailable, L1 continues working)

This replaces approximately 400+ lines of custom Dart code (`TwoTierCacheStore`, `MemCacheStore`, `EncryptedCacheStore`, SWR logic in `CacheInterceptor`).

**Alternatives considered:**
- Manual `IMemoryCache` + `IDistributedCache` -- requires implementing synchronization, SWR, fail-safe, and stampede prevention manually. High risk of subtle concurrency bugs.
- `Microsoft.Extensions.Caching.Hybrid` (HybridCache) -- newer .NET option but less mature than FusionCache for the specific SWR and fail-safe patterns needed. FusionCache has been stable since 2021.

### 2. Cache serialization via System.Text.Json

**Decision:** Use `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` for Redis L2 serialization.

**Rationale:** Required for FusionCache L2 (Redis stores byte arrays, not .NET objects). `System.Text.Json` is the standard .NET serializer, already a framework dependency. The FusionCache STJ serializer package provides the `IFusionCacheSerializer` implementation.

**What gets serialized:** A `CachedResponse` record containing the response metadata:
```csharp
public record CachedResponse(
    byte[] Content,
    Dictionary<string, string[]> Headers,
    int StatusCode,
    string? ETag
);
```

**Trade-off:** Storing `byte[]` content (not deserialized body) means the cached entry size equals the original response size. This is intentional -- the handler caches at the HTTP level, not at the domain object level. Callers can layer domain-level caching on top.

### 3. Cache key format: method + URL + optional userId

**Decision:** Cache keys follow the format `{METHOD}:{userId?}:{url}` where userId is optional based on the configured `CacheKeyStrategy`.

**Rationale:** Consistent with Dart's `_generateCacheKey` which uses `{method}:{userId}:{url}`, but the C# version adds the method prefix explicitly (Dart uses it implicitly) and makes userId truly optional via the strategy pattern.

Key format examples:
- Shared: `GET:https://api.example.com/products`
- User-isolated: `GET:user-42:https://api.example.com/profile`
- No-cache: returns `null` (handler skips cache entirely)

**Alternatives considered:**
- Hash-based keys (`SHA256(method+url+userId)`) -- shorter but unreadable in Redis inspection. Debugging cache issues requires readable keys.
- Include request headers in key -- Dart does not do this, and it would cause cache fragmentation. Vary-based caching is out of scope.

### 4. ETag stored alongside cached response in wrapper record

**Decision:** ETag values are stored as a property of `CachedResponse`, not as separate cache entries.

**Rationale:** Dart stores ETags in a parallel cache structure (separate key). This adds complexity and creates consistency issues (cached response and ETag can go out of sync). Storing the ETag inside the `CachedResponse` record ensures atomicity -- when the response is evicted, the ETag goes with it.

**ETag flow:**
1. First GET: downstream returns `200 OK` with `ETag: "abc"` -- store `CachedResponse { Content, Headers, StatusCode: 200, ETag: "abc" }`
2. Second GET (cache hit with ETag): send request with `If-None-Match: "abc"`
3. Downstream returns `304 Not Modified`: return cached `CachedResponse.Content` with updated headers
4. Downstream returns `200 OK` with new ETag: replace cached entry with new response

### 5. Mutation invalidation strategy

**Decision:** POST/PUT/DELETE/PATCH requests on a URL path invalidate the corresponding GET cache entries for that base URL.

**Rationale:** Dart's `CacheInterceptor` invalidates cache on mutations. The C# version does the same but must handle user isolation -- a mutation should invalidate ALL user variants of the URL, not just the current user's entry.

**Implementation approach:** Use FusionCache tag-based eviction if available (FusionCache v1.x supports tags). If tags are not viable, maintain a `ConcurrentDictionary<string, HashSet<string>>` mapping base URLs to their full cache keys (including user-prefixed variants), enabling prefix-based invalidation.

**Trade-off:** Tag-based eviction is cleaner but adds a FusionCache version dependency. Manual tracking adds memory overhead but is version-independent. Decision to finalize during implementation based on FusionCache version available.

### 6. ClearCacheForUrlAsync clears ALL user variants (Dart bug fix)

**Decision:** `ClearCacheForUrlAsync(string url)` removes cache entries for ALL users for that URL, not just the current user.

**Rationale:** The Dart source's `clearCacheForUrl` only clears the cache entry for the current user's cache key. This is a bug -- if an admin invalidates a cached product response, it should be invalidated for all users, not just the admin. The C# implementation fixes this by iterating over all known user variants of the URL key.

### 7. SWR and fail-safe mapping from Dart to FusionCache

**Decision:** Map Dart's custom SWR implementation to FusionCache's native SWR support.

**Mapping:**
| Dart concept | FusionCache concept |
|---|---|
| `staleWhileRevalidate: Duration(seconds: 30)` | `FactorySoftTimeout = TimeSpan.FromSeconds(30)` -- if factory (downstream call) takes longer than this, return stale data |
| Background refresh after SWR timeout | `AllowTimedOutFactoryBackgroundCompletion = true` -- factory continues running in background |
| `staleIfError: true` | `IsFailSafeEnabled = true` -- return stale data if factory throws |
| `staleIfError` max age | `FailSafeMaxDuration` -- how long stale data remains usable as fail-safe |
| Cache entry TTL | `Duration` -- maps to FusionCache entry `Duration` |

**Key difference from Dart:** Dart implements SWR manually with timers and `Completer`. FusionCache handles it natively, including stampede prevention (multiple concurrent requests for the same expired key only trigger one factory call).

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| FusionCache API may differ from assumptions in this design | Factory method signatures are stubbed; finalized during implementation. Core concepts (SWR, fail-safe, L1+L2) are stable FusionCache features. |
| Redis L2 unavailable at runtime | FusionCache gracefully degrades to L1-only mode. `AcdcCacheException` is thrown only for unexpected failures, not for L2 unavailability. |
| `CachedResponse` serialization overhead for large responses | Mitigated by configurable `Duration` (short TTL for large responses) and Redis memory limits. The handler caches at HTTP level -- callers should not cache multi-MB responses. |
| Tag-based eviction may not be available in FusionCache version used | Fallback to manual key tracking via `ConcurrentDictionary`. Decision deferred to implementation. |
| `DelegatingHandler` pooling means CacheHandler must not store per-request state | All per-request state flows through `HttpRequestMessage.Options`. FusionCache instance is injected via constructor (singleton lifetime, thread-safe). |
| Multi-instance deployments need L1 cache synchronization | FusionCache backplane (Redis pub/sub) handles this. Not in scope for initial implementation but architecture supports it. |

## Open Questions

- Should `CacheHandler` respect standard HTTP `Cache-Control` headers from downstream responses (e.g., `no-store`, `max-age`), or only use `AcdcCacheOptions` configuration? (Current decision: `AcdcCacheOptions` only, consistent with Dart source which ignores HTTP cache headers.)
- Should `ClearCacheForUserAsync` accept a wildcard pattern, or only exact user IDs? (Current decision: exact user IDs only, for simplicity and security.)
- Should the `CachedResponse` record include the response `ReasonPhrase`, or just the status code? (Current decision: status code only -- reason phrases are deprecated in HTTP/2+.)
