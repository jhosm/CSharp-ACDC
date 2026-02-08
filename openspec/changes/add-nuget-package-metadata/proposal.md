# Change: Add NuGet Package Metadata and Source Link Support

## Why

Before publishing to NuGet.org, the package needs proper metadata (description, tags, license, repository URL), Source Link for debugging, and symbol packages for a good consumer experience. Without this, the `.nupkg` would be anonymous, non-debuggable, and missing licensing information -- unacceptable for a public library. This is P11 -- packaging configuration that depends on P7 (public API must be finalized before packaging).

## What Changes

### NuGet Package Metadata
- Add NuGet metadata properties to `Directory.Build.props`:
  - `PackageId`: `CSharpAcdc`
  - `Description`: server-only HTTP client library description referencing auth, caching, logging, and DelegatingHandler pipeline
  - `Authors`: project authors
  - `PackageLicenseExpression`: `MIT`
  - `PackageProjectUrl`: GitHub repository URL
  - `RepositoryUrl`: GitHub repository URL
  - `RepositoryType`: `git`
  - `PackageTags`: `http-client`, `authentication`, `caching`, `delegating-handler`, `aspnetcore`
  - `PackageReadmeFile`: `README.md`
  - `Copyright`: copyright notice

### Source Link
- Add `Microsoft.SourceLink.GitHub` package reference to `Directory.Packages.props` for centralized version management
- Configure `<EmbedUntrackedSources>true</EmbedUntrackedSources>` in `Directory.Build.props` to ensure all source files are embedded or linked
- Configure `<IncludeSymbols>true</IncludeSymbols>` with `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` for symbol package generation

### XML Documentation
- Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in `Directory.Build.props` so XML docs ship with the package and enable IntelliSense for consumers

### License
- Create MIT `LICENSE` file at repository root
- Reference via `PackageLicenseExpression` in build props (not `PackageLicenseFile`, since MIT is a well-known SPDX identifier)

## Impact

- **Affected specs:** nuget-packaging (new capability)
- **Depends on:** P7 add-builder-and-di (public API must be finalized before packaging metadata is meaningful)
- **Parallel with:** P8 (integration tests), P10 (advanced configuration)
- **Files modified:** `Directory.Build.props`, `Directory.Packages.props`
- **Files created:** `LICENSE`
