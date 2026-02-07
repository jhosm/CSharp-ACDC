# Research Document 03: Caching System and Offline Support

## Overview

The Dart-ACDC caching subsystem provides a sophisticated, multi-layered HTTP response caching architecture with encrypted disk persistence, user isolation via JWT, HTTP cache semantics (Cache-Control, ETag, If-None-Match), stale-while-revalidate (SWR) patterns, and offline fallback support. The system is built around the `dio_cache_interceptor` package and extends it with custom interceptors for ACDC-specific behavior.

---

## 1. Two-Tier Cache Architecture (L1 Memory + L2 Disk)

### 1.1 Architecture Diagram

```
Request Flow:
  GET /api/users
      |
      v
  [AcdcCacheInterceptor]
      |
      v
  [TwoTierCacheStore]
      |--- L1: MemCacheStore (in-memory LRU, 5MB default)
      |--- L2: EncryptedCacheStore (AES-256-GCM, file-based, 10MB default)
```

### 1.2 TwoTierCacheStore

**Source**: `lib/src/cache/two_tier_cache_store.dart`

The `TwoTierCacheStore` implements the `CacheStore` interface and combines an in-memory LRU cache (L1) with a persistent encrypted store (L2). Key behaviors:

- **Read path**: Memory first (fast), then persistent (slower). On L2 hit, the entry is *promoted* to L1 for subsequent fast access.
- **Write path**: Writes to L1 synchronously, then fires-and-forgets an async write to L2. This ensures fast writes without blocking on disk I/O.
- **Delete/Clean**: Operations execute on both tiers in parallel.
- **Graceful degradation**: All persistent store operations are wrapped in `.catchError((_) => null)` -- if L2 fails, L1 continues working.

```dart
// Read: memory-first with promotion (two_tier_cache_store.dart:107-130)
@override
Future<CacheResponse?> get(String key) async {
  // Try memory first (fast path)
  var response = await memoryStore.get(key);
  if (response != null) {
    return response;
  }

  // Try persistent store (slower path)
  if (persistentStore != null) {
    try {
      response = await persistentStore!.get(key);
      if (response != null) {
        // Promote to memory cache for faster future access
        await memoryStore.set(response).catchError((_) => null);
        return response;
      }
    } on Exception catch (_) {
      return null;
    }
  }
  return null;
}

// Write: memory is critical, persistent is fire-and-forget (two_tier_cache_store.dart:163-173)
@override
Future<void> set(CacheResponse response) async {
  await memoryStore.set(response);
  if (persistentStore != null) {
    unawaited(persistentStore!.set(response).catchError((_) => null));
  }
}
```

### 1.3 CacheStoreFactory

**Source**: `lib/src/cache/cache_store_factory.dart`

Platform-aware factory that creates the appropriate cache store:

| Platform | `inMemory=true` (default) | `inMemory=false` |
|----------|---------------------------|-------------------|
| **Web** | `MemCacheStore` only | `MemCacheStore` only |
| **Native** | `TwoTierCacheStore` (Mem + Encrypted) | `EncryptedCacheStore` only |

```dart
// cache_store_factory.dart:25-57
static CacheStore build(CacheConfig config) {
  if (kIsWeb) {
    return MemCacheStore(maxSize: config.inMemoryMaxSize);
  }

  final persistentStore = EncryptedCacheStore(
    maxSize: config.maxSize,
    version: config.version,
    onError: config.onError,
    storePath: config.storePath,
  );

  if (config.inMemory) {
    final memoryStore = MemCacheStore(maxSize: config.inMemoryMaxSize);
    return TwoTierCacheStore(
      memoryStore: memoryStore,
      persistentStore: persistentStore,
    );
  }

  return persistentStore;
}
```

---

## 2. Cache Configuration Options and Defaults

**Source**: `lib/src/cache/cache_config.dart`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ttl` | `Duration` | 1 hour | Cache entry time-to-live |
| `maxSize` | `int` | 10 MB (10,485,760) | Maximum disk cache size in bytes |
| `inMemory` | `bool` | `true` | Enable in-memory L1 cache layer |
| `inMemoryMaxSize` | `int` | 5 MB (5,242,880) | Maximum memory cache size in bytes |
| `cacheAuthenticatedRequests` | `bool` | `true` | Cache authenticated requests (with user isolation) |
| `staleWhileRevalidate` | `bool` | `false` | Serve stale then refresh in background |
| `staleIfError` | `bool` | `true` | Serve stale cache on network errors |
| `userIdProvider` | `Future<String?> Function(String)?` | `null` | Custom user ID extraction for non-JWT auth |
| `keyBuilder` | `String Function(RequestOptions)?` | `null` | Custom cache key generator |
| `version` | `String?` | `null` | Cache version (change invalidates entire cache) |
| `onError` | `void Function(Object, StackTrace)?` | `null` | Error callback for cache operations |
| `storePath` | `String?` | `null` | Custom cache storage path (defaults to app documents dir) |

The `CacheConfig` class is immutable (all `final` fields, `const` constructor).

---

## 3. HTTP Cache Semantics

### 3.1 Cache-Control Header

**Source**: `lib/src/interceptors/cache_interceptor.dart`

The interceptor uses `CachePolicy.request` from `dio_cache_interceptor`, which respects standard HTTP caching headers:

- `Cache-Control: max-age=N` -- Respected for TTL
- `Cache-Control: no-cache` -- Forces revalidation
- `Cache-Control: no-store` -- Prevents caching

The `maxStale` parameter is set to 7 days when `staleIfError` or `staleWhileRevalidate` is enabled, allowing stale cache to be served for up to a week:

```dart
// cache_interceptor.dart:54-57
maxStale: (config.staleIfError || config.staleWhileRevalidate)
    ? const Duration(days: 7)
    : null,
hitCacheOnErrorCodes: config.staleIfError ? [401, 403] : [],
```

### 3.2 ETag and If-None-Match (Conditional Requests)

**Source**: `test/interceptors/etag_cache_test.dart`

The cache interceptor supports full ETag-based conditional requests:

1. **First request**: Server responds with `200 OK` + `ETag: "test-etag-12345"` header. Response is cached.
2. **Subsequent request**: Client sends `If-None-Match: "test-etag-12345"` header.
3. **304 Not Modified**: Server responds with `304`. The interceptor resolves with cached content, returning status `200` to the caller.

```dart
// cache_interceptor.dart:427-450 - 304 resolution
Future<Response<dynamic>?> _resolve304Response(
  RequestOptions requestOptions,
) async {
  try {
    final key = buildCacheKeyWithUserIsolation(
      requestOptions,
      customKeyBuilder: _config.keyBuilder,
    );
    final cachedResponse = await _cacheOptions.store?.get(key);
    if (cachedResponse != null) {
      final response = cachedResponse.toResponse(requestOptions)
        ..statusCode = 200;
      response.extra['acdc_source'] = 'cache';
      _addCacheMetadata(response);
      return response;
    }
  } on Exception catch (_) {}
  return null;
}
```

### 3.3 Response Metadata

The interceptor adds metadata to responses to indicate cache behavior:

| Metadata | Location | Values | Meaning |
|----------|----------|--------|---------|
| `X-ACDC-From-Cache` | Response header | `"true"` | Response was served from cache |
| `acdc_source` | `response.extra` | `"network"`, `"cache"`, `"cache_stale"`, `"network_fresh"` | Source of the response |
| `fromOfflineCache` | `response.extra` | `true` | Response served from stale cache during offline |
| `from_cache` | `response.extra` | `true` | General cache hit indicator |

---

## 4. User Isolation via JWT

### 4.1 JwtUtils

**Source**: `lib/src/cache/jwt_utils.dart`

Extracts user IDs from JWT tokens by checking claims in priority order:

1. `sub` (standard JWT subject claim)
2. `user_id` (common custom claim)
3. `uid` (alternative user ID claim)

```dart
// jwt_utils.dart:27-69
static String? extractUserId(String? token) {
  if (token == null || token.isEmpty) return null;
  try {
    final decodedToken = JwtDecoder.decode(token);
    if (decodedToken.containsKey('sub')) {
      final sub = decodedToken['sub'];
      if (sub != null && sub.toString().isNotEmpty) return sub.toString();
    }
    if (decodedToken.containsKey('user_id')) {
      final userId = decodedToken['user_id'];
      if (userId != null && userId.toString().isNotEmpty) return userId.toString();
    }
    if (decodedToken.containsKey('uid')) {
      final uid = decodedToken['uid'];
      if (uid != null && uid.toString().isNotEmpty) return uid.toString();
    }
    return null;
  } on Exception catch (_) {
    return null;
  }
}
```

Key design decisions:
- JWT signature is **not** verified (that is the auth server's responsibility). Only the payload is decoded.
- Numeric user IDs are converted to strings via `.toString()`.
- Invalid JWTs silently return `null`, disabling caching for that request.

### 4.2 UserIdExtractor

**Source**: `lib/src/security/user_id_extractor.dart`

Encapsulates the full extraction pipeline:

1. Parse `Authorization` header (strip `Bearer ` prefix if present)
2. Try custom `userIdProvider` first (for non-JWT auth systems)
3. Fall back to `JwtUtils.extractUserId()` for JWT tokens

Returns a `UserIdResult` with `hasAuth`, `userId`, and `token` fields.

### 4.3 Cache Key Generation with User Isolation

**Source**: `lib/src/interceptors/cache_interceptor.dart:141-176`

Three cache key strategies based on authentication state:

| Scenario | Cache Key | Behavior |
|----------|-----------|----------|
| **No auth** (public endpoint) | `{baseKey}` | Shared cache -- all users see same data |
| **Auth + user ID found** | `{baseKey}:{userId}` | User-isolated cache -- each user gets own entry |
| **Auth + no user ID** (bad JWT) | `""` (empty) | **No caching** -- security measure to prevent data leakage |

```dart
// cache_interceptor.dart:141-176
static String buildCacheKeyWithUserIsolation(
  RequestOptions options, {
  String Function(RequestOptions)? customKeyBuilder,
}) {
  final baseKey = customKeyBuilder?.call(options) ??
      CacheOptions.defaultCacheKeyBuilder(
        url: Uri.parse(options.uri.toString()),
        headers: options.headers.map((key, value) => MapEntry(key, value.toString())),
        body: options.data,
      );

  final userId = options.headers['X-ACDC-User-Id']?.toString() ??
      options.extra['_acdc_user_id'] as String?;
  final hasAuth = options.extra['_acdc_has_auth'] as bool? ?? false;

  if (!hasAuth && userId == null) return baseKey;           // Shared cache
  if (hasAuth && (userId == null || userId.isEmpty)) return ''; // No caching
  if (userId != null && userId.isNotEmpty) return '$baseKey:$userId'; // Isolated
  return baseKey;
}
```

The user ID is propagated via:
- `X-ACDC-User-Id` request header (for `dio_cache_interceptor`'s `keyBuilder`)
- `options.extra['_acdc_user_id']` (for backward compatibility/testing)
- `options.extra['_acdc_has_auth']` (auth presence flag)

---

## 5. AES-256 Encryption of Cached Data

### 5.1 EncryptedCacheStore

**Source**: `lib/src/cache/encrypted_cache_store.dart`

Wraps a `FileCacheStore` (from `http_cache_file_store`) and encrypts all cached response content using AES-256-GCM.

#### Encryption Details

| Parameter | Value |
|-----------|-------|
| **Algorithm** | AES-GCM (Galois/Counter Mode) |
| **Key Size** | 256 bits (32 bytes) |
| **IV Size** | 12 bytes (standard for GCM) |
| **Key Storage** | `FlutterSecureStorage` (Keychain on iOS, KeyStore on Android) |
| **Key Generation** | `Key.fromSecureRandom(32)` via `encrypt` package |

#### Serialization Format

Encrypted content is stored as: `[IV Length (1 byte)] [IV bytes] [Ciphertext bytes]`

```dart
// encrypted_cache_store.dart:289-303
Uint8List _serializeContent(Uint8List iv, Uint8List ciphertext) {
  final bb = BytesBuilder()
    ..addByte(iv.length)
    ..add(iv)
    ..add(ciphertext);
  return bb.toBytes();
}

({Uint8List iv, Uint8List ciphertext}) _deserializeContent(Uint8List bytes) {
  final ivLength = bytes[0];
  final iv = bytes.sublist(1, 1 + ivLength);
  final ciphertext = bytes.sublist(1 + ivLength);
  return (iv: iv, ciphertext: ciphertext);
}
```

#### Key Management

```dart
// encrypted_cache_store.dart:122-135
static Future<Uint8List> _getOrGenerateKey(FlutterSecureStorage storage) async {
  var base64Key = await storage.read(key: _keyStorageKey);
  if (base64Key == null) {
    final key = Key.fromSecureRandom(32); // 256 bits
    base64Key = base64Url.encode(key.bytes);
    await storage.write(key: _keyStorageKey, value: base64Key);
    return key.bytes;
  }
  return base64Url.decode(base64Key);
}
```

#### Encryption Flow

- **Write** (`set`): Generate random 12-byte IV -> Encrypt content with AES-GCM -> Serialize IV+ciphertext -> Store to FileCacheStore
- **Read** (`get`): Read from FileCacheStore -> Deserialize IV+ciphertext -> Decrypt -> Return plaintext
- **Decryption failure**: Treats as cache miss, deletes corrupted entry silently.

#### Version-Based Invalidation

```dart
// encrypted_cache_store.dart:100-108
final storedVersion = await _secureStorage.read(key: _versionStorageKey);
if (version != null && storedVersion != version) {
  await _fileStore!.clean();
  await _secureStorage.write(key: _versionStorageKey, value: version);
} else if (version != null && storedVersion == null) {
  await _secureStorage.write(key: _versionStorageKey, value: version);
}
```

#### Lazy Initialization

The store uses lazy initialization (`_ensureInitialized()`) to defer directory creation, key generation, and version checking until first use.

---

## 6. Offline Support / Stale-While-Revalidate Patterns

### 6.1 OfflineInterceptor

**Source**: `lib/src/interceptors/offline_interceptor.dart`

Detects offline state using a `NetworkInfo` service and provides fail-fast or cached fallback behavior.

```
Request Flow (Offline Interceptor):

  Request arrives
      |
      v
  force_network? --> YES --> handler.next() (proceed normally)
      |
      NO
      v
  isConnected? --> YES --> handler.next() (proceed normally)
      |
      NO (offline)
      v
  Cache available? --> YES --> handler.resolve(cachedResponse)
      |                         (with fromOfflineCache=true, status=200)
      NO
      v
  failFast? --> YES --> handler.reject(AcdcNetworkException)
      |
      NO --> handler.next() (let Dio/other interceptors handle)
```

Key behaviors:
- Only caches GET and HEAD requests for offline fallback.
- Uses the same `buildCacheKeyWithUserIsolation()` as the cache interceptor to maintain user isolation.
- Allows stale content when offline (sets `statusCode = 200` even for expired cache).
- Adds `fromOfflineCache=true` and `X-ACDC-From-Cache: true` metadata.
- `forceNetwork` flag in `RequestOptions.extra` bypasses offline checks entirely.

```dart
// offline_interceptor.dart:88-125
Future<Response<dynamic>?> _tryGetFromCache(RequestOptions options) async {
  if (options.method != 'GET' && options.method != 'HEAD') return null;
  try {
    final key = AcdcCacheInterceptor.buildCacheKeyWithUserIsolation(
      options, customKeyBuilder: cacheConfig?.keyBuilder,
    );
    if (key.isEmpty) return null;
    final cachedResponse = await cacheStore!.get(key);
    if (cachedResponse != null) {
      final response = cachedResponse.toResponse(options);
      response.extra['fromOfflineCache'] = true;
      response.extra['from_cache'] = true;
      response.headers.add('X-ACDC-From-Cache', 'true');
      response.statusCode = 200;
      return response;
    }
  } on Object catch (_) {}
  return null;
}
```

### 6.2 Stale-While-Revalidate (SWR)

**Source**: `lib/src/interceptors/cache_interceptor.dart:187-247`

When `staleWhileRevalidate: true`, the interceptor:

1. Checks for a cached response for GET requests.
2. If cache hit: Immediately resolves with cached (possibly stale) data.
3. Triggers a background refresh via the `onRefresh` callback.
4. The refresh uses `CachePolicy.refreshForceCache` to update the cache.
5. Prevents infinite SWR loops via `swr_refresh: true` marker in `extra`.

```dart
// cache_interceptor.dart:187-247 (simplified)
if (_config.staleWhileRevalidate && options.method.toUpperCase() == 'GET'
    && options.extra['swr_refresh'] != true) {
  final key = buildCacheKeyWithUserIsolation(options, customKeyBuilder: _config.keyBuilder);
  final cachedResponse = await _cacheOptions.store?.get(key);

  if (cachedResponse != null) {
    // Serve stale immediately
    final response = cachedResponse.toResponse(options)..statusCode = 200;
    response.extra['acdc_source'] = 'cache_stale';
    handler.resolve(response);

    // Trigger background refresh
    final refreshOptions = options.copyWith(
      extra: _cacheOptions.copyWith(policy: CachePolicy.refreshForceCache).toExtra()
        ..['swr_refresh'] = true,
    );
    if (onRefresh != null) {
      Future.microtask(() => onRefresh!(refreshOptions)).catchError((e) {}).ignore();
    }
    return;
  }
}
```

### 6.3 Stale-If-Error

When `staleIfError: true` (default), the `_CacheAwareErrorHandler` detects when `dio_cache_interceptor` serves stale cache during network errors and adds offline metadata:

```dart
// cache_interceptor.dart:582-591
@override
void resolve(Response<dynamic> response) {
  response.extra['fromOfflineCache'] = true;
  response.extra['from_cache'] = true;
  response.extra['acdc_source'] = 'cache';
  response.headers.add('X-ACDC-From-Cache', 'true');
  originalHandler.resolve(response);
}
```

---

## 7. Cache Invalidation Strategies

### 7.1 Mutation-Based Invalidation

POST, PUT, DELETE, and PATCH responses automatically trigger cache invalidation for the request URL:

```dart
// cache_interceptor.dart:300-304
@override
void onResponse(Response<dynamic> response, ResponseInterceptorHandler handler) {
  final method = response.requestOptions.method.toUpperCase();
  if (_isMutationMethod(method)) {
    _invalidateCacheForUrl(response.requestOptions.uri.toString());
  }
  // ...
}
```

### 7.2 Version-Based Invalidation

Changing the `CacheConfig.version` string clears the entire cache on next initialization. This is stored in `FlutterSecureStorage` and compared during `EncryptedCacheStore._initialize()`.

### 7.3 Manual Invalidation via AcdcCacheManager

Accessible via the `dio.cache` extension:

```dart
// Clear all cached data
await dio.cache.clearCache();

// Clear cache for a specific URL
await dio.cache.clearCacheForUrl('https://api.example.com/users');
```

### 7.4 TTL/Stale Expiration

- Standard TTL is driven by HTTP `Cache-Control: max-age` headers.
- `maxStale` allows stale entries to persist up to 7 days for SWR/offline scenarios.
- `clean(staleOnly: true)` removes only expired entries.

---

## 8. AcdcCacheManager

**Source**: `lib/src/cache/acdc_cache_manager.dart`

High-level cache management accessible via `Dio` extension:

```dart
// Extension on Dio
extension AcdcCache on Dio {
  AcdcCacheManager get cache {
    final manager = options.extra['_acdc_cache_manager'] as AcdcCacheManager?;
    if (manager == null) {
      throw StateError('AcdcCacheManager not initialized.');
    }
    return manager;
  }
}
```

The manager is injected during `AcdcClientBuilder.build()` and delegates to `AcdcCacheInterceptor`. It returns a no-op manager when cache is disabled.

---

## 9. Test Coverage Summary

### 9.1 Cache Module Tests

| Test File | Coverage |
|-----------|----------|
| `acdc_cache_manager_test.dart` | Manager accessible via extension, no-op when cache disabled, clearCache/clearCacheForUrl delegation |
| `cache_invalidation_test.dart` | Version change invalidation, first-run version storage, onError callback on storage failures |
| `cache_store_factory_test.dart` | TwoTierCacheStore when inMemory=true, EncryptedCacheStore when inMemory=false |
| `encrypted_cache_store_test.dart` | Store/retrieve, delete, clean, stale-only delete, getFromPath, deleteFromPath, pathExists, close, encryption failure handling |
| `two_tier_cache_store_test.dart` | Memory-first reads, L2-to-L1 promotion, both-tier operations, duplicate merging, graceful persistent failure handling, memory-only mode |
| `jwt_utils_test.dart` | sub/user_id/uid claim extraction, priority order, null/empty/invalid tokens, numeric IDs, isValidJwt, isExpired |
| `user_isolation_test.dart` | User ID extraction from Bearer tokens, unauthenticated marking, invalid JWT handling, custom userIdProvider, provider fallback, cache key generation (isolated/shared/empty), different users produce different keys, same user across token refreshes |

### 9.2 Interceptor Tests

| Test File | Coverage |
|-----------|----------|
| `cache_interceptor_test.dart` | Method-based caching (POST/PUT/DELETE/PATCH invalidation, GET passthrough), SWR integration, cache metadata (X-ACDC-From-Cache, acdc_source), custom key builder with user isolation enforcement, edge cases (empty user ID), logging (cache miss/write/hit) |
| `offline_interceptor_test.dart` | Online passthrough, force_network bypass, offline cache hit with metadata, fail-fast on cache miss, non-fail-fast passthrough, POST requests ignored for cache |
| `swr_offline_test.dart` | SWR offline scenario -- serves from cache with correct acdc_source when network fails |
| `etag_cache_test.dart` | ETag storage from response, If-None-Match header in subsequent requests, 304 Not Modified resolution to 200 with cached content |

---

## 10. C# Porting Considerations

### 10.1 Server-Side vs. Client-Side Caching

The Dart-ACDC caching system is designed for a **mobile/client** context. For a C# ASP.NET Core server-side port, several fundamental differences apply:

| Concern | Dart (Client) | C# (Server) |
|---------|---------------|-------------|
| **Encrypted disk cache** | Required (user device is untrusted) | **Not needed** (server is trusted infrastructure) |
| **Key storage** | FlutterSecureStorage (Keychain/KeyStore) | N/A (no per-device encryption) |
| **Two-tier cache** | Memory + encrypted file | **Memory + Redis** or **IDistributedCache** |
| **Offline support** | Essential (mobile goes offline) | **Not applicable** (server is always online) |
| **User isolation** | Via JWT claim extraction | Via `HttpContext.User.Identity` / `ClaimsPrincipal` |

### 10.2 Package Mapping

| Dart Package | C# Equivalent | Notes |
|-------------|---------------|-------|
| `dio_cache_interceptor` | **FusionCache** or custom `DelegatingHandler` | FusionCache provides L1/L2, SWR, fail-safe. Alternatively, implement as `HttpMessageHandler` in `HttpClient` pipeline |
| `MemCacheStore` | `IMemoryCache` (`Microsoft.Extensions.Caching.Memory`) | Built-in, LRU, size-limited |
| `FileCacheStore` / `EncryptedCacheStore` | **Not needed on server**. If needed: `IDistributedCache` with Redis (`StackExchange.Redis`) or SQLite (`Microsoft.Data.Sqlite`) | For server: Redis is the standard L2. For client (MAUI): LiteDB or SQLite with DPAPI encryption |
| `encrypt` (AES-GCM) | `System.Security.Cryptography.AesGcm` | .NET 6+ has native AES-GCM support |
| `FlutterSecureStorage` | `DPAPI` (Windows), `Keychain` (macOS), `SecretManager` (server) | Server-side: use `IConfiguration` / Azure Key Vault for key management |
| `jwt_decoder` | `System.IdentityModel.Tokens.Jwt` / `JwtSecurityTokenHandler` | Or simply `HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)` on server |
| `path_provider` | `Environment.GetFolderPath()` or `IHostEnvironment.ContentRootPath` | |

### 10.3 Recommended C# Architecture

#### For ASP.NET Core Server (Primary Target)

```
HTTP Pipeline:
  Request --> [CacheMiddleware / DelegatingHandler] --> Backend/DB

Cache Stack:
  L1: IMemoryCache (per-process, fast)
  L2: IDistributedCache (Redis, shared across instances)

User Isolation:
  Cache key = $"{httpMethod}:{url}:{userId}"
  userId from HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
```

**FusionCache** is the recommended library for C# -- it natively supports:
- Two-tier caching (L1 memory + L2 distributed)
- Stale-while-revalidate ("fail-safe" in FusionCache terminology)
- Automatic L2-to-L1 promotion (backplane support)
- Adaptive caching

```csharp
// Equivalent FusionCache setup
services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromHours(1),          // TTL
        FailSafeMaxDuration = TimeSpan.FromDays(7), // staleIfError equivalent
        IsFailSafeEnabled = true,                    // staleIfError
        AllowTimedOutFactoryBackgroundCompletion = true, // SWR equivalent
    })
    .WithMemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 5 * 1024 * 1024, // 5MB L1
    })
    .WithDistributedCache(
        services.BuildServiceProvider().GetRequiredService<IDistributedCache>()
    );
```

#### For .NET MAUI Client (If Applicable)

If porting to a .NET mobile client, the architecture maps more directly:

```csharp
// C# TwoTierCacheStore equivalent
public class TwoTierCache : ICacheStore
{
    private readonly IMemoryCache _l1;
    private readonly LiteDatabase _l2; // or SQLite

    public async Task<CacheEntry?> GetAsync(string key)
    {
        if (_l1.TryGetValue(key, out var entry)) return entry;
        var persisted = await _l2.GetAsync(key);
        if (persisted != null) _l1.Set(key, persisted); // Promote
        return persisted;
    }
}

// C# AES-GCM encryption equivalent
public class EncryptedCacheStore : ICacheStore
{
    private byte[] _key; // From SecureStorage / DPAPI

    public byte[] Encrypt(byte[] plaintext)
    {
        var nonce = new byte[12]; // GCM standard
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(_key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Serialize: [nonce][tag][ciphertext]
        return [..nonce, ..tag, ..ciphertext];
    }
}
```

### 10.4 Cache Key Generation in C#

```csharp
public static class CacheKeyBuilder
{
    public static string Build(HttpRequestMessage request, ClaimsPrincipal? user = null)
    {
        var baseKey = $"{request.Method}:{request.RequestUri}";

        if (user?.Identity?.IsAuthenticated != true)
            return baseKey; // Shared cache

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub")
                  ?? user.FindFirstValue("user_id");

        if (string.IsNullOrEmpty(userId))
            return string.Empty; // No caching (security)

        return $"{baseKey}:{userId}"; // User-isolated
    }
}
```

### 10.5 Key Porting Decisions

1. **Skip EncryptedCacheStore for server**: Server storage is trusted. Use Redis with TLS for network encryption if needed.

2. **Replace OfflineInterceptor**: Server-side has no offline concept. Instead, implement circuit-breaker patterns (Polly) for downstream service calls.

3. **Cache invalidation on mutations**: Implement as ASP.NET Core middleware or action filter that clears related cache keys after POST/PUT/DELETE.

4. **ETag/If-None-Match**: ASP.NET Core has built-in response caching middleware and `[ResponseCache]` attribute. For downstream API calls, implement in a custom `DelegatingHandler`.

5. **SWR pattern**: FusionCache's "fail-safe with background refresh" closely matches the Dart SWR behavior.

6. **Version-based invalidation**: Use cache key prefix with version, or FusionCache's built-in cache clearing.

7. **User isolation approach**: On server, extract user ID from `ClaimsPrincipal` rather than parsing JWT manually. The JWT is already validated by ASP.NET Core authentication middleware.

### 10.6 Dependency Summary

| C# Package | Purpose | NuGet |
|------------|---------|-------|
| `ZiggyCreatures.FusionCache` | L1/L2 cache with SWR + fail-safe | `dotnet add package ZiggyCreatures.FusionCache` |
| `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` | FusionCache JSON serialization | |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | Redis L2 backend | `dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis` |
| `Microsoft.Extensions.Caching.Memory` | Built-in L1 memory cache | Included in framework |
| `System.IdentityModel.Tokens.Jwt` | JWT parsing (if needed outside auth middleware) | `dotnet add package System.IdentityModel.Tokens.Jwt` |
| `Polly` | Circuit breaker / retry (replaces offline interceptor) | `dotnet add package Polly` |

---

## 11. Diagrams

### 11.1 Cache Interceptor Request Flow

```
                 +-----------------------+
                 |   Incoming Request    |
                 +-----------+-----------+
                             |
                    +--------v--------+
                    | Extract User ID |
                    | (JWT / Custom)  |
                    +--------+--------+
                             |
                 +-----------v-----------+
                 | SWR Enabled & GET?    |
                 +----+------------+-----+
                 YES  |            | NO
                      v            v
              +-------+------+   +--+--+
              | Check Cache  |   | Delegate to         |
              | (by user key)|   | dio_cache_interceptor|
              +--+------+----+   +-----+-----+
              HIT|      |MISS          |
                 v      v              v
          +------+    +-+-----+   (Standard cache
          |Resolve|   |Forward|    lookup / network)
          |Stale  |   |to Net |
          +--+----+   +-------+
             |
             v
      +------+--------+
      | Background     |
      | Refresh (async)|
      +----------------+
```

### 11.2 Cache Key Decision Tree

```
Has Authorization header?
  |
  NO --> baseKey (shared cache)
  |
  YES --> Can extract user ID?
           |
           YES --> baseKey:userId (user-isolated)
           |
           NO --> "" (empty key = NO CACHING, security measure)
```
