# Design: Add .NET Solution Scaffold

## Context

CSharp-ACDC is a green-field .NET 8 class library — a server-only C# port of Dart-ACDC. No C# code exists yet. This proposal establishes the solution structure, build configuration, and dependency management patterns that all subsequent proposals (P2 through P11) will build upon. The project expects 10+ parallel feature branches, making merge conflict prevention a first-class concern.

## Goals

- **Zero-conflict parallel development:** All NuGet packages are declared upfront so that no subsequent proposal needs to modify `.csproj` files to add dependencies. This eliminates `.csproj` as a merge conflict source.
- **Central package management:** A single `Directory.Packages.props` file controls all package versions, ensuring consistency and simplifying upgrades.
- **Consistent build settings:** `Directory.Build.props` and `.editorconfig` enforce project conventions (nullable, file-scoped namespaces, warnings-as-errors) across all projects without duplication.
- **Reproducible builds:** `global.json` pins the .NET SDK version so all contributors and CI build with the same tooling.

## Non-Goals

- No application code is written in this proposal — only project infrastructure.
- No CI/CD pipeline configuration (that is a separate concern).
- No Docker or deployment configuration.
- No actual test cases — only the test project infrastructure.
- No `NuGet.config` — the project uses only nuget.org, so the default package source is sufficient.

## Decisions

### 1. Central Package Management via `Directory.Packages.props`

**Decision:** Use NuGet Central Package Management (CPM) to declare all package versions in a single `Directory.Packages.props` file at the solution root. Individual `.csproj` files use `<PackageReference Include="..." />` without `Version` attributes.

**Why:** CPM ensures every project in the solution uses the same version of each package. It also means that adding a new package reference to a `.csproj` only requires a one-line addition (no version string), and version bumps happen in exactly one file. This directly supports the zero-conflict goal.

**Alternatives considered:**
- Per-project version management: Rejected because it creates merge conflicts when multiple branches add the same package.
- `Directory.Build.targets` with `<PackageReference>`: Rejected because it auto-includes packages in all projects, which is too coarse.

### 2. `Directory.Build.props` for Shared Build Settings

**Decision:** Use `Directory.Build.props` at the solution root to set `TargetFramework`, `LangVersion`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, and `EnforceCodeStyleInBuild`. `LangVersion` is explicitly set to `12` even though .NET 8 defaults to C# 12, to prevent drift if the SDK version changes. `EnforceCodeStyleInBuild` is set to `true` so that `.editorconfig` style rules are evaluated by Roslyn analyzers during `dotnet build`, not only in IDEs.

**Why:** Avoids duplicating settings across three `.csproj` files. Any new projects added later automatically inherit these settings. Changes to the target framework or language version happen in one place. Without `EnforceCodeStyleInBuild`, `.editorconfig` rules are advisory during CLI builds — CI would never catch style violations.

### 3. Front-Load ALL NuGet Packages

**Decision:** Every NuGet package that any subsequent proposal (P2-P11) will need is declared in the initial scaffold, even though no code references them yet.

**Why:** This is the key design decision. If packages are added incrementally in feature branches, every branch that adds a `<PackageReference>` line creates a potential merge conflict with every other branch that does the same. By front-loading all packages, `.csproj` files become stable after P1 — no further modifications needed for dependency additions.

**Trade-off:** The initial `dotnet restore` downloads packages that are not yet used. This is negligible (a few MB of NuGet cache) compared to the developer-hours saved from avoiding merge conflicts.

### 4. `.gitignore` for Build Artifacts and Local Files

**Decision:** Include a `.gitignore` file at the repository root covering .NET build artifacts (`bin/`, `obj/`, `*.user`, `.vs/`), IDE files (`.idea/`, `*.suo`), OS files (`.DS_Store`), and local-only files (`.claude/settings.local.json`, `.env`).

**Why:** Prevents accidental commits of build output, IDE state, and sensitive local configuration. This is a prerequisite for all subsequent development — without it, the first `git add .` after implementation risks committing binary artifacts and secrets.

### 5. Separate Integration Test Project

**Decision:** Create a dedicated `tests/CSharpAcdc.IntegrationTests/` project separate from the unit test project.

**Why:**
- Integration tests (WireMock.Net) start real HTTP servers and need longer timeouts.
- Separation allows running `dotnet test --filter` by project for faster feedback loops during development.
- CI can run unit tests and integration tests as separate stages with different failure policies.

### 6. `.editorconfig` for Code Style Enforcement

**Decision:** Include an `.editorconfig` file enforcing project conventions (file-scoped namespaces, expression-body preferences, naming conventions with `_camelCase` for private fields, indentation).

**Why:** Ensures consistent formatting across all contributors and IDEs (Visual Studio, Rider, VS Code). Prevents style-only diffs that pollute PRs.

### 7. `global.json` for SDK Version Pinning

**Decision:** Pin the .NET 8 SDK version via `global.json` with a `latestFeature` rollForward policy.

**Why:** Ensures all developers and CI systems use the same major SDK version while allowing automatic adoption of patch-level updates within that feature band. Prevents "works on my machine" issues from SDK version differences.

**Roll-forward policy:** `latestFeature` allows the SDK to roll forward to the latest feature band within the same major version, balancing stability with security patches.

### 8. Namespace-Aligned Directory Structure

**Decision:** Create a directory skeleton under `src/CSharpAcdc/` with folders matching the intended namespace structure: Exceptions, Handlers, Auth, Cache, Logging, Configuration, Extensions, Builder, Client.

**Why:** Establishes the module boundaries up front so subsequent proposals know exactly where to place their code. `.gitkeep` files ensure empty directories are tracked by Git.

### 9. Solution Folders for Project Organization

**Decision:** Use solution folders (`src` and `tests`) inside the `.sln` file to group projects logically in IDE solution explorers.

**Why:** Without solution folders, all three projects appear flat in the IDE. Solution folders provide visual hierarchy that matches the physical directory structure, making navigation easier as the solution grows.

## Risks / Trade-offs

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Package versions drift if not updated together | Low | Medium | Central Package Management ensures single-file updates; Dependabot can automate PRs |
| Front-loaded packages may include unused dependencies in final build | Low | Low | .NET tree-shaking and trimming remove unused references; packages only add to build metadata until referenced |
| SDK version pin may block developers on older SDK | Low | Medium | `latestFeature` rollForward policy allows flexibility within the feature band |
| `.editorconfig` rules may conflict with individual IDE settings | Low | Low | `.editorconfig` takes precedence by design; rules match documented project conventions |

## Open Questions

None. All design decisions have been finalized.
