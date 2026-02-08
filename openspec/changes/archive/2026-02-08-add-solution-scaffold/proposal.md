# Change: Add .NET Solution Scaffold with All NuGet Packages

## Why

The CSharp-ACDC library needs a .NET solution structure before any code can be written. This is the P1 foundation proposal. Front-loading ALL NuGet packages into the project files eliminates `.csproj` as a merge conflict source for all subsequent proposals, enabling parallel development across 10+ feature branches.

## What Changes

- **`.gitignore`** — Git ignore rules for .NET build artifacts, IDE files, and local-only files (`.claude/settings.local.json`, `.env`, etc.)
- **`CSharp-ACDC.sln`** — Solution file organizing source and test projects into `src` and `tests` solution folders
- **`src/CSharpAcdc/CSharpAcdc.csproj`** — net10.0 class library with nullable enabled and all NuGet package references
- **`tests/CSharpAcdc.Tests/CSharpAcdc.Tests.csproj`** — xUnit unit test project with NSubstitute, FluentAssertions, MockHttp, and all test packages
- **`tests/CSharpAcdc.IntegrationTests/CSharpAcdc.IntegrationTests.csproj`** — Integration test project for WireMock.Net tests requiring longer timeouts
- **`Directory.Build.props`** — Shared build settings (TFM net10.0, C# 14, nullable enabled, implicit usings, TreatWarningsAsErrors)
- **`Directory.Packages.props`** — Central package version management for all NuGet dependencies
- **`.editorconfig`** — Code style rules matching project conventions (file-scoped namespaces, etc.)
- **`global.json`** — .NET SDK version pin for reproducible builds
- **Directory skeleton** with `.gitkeep` files for namespace-aligned folders

### Pre-loaded NuGet packages (library `CSharpAcdc.csproj`):

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.Http` | `IHttpClientFactory` and `DelegatingHandler` pipeline |
| `Microsoft.Extensions.Caching.Memory` | `IMemoryCache` for L1 cache |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | `IDistributedCache` Redis L2 cache |
| `Microsoft.Extensions.Logging.Abstractions` | `ILogger<T>` structured logging |
| `Microsoft.Extensions.Options` | `IOptions<T>` configuration pattern |
| `ZiggyCreatures.FusionCache` | Two-tier cache (L1 memory + L2 Redis) |
| `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` | FusionCache JSON serialization |
| `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis` | FusionCache Redis backplane for L1 cache synchronization across instances |
| `System.IdentityModel.Tokens.Jwt` | JWT token parsing and validation |

### Pre-loaded NuGet packages (test projects):

| Package | Purpose |
|---------|---------|
| `xunit` | Test framework |
| `xunit.runner.visualstudio` | Test runner |
| `Microsoft.NET.Test.Sdk` | Test SDK |
| `NSubstitute` | Mocking framework |
| `RichardSzalay.MockHttp` | HTTP message handler mocking |
| `WireMock.Net` | Integration test HTTP server |
| `FluentAssertions` | Assertion library |
| `Microsoft.AspNetCore.Mvc.Testing` | In-process test server with `WebApplicationFactory<T>` for DI-aware integration tests |
| `coverlet.collector` | Code coverage |

### Directory skeleton (`src/CSharpAcdc/`):

```
Exceptions/     — AcdcException hierarchy
Handlers/       — DelegatingHandler implementations
Auth/           — TokenProvider, refresh strategy, backoff
Cache/          — FusionCache integration, SWR
Logging/        — Structured logging utilities
Configuration/  — IOptions<T> configuration records
Extensions/     — IServiceCollection extension methods
Builder/        — Fluent builder for HttpClient configuration
Client/         — High-level client abstraction
```

## Impact

- **Affected specs:** solution-scaffold (new capability)
- **Affected code:** Entire solution structure (all new files, no modifications to existing)
- **Enables:** All subsequent proposals (P2-P11) can develop in parallel without `.csproj` merge conflicts
