# Capability: Documentation

## ADDED Requirements

### Requirement: README Documentation

The project SHALL provide a `README.md` at the repository root with comprehensive library documentation. The README SHALL include: project overview and motivation, installation instructions, a quick start guide demonstrating zero-config usage in 3 lines of code, configuration examples for authenticated client and cached client and full pipeline, a handler pipeline diagram showing handler execution order, a configuration reference listing all `IOptions<T>` settings with their defaults, an exception handling guide describing the exception hierarchy and when to catch each type, and contributing guidelines.

#### Scenario: README exists at repository root

- **WHEN** the repository root is inspected
- **THEN** a `README.md` file SHALL exist

#### Scenario: Installation instructions are present

- **WHEN** a developer reads `README.md`
- **THEN** it SHALL contain installation instructions showing `dotnet add package CSharpAcdc`
- **AND** it SHALL specify the minimum required .NET version (net10.0)

#### Scenario: Quick start demonstrates zero-config usage

- **WHEN** a developer reads the quick start section of `README.md`
- **THEN** it SHALL show a working example that configures and uses CSharpAcdc in 3 lines of code or fewer
- **AND** the example SHALL use `IHttpClientFactory` via dependency injection

#### Scenario: Handler pipeline diagram is included

- **WHEN** a developer reads `README.md`
- **THEN** it SHALL contain a diagram showing the handler execution order: Logging, Error, Cancellation, Auth, Cache, Custom, Deduplication
- **AND** the diagram SHALL indicate that handler order is critical

#### Scenario: Configuration reference covers all options

- **WHEN** a developer reads the configuration reference section of `README.md`
- **THEN** it SHALL list every `IOptions<T>` configuration class
- **AND** each configuration option SHALL show its default value and a description

#### Scenario: Exception handling guide describes hierarchy

- **WHEN** a developer reads the exception handling guide in `README.md`
- **THEN** it SHALL show the full exception hierarchy (`AcdcException` and its subtypes)
- **AND** it SHALL describe when each exception type is thrown (HTTP status codes, network errors, cache failures)
- **AND** it SHALL provide code examples showing how to catch and handle each exception type

---

### Requirement: Sample Projects

The project SHALL provide runnable sample projects under a `samples/` directory demonstrating basic usage, authenticated client with OAuth 2.1 token refresh, cached client with FusionCache and Redis L2, and full pipeline configuration with all features enabled. Each sample SHALL be a standalone .NET console application that compiles and runs independently.

#### Scenario: Basic usage sample exists

- **WHEN** the `samples/BasicUsage/` directory is inspected
- **THEN** it SHALL contain `BasicUsage.csproj` and `Program.cs`
- **AND** `Program.cs` SHALL demonstrate zero-config CSharpAcdc usage with `IHttpClientFactory`
- **AND** `dotnet build samples/BasicUsage/` SHALL succeed

#### Scenario: Authenticated client sample exists

- **WHEN** the `samples/AuthenticatedClient/` directory is inspected
- **THEN** it SHALL contain `AuthenticatedClient.csproj` and `Program.cs`
- **AND** `Program.cs` SHALL demonstrate configuring an `ITokenProvider` with OAuth 2.1 token refresh
- **AND** `dotnet build samples/AuthenticatedClient/` SHALL succeed

#### Scenario: Cached client sample exists

- **WHEN** the `samples/CachedClient/` directory is inspected
- **THEN** it SHALL contain `CachedClient.csproj` and `Program.cs`
- **AND** `Program.cs` SHALL demonstrate configuring FusionCache with `IMemoryCache` L1 and `IDistributedCache` Redis L2
- **AND** `dotnet build samples/CachedClient/` SHALL succeed

#### Scenario: Full pipeline sample exists

- **WHEN** the `samples/FullPipeline/` directory is inspected
- **THEN** it SHALL contain `FullPipeline.csproj` and `Program.cs`
- **AND** `Program.cs` SHALL demonstrate configuring all handlers (logging, error, cancellation, auth, cache, deduplication) with all available options
- **AND** `dotnet build samples/FullPipeline/` SHALL succeed

#### Scenario: All samples reference the library project

- **WHEN** any sample `.csproj` file is inspected
- **THEN** it SHALL contain a `<ProjectReference>` to the `src/CSharpAcdc/CSharpAcdc.csproj` library project

---

### Requirement: XML Documentation

All public types and members in the `CSharpAcdc` library SHALL have XML doc comments with at minimum a `<summary>` tag. This includes public classes, interfaces, records, enums, methods, properties, and enum values.

#### Scenario: Public classes have XML doc comments

- **WHEN** any public class in the `CSharpAcdc` namespace is inspected
- **THEN** it SHALL have an XML doc comment with a `<summary>` tag describing its purpose

#### Scenario: Public methods have XML doc comments

- **WHEN** any public method in the `CSharpAcdc` namespace is inspected
- **THEN** it SHALL have an XML doc comment with a `<summary>` tag
- **AND** each parameter SHALL have a `<param>` tag
- **AND** methods with return values SHALL have a `<returns>` tag

#### Scenario: Public interfaces have XML doc comments

- **WHEN** any public interface in the `CSharpAcdc` namespace is inspected
- **THEN** it SHALL have an XML doc comment with a `<summary>` tag describing the contract it defines

#### Scenario: Build enforces documentation

- **WHEN** the library project is built with `TreatWarningsAsErrors` enabled
- **THEN** missing XML doc comments on public members SHALL produce build warnings (CS1591) that are promoted to errors
- **AND** the `CSharpAcdc.csproj` SHALL set `<GenerateDocumentationFile>true</GenerateDocumentationFile>`

---

### Requirement: Changelog

The project SHALL maintain a `CHANGELOG.md` file at the repository root following the Keep a Changelog format (https://keepachangelog.com). The changelog SHALL document all notable changes for each released version.

#### Scenario: CHANGELOG.md exists with initial entry

- **WHEN** the repository root is inspected
- **THEN** a `CHANGELOG.md` file SHALL exist
- **AND** it SHALL contain an entry for version 1.0.0
- **AND** it SHALL follow the Keep a Changelog format with sections for Added, Changed, Deprecated, Removed, Fixed, and Security as applicable

#### Scenario: Changelog uses semantic versioning

- **WHEN** `CHANGELOG.md` is inspected
- **THEN** all version numbers SHALL follow Semantic Versioning 2.0.0 (MAJOR.MINOR.PATCH)

#### Scenario: Changelog has comparison links

- **WHEN** `CHANGELOG.md` is inspected
- **THEN** each version entry SHALL include a date in YYYY-MM-DD format
- **AND** the file SHALL include a link format for comparing versions in the repository

---

### Requirement: Migration Guide

The `README.md` SHALL include a migration guide section for developers transitioning from raw `HttpClient` / `IHttpClientFactory` usage to CSharpAcdc. The guide SHALL show before/after code comparisons and explain the benefits of each CSharpAcdc feature over manual implementation.

#### Scenario: Migration guide shows before/after examples

- **WHEN** a developer reads the migration guide section of `README.md`
- **THEN** it SHALL contain at least one before/after code comparison showing raw `HttpClient` code alongside the equivalent CSharpAcdc code

#### Scenario: Migration guide covers authentication migration

- **WHEN** a developer reads the migration guide
- **THEN** it SHALL explain how to replace manual token management (storing tokens, refreshing on 401, retry logic) with CSharpAcdc's `ITokenProvider` and `AuthHandler`

#### Scenario: Migration guide covers error handling migration

- **WHEN** a developer reads the migration guide
- **THEN** it SHALL explain how to replace manual HTTP status code checking with CSharpAcdc's typed exception hierarchy (`AcdcAuthException`, `AcdcClientException`, `AcdcServerException`, etc.)
