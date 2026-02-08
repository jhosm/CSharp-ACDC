# Change: Add Documentation and Runnable Sample Projects

## Why

Library consumers need comprehensive documentation to understand installation, configuration, exception handling, and migration from raw `HttpClient`. Without clear docs and runnable examples, adoption requires reading source code. Runnable sample projects provide copy-paste starting points that reduce time-to-first-request from hours to minutes.

## What Changes

- **`README.md`** — Comprehensive library documentation covering:
  - Project overview and motivation
  - Installation instructions (`dotnet add package CSharpAcdc`)
  - Quick start: zero-config usage (3 lines of code)
  - Configuration examples: authenticated client, cached client, full pipeline
  - Handler pipeline explanation with ASCII diagram
  - Configuration reference (all `IOptions<T>` settings with defaults)
  - Exception handling guide (which exceptions to catch, when)
  - Migration guide from raw `HttpClient` to CSharpAcdc
  - Contributing guidelines
- **`samples/BasicUsage/`** — Minimal zero-config example project
  - `BasicUsage.csproj` and `Program.cs`
- **`samples/AuthenticatedClient/`** — OAuth 2.1 token refresh example project
  - `AuthenticatedClient.csproj` and `Program.cs`
- **`samples/CachedClient/`** — FusionCache with Redis L2 example project
  - `CachedClient.csproj` and `Program.cs`
- **`samples/FullPipeline/`** — Complete configuration with all features
  - `FullPipeline.csproj` and `Program.cs`
- **`CHANGELOG.md`** — Version history following Keep a Changelog format (initial 1.0.0 entry)
- **XML doc comments audit** — Ensure all public types and members have `<summary>` documentation

## Impact

- **Affected specs:** documentation (new capability)
- **Depends on:** P7 (builder/DI must exist for examples to reference the API surface)
- **Parallel with:** P8 (integration tests), P11 (CI/CD)
- **Affected code:** `README.md`, `CHANGELOG.md`, `samples/BasicUsage/`, `samples/AuthenticatedClient/`, `samples/CachedClient/`, `samples/FullPipeline/`
- **Files to be created:**
  - `README.md`
  - `CHANGELOG.md`
  - `samples/BasicUsage/Program.cs`
  - `samples/BasicUsage/BasicUsage.csproj`
  - `samples/AuthenticatedClient/Program.cs`
  - `samples/AuthenticatedClient/AuthenticatedClient.csproj`
  - `samples/CachedClient/Program.cs`
  - `samples/CachedClient/CachedClient.csproj`
  - `samples/FullPipeline/Program.cs`
  - `samples/FullPipeline/FullPipeline.csproj`
