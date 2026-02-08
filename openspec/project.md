# Project Context

## Purpose
CSharp-ACDC is a **server-only** C# port of Dart-ACDC — a production HTTP client library providing authentication, caching, logging, and structured error handling. It targets ASP.NET Core / .NET 10 server environments.

## Tech Stack
- C# 14, .NET 10, ASP.NET Core
- `IHttpClientFactory` with `DelegatingHandler` pipeline
- FusionCache (`IMemoryCache` L1 + `IDistributedCache` Redis L2)
- `System.IdentityModel.Tokens.Jwt` for JWT handling
- xUnit + NSubstitute + FluentAssertions for testing
- WireMock.Net + RichardSzalay.MockHttp for HTTP mocking

## Project Conventions

### Code Style
- File-scoped namespaces, nullable reference types enabled
- `record` types for DTOs and immutable config
- `IOptions<T>` pattern for configuration
- `ILogger<T>` for structured logging
- Async all the way — no `.Result` or `.Wait()`
- Root namespace: `CSharpAcdc`

### Architecture Patterns
- Dio interceptors map to `DelegatingHandler` pipeline via `IHttpClientFactory`
- Handler order is critical: Logging → Error → Cancellation → Auth → Cache → Custom → Dedup
- All handlers must be thread-safe (C# server handles concurrent requests, unlike single-threaded Dart)
- Per-request metadata via `HttpRequestMessage.Options` (not instance fields)

### Testing Strategy
- Unit tests in `tests/CSharpAcdc.Tests/` (xUnit)
- Integration tests in `tests/CSharpAcdc.IntegrationTests/` (WireMock.Net)
- Thread-safety tests for all concurrent components
- Mock HTTP via `MockHttpMessageHandler` or `RichardSzalay.MockHttp`

### Git Workflow
- Main branch: `main`
- Feature branches for each proposal
- PR-based workflow with CI checks

## Domain Context
This is a port of an existing Dart library (Dart-ACDC). Detailed analysis of the Dart source lives in `research/`. Server-only features are excluded (no offline support, no mobile storage, no connectivity monitoring). Thread safety is the #1 concern since Dart is single-threaded but C# servers handle concurrent requests.

## Important Constraints
- Server-only: no mobile/desktop targets
- `IHttpClientFactory` is required (never `new HttpClient()`)
- `DelegatingHandler` instances are pooled (2-min default lifetime) — no per-request state in instance fields
- `HttpRequestMessage` cannot be sent twice — must clone before retry

## External Dependencies
- NuGet packages (all pre-loaded in scaffold): Microsoft.Extensions.Http, FusionCache, System.IdentityModel.Tokens.Jwt
- Redis (optional L2 cache via `IDistributedCache`)
- OAuth 2.1 token endpoints (for auth refresh)
