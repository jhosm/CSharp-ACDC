## 1. Package Metadata
- [ ] 1.1 Add NuGet metadata properties to `Directory.Build.props` (`PackageId`, `Description`, `Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType`)
- [ ] 1.2 Add `PackageTags` with values: `http-client`, `authentication`, `caching`, `delegating-handler`, `aspnetcore`
- [ ] 1.3 Add `PackageReadmeFile` pointing to `README.md` and include README in package via `<None Include="..." Pack="true" PackagePath="\" />`
- [ ] 1.4 Set `PackageLicenseExpression` to `MIT`

## 2. Source Link
- [ ] 2.1 Add `Microsoft.SourceLink.GitHub` to `Directory.Packages.props` with a pinned version
- [ ] 2.2 Add `Microsoft.SourceLink.GitHub` as a `PrivateAssets="All"` package reference in `Directory.Build.props` (or library `.csproj`)
- [ ] 2.3 Configure `<EmbedUntrackedSources>true</EmbedUntrackedSources>` in `Directory.Build.props`
- [ ] 2.4 Configure `<IncludeSymbols>true</IncludeSymbols>` and `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` in `Directory.Build.props`

## 3. Documentation
- [ ] 3.1 Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in `Directory.Build.props`

## 4. License
- [ ] 4.1 Create MIT `LICENSE` file at repository root

## 5. Verification
- [ ] 5.1 Verify `dotnet pack` produces a valid `.nupkg` with no warnings
- [ ] 5.2 Verify package metadata is correct in the `.nupkg` (inspect with `dotnet nuget verify` or NuGet Package Explorer)
- [ ] 5.3 Verify `.snupkg` symbol package is generated alongside the `.nupkg`
- [ ] 5.4 Verify `PackageReadmeFile` content is included in the package
