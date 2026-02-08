# Tasks: Add .NET Solution Scaffold

## 1. Solution Setup

- [ ] 1.1 Create `global.json` with .NET 8 SDK version pin
- [ ] 1.2 Create `Directory.Build.props` with shared build settings (net8.0, nullable enable, implicit usings, TreatWarningsAsErrors, file-scoped namespaces)
- [ ] 1.3 Create `Directory.Packages.props` with central package version management for all library and test NuGet packages
- [ ] 1.4 Create `.editorconfig` with code style rules (file-scoped namespaces, expression-body preferences, naming conventions)
- [ ] 1.5 Create `CSharp-ACDC.sln` solution file

## 2. Library Project

- [ ] 2.1 Create `src/CSharpAcdc/CSharpAcdc.csproj` with PackageReference entries for all library packages (Microsoft.Extensions.Http, FusionCache, JWT, etc.)
- [ ] 2.2 Create directory skeleton under `src/CSharpAcdc/` with `.gitkeep` files: Exceptions/, Handlers/, Auth/, Cache/, Logging/, Configuration/, Extensions/, Builder/, Client/

## 3. Test Projects

- [ ] 3.1 Create `tests/CSharpAcdc.Tests/CSharpAcdc.Tests.csproj` with xUnit, NSubstitute, FluentAssertions, RichardSzalay.MockHttp, coverlet.collector, and project reference to CSharpAcdc
- [ ] 3.2 Create `tests/CSharpAcdc.IntegrationTests/CSharpAcdc.IntegrationTests.csproj` with xUnit, WireMock.Net, FluentAssertions, Microsoft.AspNetCore.TestHost, coverlet.collector, and project reference to CSharpAcdc

## 4. Verification

- [ ] 4.1 Verify `dotnet restore` succeeds (all packages resolve)
- [ ] 4.2 Verify `dotnet build` succeeds with zero warnings
- [ ] 4.3 Verify `dotnet test` runs successfully (no tests yet, but infrastructure works — exit code 0)
- [ ] 4.4 Verify solution opens correctly in Visual Studio / Rider / VS Code
