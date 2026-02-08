# Capability: Solution Scaffold

## ADDED Requirements

### Requirement: Git Ignore Rules

The project SHALL include a `.gitignore` file at the repository root that prevents build artifacts, IDE files, and local-only files from being committed to version control.

#### Scenario: .NET build artifacts are ignored

- **WHEN** `.gitignore` is inspected
- **THEN** it SHALL exclude `bin/`, `obj/`, `*.user`, `*.suo`, and `*.DotSettings.user`

#### Scenario: IDE files are ignored

- **WHEN** `.gitignore` is inspected
- **THEN** it SHALL exclude `.vs/`, `.idea/`, and `*.sln.DotSettings`

#### Scenario: Local-only files are ignored

- **WHEN** `.gitignore` is inspected
- **THEN** it SHALL exclude `.claude/settings.local.json`, `.env`, and `.env.*`

#### Scenario: OS files are ignored

- **WHEN** `.gitignore` is inspected
- **THEN** it SHALL exclude `.DS_Store` and `Thumbs.db`

---

### Requirement: Solution Structure

The project SHALL provide a .NET solution file (`CSharp-ACDC.sln`) that organizes source and test projects into a standard layout with solution folders. The solution SHALL contain exactly one library project (`src/CSharpAcdc/CSharpAcdc.csproj`), one unit test project (`tests/CSharpAcdc.Tests/CSharpAcdc.Tests.csproj`), and one integration test project (`tests/CSharpAcdc.IntegrationTests/CSharpAcdc.IntegrationTests.csproj`).

#### Scenario: Solution file references all projects

- **WHEN** the solution file `CSharp-ACDC.sln` is opened
- **THEN** it SHALL contain project references to `src/CSharpAcdc/CSharpAcdc.csproj`, `tests/CSharpAcdc.Tests/CSharpAcdc.Tests.csproj`, and `tests/CSharpAcdc.IntegrationTests/CSharpAcdc.IntegrationTests.csproj`

#### Scenario: Projects are organized in solution folders

- **WHEN** `dotnet sln list` is run
- **THEN** the library project SHALL appear under a `src` solution folder
- **AND** both test projects SHALL appear under a `tests` solution folder

#### Scenario: Library project targets net8.0

- **WHEN** the library project `CSharpAcdc.csproj` is inspected
- **THEN** it SHALL target `net8.0`
- **AND** it SHALL have `OutputType` of `Library` (default)

#### Scenario: Test projects reference the library

- **WHEN** the test projects are inspected
- **THEN** each test project SHALL contain a `<ProjectReference>` to `src/CSharpAcdc/CSharpAcdc.csproj`

---

### Requirement: Central Package Management

The project SHALL use `Directory.Packages.props` for centralized NuGet package version management. All package versions MUST be declared in this single file. Individual `.csproj` files SHALL use `<PackageReference Include="..." />` without specifying `Version` attributes.

#### Scenario: All package versions are centralized

- **WHEN** any `.csproj` file in the solution is inspected
- **THEN** no `<PackageReference>` element SHALL have a `Version` attribute
- **AND** every package referenced in any `.csproj` SHALL have a corresponding `<PackageVersion>` entry in `Directory.Packages.props`

#### Scenario: Central package management is enabled

- **WHEN** `Directory.Packages.props` is inspected
- **THEN** it SHALL contain `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`

#### Scenario: Library packages are declared

- **WHEN** `Directory.Packages.props` is inspected
- **THEN** it SHALL declare versions for: `Microsoft.Extensions.Http`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `ZiggyCreatures.FusionCache`, `ZiggyCreatures.FusionCache.Serialization.SystemTextJson`, `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis`, and `System.IdentityModel.Tokens.Jwt`

#### Scenario: Test packages are declared

- **WHEN** `Directory.Packages.props` is inspected
- **THEN** it SHALL declare versions for: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `RichardSzalay.MockHttp`, `WireMock.Net`, `FluentAssertions`, `Microsoft.AspNetCore.Mvc.Testing`, and `coverlet.collector`

---

### Requirement: Build Configuration

The project SHALL use `Directory.Build.props` for shared build settings. All projects in the solution MUST inherit these settings without needing to redeclare them in individual `.csproj` files.

#### Scenario: C# language version is set

- **WHEN** `Directory.Build.props` is inspected
- **THEN** it SHALL set `<LangVersion>12</LangVersion>` to explicitly target C# 12

#### Scenario: Nullable reference types are enabled

- **WHEN** any project in the solution is built
- **THEN** nullable reference types SHALL be enabled (`<Nullable>enable</Nullable>`)

#### Scenario: Implicit usings are enabled

- **WHEN** any project in the solution is built
- **THEN** implicit usings SHALL be enabled (`<ImplicitUsings>enable</ImplicitUsings>`)

#### Scenario: Warnings are treated as errors

- **WHEN** any project in the solution is built
- **THEN** all warnings SHALL be treated as errors (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)

#### Scenario: Code style is enforced at build time

- **WHEN** `Directory.Build.props` is inspected
- **THEN** it SHALL set `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` so that `.editorconfig` style rules are evaluated during `dotnet build`, not only in IDEs

#### Scenario: Target framework is net8.0

- **WHEN** `Directory.Build.props` is inspected
- **THEN** it SHALL set `<TargetFramework>net8.0</TargetFramework>` as the default for all projects

---

### Requirement: SDK Version Pinning

The project SHALL pin the .NET SDK version via a `global.json` file at the solution root to ensure reproducible builds across all development environments and CI systems.

#### Scenario: global.json specifies SDK version

- **WHEN** `global.json` is inspected
- **THEN** it SHALL specify a `sdk.version` targeting .NET 8 (e.g., `8.0.xxx`)
- **AND** it SHALL specify a `rollForward` policy of `latestFeature` to allow patch-level updates within the feature band

#### Scenario: SDK version is respected by dotnet CLI

- **WHEN** `dotnet --version` is run in the repository root
- **THEN** the reported SDK version SHALL match the constraints in `global.json`

---

### Requirement: Code Style

The project SHALL enforce code style conventions via an `.editorconfig` file at the solution root. The style rules MUST match the conventions documented in the project (file-scoped namespaces, C# 12 features).

#### Scenario: File-scoped namespaces are enforced

- **WHEN** `.editorconfig` is inspected
- **THEN** it SHALL set `csharp_style_namespace_declarations = file_scoped:warning` (or stricter)

#### Scenario: Indentation settings are defined

- **WHEN** `.editorconfig` is inspected
- **THEN** it SHALL define `indent_style = space` and `indent_size = 4` for C# files

#### Scenario: Naming conventions are defined

- **WHEN** `.editorconfig` is inspected
- **THEN** it SHALL define naming rules for public members (PascalCase), private fields (`_camelCase` with underscore prefix), and constants (PascalCase)

---

### Requirement: Project Structure

The library project SHALL organize code into namespace-aligned directories that reflect the module boundaries of the CSharp-ACDC library. Each directory SHALL contain a `.gitkeep` file to ensure it is tracked by Git before any source files are added.

#### Scenario: All module directories exist

- **WHEN** the `src/CSharpAcdc/` directory is inspected
- **THEN** it SHALL contain the following subdirectories: `Exceptions/`, `Handlers/`, `Auth/`, `Cache/`, `Logging/`, `Configuration/`, `Extensions/`, `Builder/`, `Client/`

#### Scenario: Empty directories are tracked by Git

- **WHEN** any of the module directories is inspected
- **THEN** it SHALL contain a `.gitkeep` file to ensure Git tracks the empty directory

---

### Requirement: Test Infrastructure

The solution SHALL include separate unit test and integration test projects. Both test projects SHALL have all required test packages pre-referenced so that subsequent proposals can add test classes without modifying `.csproj` files.

#### Scenario: Unit test project has required packages

- **WHEN** `tests/CSharpAcdc.Tests/CSharpAcdc.Tests.csproj` is inspected
- **THEN** it SHALL reference: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `RichardSzalay.MockHttp`, `FluentAssertions`, and `coverlet.collector`

#### Scenario: Integration test project has required packages

- **WHEN** `tests/CSharpAcdc.IntegrationTests/CSharpAcdc.IntegrationTests.csproj` is inspected
- **THEN** it SHALL reference: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `WireMock.Net`, `FluentAssertions`, `Microsoft.AspNetCore.Mvc.Testing`, and `coverlet.collector`

#### Scenario: Test infrastructure compiles without test classes

- **WHEN** `dotnet build` is run on the solution with no test classes present
- **THEN** the build SHALL succeed with zero errors

#### Scenario: Test runner executes with no tests

- **WHEN** `dotnet test` is run on the solution with no test classes present
- **THEN** the command SHALL complete without error (exit code 0 with no test assemblies discovered is acceptable)
