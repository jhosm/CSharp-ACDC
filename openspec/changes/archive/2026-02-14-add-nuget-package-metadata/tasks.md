## 1. Package Metadata
- [x] 1.1 Add NuGet metadata properties to `Directory.Build.props` (`PackageId`, `Description`, `Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType`)
- [x] 1.2 Add `PackageTags` with values: `http-client`, `authentication`, `caching`, `delegating-handler`, `aspnetcore`
- [x] 1.3 Add `PackageReadmeFile` pointing to `README.md` and include README in package via `<None Include="..." Pack="true" PackagePath="\" />`
- [x] 1.4 Set `PackageLicenseExpression` to `MIT`

## 2. Source Link
- [x] 2.1 Add `Microsoft.SourceLink.GitHub` to `Directory.Packages.props` with a pinned version
- [x] 2.2 Add `Microsoft.SourceLink.GitHub` as a `PrivateAssets="All"` package reference in `Directory.Build.props` (or library `.csproj`)
- [x] 2.3 Configure `<EmbedUntrackedSources>true</EmbedUntrackedSources>` in `Directory.Build.props`
- [x] 2.4 Configure `<IncludeSymbols>true</IncludeSymbols>` and `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` in `Directory.Build.props`

## 3. Documentation
- [x] 3.1 Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in `Directory.Build.props`

## 4. License
- [x] 4.1 Create MIT `LICENSE` file at repository root

## 5. Verification
- [x] 5.1 Verify `dotnet pack` produces a valid `.nupkg` with no warnings
- [x] 5.2 Verify package metadata is correct in the `.nupkg` (inspect with `dotnet nuget verify` or NuGet Package Explorer)
- [x] 5.3 Verify `.snupkg` symbol package is generated alongside the `.nupkg`
- [x] 5.4 Verify `PackageReadmeFile` content is included in the package
