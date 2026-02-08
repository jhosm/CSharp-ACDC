# Tasks: Add .NET Solution Scaffold

## 1. Solution Setup

- [x] 1.1 Create `.gitignore` with rules for .NET build artifacts (`bin/`, `obj/`, `*.user`, `.vs/`, `.idea/`), local-only files (`.claude/settings.local.json`, `.env`), and OS files (`.DS_Store`, `Thumbs.db`)
- [x] 1.2 Create `global.json` with .NET 10 SDK version pin and `latestFeature` rollForward policy
- [x] 1.3 Create `Directory.Build.props` with shared build settings (net10.0, C# 14 LangVersion, nullable enable, implicit usings, TreatWarningsAsErrors, EnforceCodeStyleInBuild)
- [x] 1.4 Create `Directory.Packages.props` with central package version management for all library and test NuGet packages (use latest stable versions at implementation time)
- [x] 1.5 Create `.editorconfig` with code style rules (file-scoped namespaces, expression-body preferences, naming conventions with `_camelCase` for private fields)

## 2. Library Project

- [x] 2.1 Create `src/CSharpAcdc/CSharpAcdc.csproj` with PackageReference entries for all library packages (Microsoft.Extensions.Http, Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, ZiggyCreatures.FusionCache, ZiggyCreatures.FusionCache.Serialization.SystemTextJson, ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis, System.IdentityModel.Tokens.Jwt)
- [x] 2.2 Create directory skeleton under `src/CSharpAcdc/` with `.gitkeep` files: Exceptions/, Handlers/, Auth/, Cache/, Logging/, Configuration/, Extensions/, Builder/, Client/

## 3. Test Projects

- [x] 3.1 Create `tests/CSharpAcdc.Tests/CSharpAcdc.Tests.csproj` with xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, NSubstitute, FluentAssertions, RichardSzalay.MockHttp, coverlet.collector, and project reference to CSharpAcdc
- [x] 3.2 Create `tests/CSharpAcdc.IntegrationTests/CSharpAcdc.IntegrationTests.csproj` with xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, WireMock.Net, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing, coverlet.collector, and project reference to CSharpAcdc

## 4. Solution File

- [x] 4.1 Create `CSharp-ACDC.sln` solution file with `src` and `tests` solution folders
- [x] 4.2 Add all three projects to the solution under their respective solution folders

## 5. Verification

- [x] 5.1 Verify `dotnet restore` succeeds (all packages resolve)
- [x] 5.2 Verify `dotnet build` succeeds with zero warnings
- [x] 5.3 Verify `dotnet test` completes without error (no tests yet — exit code 0 with no test assemblies discovered is acceptable)
- [x] 5.4 Verify `dotnet sln list` shows all three projects under correct solution folders
