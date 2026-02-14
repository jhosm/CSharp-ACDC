# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [1.0.0] - 2026-02-14

### Added

- DelegatingHandler pipeline: Logging, Error, Cancellation, Auth, Cache, Deduplication
- OAuth 2.1 token refresh with proactive and reactive strategies
- Concurrent refresh queue with leader/follower pattern (single refresh for N simultaneous 401s)
- Exponential backoff for transient auth failures (1s-30s clamped)
- FusionCache integration with ETag/If-None-Match revalidation
- Stale-while-revalidate support via FusionCache soft timeout
- Cache mutation invalidation (POST/PUT/DELETE clear related GET caches)
- User-isolated cache keys via JWT `sub` claim extraction
- Typed exception hierarchy: AcdcAuthException, AcdcClientException, AcdcServerException, AcdcNetworkException, AcdcCacheException
- URL redaction and response body truncation in exceptions
- Fluent builder API with progressive disclosure (`AcdcClientBuilder`)
- IHttpClientFactory integration with DI composition root
- Keyed service support for multiple named clients
- IConfiguration binding for appsettings.json configuration
- Structured logging with sensitive data redaction
- Slow request and large payload warnings
- Bulk request cancellation via `CancelAll()`
- GET request deduplication
- Per-request option overrides (SkipCache, SkipAuth, CacheMaxAge)
