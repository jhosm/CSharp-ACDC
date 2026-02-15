# CSharp-ACDC Samples

Four sample projects demonstrating CSharp-ACDC features, from minimal to full-featured.

## BasicUsage

Zero-config HTTP client. Makes a GET request to httpbin.org.

```bash
dotnet run --project samples/BasicUsage
```

**Expected output**: HTTP 200 response with JSON body from httpbin.org.

## AuthenticatedClient

OAuth 2.1 authentication with token seeding, automatic Bearer injection, and logout.

```bash
dotnet run --project samples/AuthenticatedClient
```

**Note**: This sample targets `https://auth.example.com` and `https://api.example.com`, which are placeholder URLs. It will fail with a network error. To use it, replace the URLs with a real OAuth 2.1 provider and API endpoint, and supply valid tokens.

## CachedClient

Response caching with stale-while-revalidate, fail-safe, and ETag support.

```bash
dotnet run --project samples/CachedClient
```

**Expected output**: First request shows a cache miss, second request shows a cache hit from the in-memory cache. Uses httpbin.org.

## FullPipeline

All features enabled: auth, caching, logging (at Debug level), cancellation, and deduplication.

```bash
dotnet run --project samples/FullPipeline
```

**Note**: This sample targets `https://api.example.com` and `https://auth.example.com`, which are placeholder URLs. It will fail with a network error. To use it, replace the URLs with real endpoints and supply valid tokens.

## Prerequisites

- .NET 10 SDK
- Internet access (BasicUsage and CachedClient call httpbin.org)
