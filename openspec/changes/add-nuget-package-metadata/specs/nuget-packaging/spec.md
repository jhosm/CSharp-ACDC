# Capability: NuGet Packaging

## ADDED Requirements

### Requirement: Package Metadata

The NuGet package SHALL include the following metadata properties: `PackageId` (set to `CSharpAcdc`), `Description`, `Authors`, `PackageLicenseExpression` (set to `MIT`), `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType` (set to `git`), `PackageTags`, `PackageReadmeFile`, and `Copyright`. All metadata properties SHALL be declared in `Directory.Build.props` so they apply to all packable projects in the solution.

#### Scenario: Package contains required metadata fields

- **WHEN** `dotnet pack` is run and the resulting `.nupkg` is inspected
- **THEN** the package SHALL contain `PackageId` set to `CSharpAcdc`
- **AND** the package SHALL contain a non-empty `Description`
- **AND** the package SHALL contain a non-empty `Authors`
- **AND** the package SHALL contain `PackageLicenseExpression` set to `MIT`
- **AND** the package SHALL contain `RepositoryUrl` pointing to the GitHub repository
- **AND** the package SHALL contain `RepositoryType` set to `git`
- **AND** the package SHALL contain `Copyright` with a valid copyright notice

#### Scenario: Package includes tags for discoverability

- **WHEN** the `.nupkg` metadata is inspected
- **THEN** `PackageTags` SHALL include at minimum: `http-client`, `authentication`, `caching`, `delegating-handler`, `aspnetcore`

#### Scenario: Package includes README

- **WHEN** the `.nupkg` is inspected
- **THEN** it SHALL contain the `README.md` file
- **AND** `PackageReadmeFile` SHALL reference this file so NuGet.org renders it on the package page

---

### Requirement: Source Link

The package SHALL include Source Link configuration via `Microsoft.SourceLink.GitHub`, enabling consumers to step into library source code during debugging. The Source Link package SHALL be referenced with `PrivateAssets="All"` so it does not become a transitive dependency of consumers.

#### Scenario: Source Link is configured in build props

- **WHEN** `Directory.Build.props` is inspected
- **THEN** it SHALL contain `<EmbedUntrackedSources>true</EmbedUntrackedSources>`

#### Scenario: Source Link package is declared in central package management

- **WHEN** `Directory.Packages.props` is inspected
- **THEN** it SHALL declare a `<PackageVersion>` entry for `Microsoft.SourceLink.GitHub`

#### Scenario: Source Link does not leak as transitive dependency

- **WHEN** a consumer project references the `CSharpAcdc` NuGet package
- **THEN** `Microsoft.SourceLink.GitHub` SHALL NOT appear as a transitive dependency of the consumer

---

### Requirement: Symbol Package

The package SHALL produce a `.snupkg` symbol package alongside the `.nupkg`. Symbol packages enable consumers to debug into library code via NuGet.org's symbol server without requiring a local source checkout.

#### Scenario: Symbol package is generated

- **WHEN** `dotnet pack` is run
- **THEN** it SHALL produce both a `.nupkg` file and a `.snupkg` file in the output directory

#### Scenario: Symbol package format is configured

- **WHEN** `Directory.Build.props` is inspected
- **THEN** it SHALL contain `<IncludeSymbols>true</IncludeSymbols>`
- **AND** it SHALL contain `<SymbolPackageFormat>snupkg</SymbolPackageFormat>`

---

### Requirement: XML Documentation

The package SHALL include generated XML documentation files so that consumers get IntelliSense tooltips and documentation for all public API members.

#### Scenario: XML documentation generation is enabled

- **WHEN** `Directory.Build.props` is inspected
- **THEN** it SHALL contain `<GenerateDocumentationFile>true</GenerateDocumentationFile>`

#### Scenario: XML documentation is included in the package

- **WHEN** `dotnet pack` is run and the resulting `.nupkg` is inspected
- **THEN** the package SHALL contain the XML documentation file alongside the library assembly

---

### Requirement: License

The repository SHALL include a `LICENSE` file at the repository root containing the full MIT license text. The NuGet package metadata SHALL reference this license via the `PackageLicenseExpression` property set to `MIT`.

#### Scenario: LICENSE file exists at repository root

- **WHEN** the repository root is inspected
- **THEN** a `LICENSE` file SHALL exist
- **AND** its contents SHALL be a valid MIT license text

#### Scenario: Package metadata references the license

- **WHEN** `Directory.Build.props` is inspected
- **THEN** `PackageLicenseExpression` SHALL be set to `MIT`
- **AND** `PackageLicenseFile` SHALL NOT be used (since MIT is a well-known SPDX identifier, the expression form is preferred)

---

### Requirement: Package Validation

Running `dotnet pack` SHALL produce a valid `.nupkg` with correct metadata, no packaging warnings, and all expected artifacts (symbols, documentation, README).

#### Scenario: dotnet pack succeeds without warnings

- **WHEN** `dotnet pack --configuration Release` is run on the solution
- **THEN** the command SHALL complete successfully (exit code 0)
- **AND** no packaging-related warnings SHALL be emitted

#### Scenario: Package passes NuGet validation

- **WHEN** the resulting `.nupkg` is validated (e.g., via NuGet Package Explorer or `dotnet nuget verify`)
- **THEN** it SHALL contain all declared metadata fields
- **AND** the package structure SHALL conform to NuGet packaging conventions
