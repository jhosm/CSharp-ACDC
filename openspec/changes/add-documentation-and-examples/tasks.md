# Tasks: Add Documentation and Runnable Sample Projects

## 1. README

- [x] 1.1 Write project overview and motivation section
- [x] 1.2 Write installation instructions (`dotnet add package CSharpAcdc`)
- [x] 1.3 Write quick start section (zero-config, 3 lines of code)
- [x] 1.4 Write authenticated client configuration example
- [x] 1.5 Write cached client configuration example
- [x] 1.6 Write full pipeline configuration example
- [x] 1.7 Write handler pipeline diagram (ASCII art showing handler order)
- [x] 1.8 Write configuration reference (all `IOptions<T>` settings with defaults)
- [x] 1.9 Write exception handling guide (which exceptions to catch, when, hierarchy diagram)
- [x] 1.10 Write migration guide from raw `HttpClient` to CSharpAcdc
- [x] 1.11 Write contributing guidelines section

## 2. Samples

- [x] 2.1 Create `samples/BasicUsage/` project (BasicUsage.csproj + Program.cs)
- [x] 2.2 Create `samples/AuthenticatedClient/` project (AuthenticatedClient.csproj + Program.cs)
- [x] 2.3 Create `samples/CachedClient/` project (CachedClient.csproj + Program.cs)
- [x] 2.4 Create `samples/FullPipeline/` project (FullPipeline.csproj + Program.cs)
- [x] 2.5 Verify all sample projects build successfully (`dotnet build` for each)

## 3. Other

- [x] 3.1 Create `CHANGELOG.md` with initial 1.0.0 entry following Keep a Changelog format
- [x] 3.2 Audit XML doc comments on all public types and members — ensure every public type, method, property, and enum value has a `<summary>` tag
