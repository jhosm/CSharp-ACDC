## ADDED Requirements

### Requirement: Continuous Integration
The CI workflow SHALL build the solution with warnings-as-errors and run all unit tests on every push to `main` and on every pull request. The workflow SHALL use .NET 8 on ubuntu-latest and SHALL restore dependencies before building.

#### Scenario: PR triggers CI build and tests
- **WHEN** a pull request is opened or updated against `main`
- **THEN** the CI workflow SHALL check out the code, restore NuGet packages, build the solution with `TreatWarningsAsErrors` enabled, and run all unit tests
- **AND** the workflow SHALL report success only if the build succeeds and all unit tests pass

#### Scenario: Push to main triggers CI
- **WHEN** a commit is pushed to the `main` branch
- **THEN** the CI workflow SHALL execute the same build and test steps as for pull requests

### Requirement: Test Coverage
The CI workflow SHALL collect code coverage via coverlet during the unit test step and upload the coverage report as a build artifact.

#### Scenario: Coverage report is generated and uploaded
- **WHEN** the unit test step completes successfully
- **THEN** the CI workflow SHALL produce a code coverage report using coverlet
- **AND** the workflow SHALL upload the coverage report as a GitHub Actions build artifact

### Requirement: Integration Tests
The CI workflow SHALL run integration tests in a separate step from unit tests. The integration test step SHALL execute tests from the `CSharpAcdc.IntegrationTests` project.

#### Scenario: Integration tests run separately from unit tests
- **WHEN** the CI workflow executes
- **THEN** unit tests from `CSharpAcdc.Tests` SHALL run in one step
- **AND** integration tests from `CSharpAcdc.IntegrationTests` SHALL run in a separate subsequent step

### Requirement: NuGet Package Cache
The CI workflow SHALL cache NuGet packages to reduce build times across workflow runs.

#### Scenario: NuGet packages are cached between runs
- **WHEN** the CI workflow runs and NuGet packages have been previously restored
- **THEN** the workflow SHALL use cached packages instead of downloading them again
- **AND** the cache key SHALL be based on the lock file or project files so that cache invalidation occurs when dependencies change

### Requirement: Release Publishing
The release workflow SHALL pack and publish the NuGet package to NuGet.org when a version tag matching the pattern `v*.*.*` is pushed. The workflow SHALL build in Release configuration, run all tests, pack the NuGet package, and push it to the NuGet.org feed.

#### Scenario: Version tag triggers NuGet publish
- **WHEN** a Git tag matching `v*.*.*` (e.g., `v1.0.0`, `v2.3.1`) is pushed
- **THEN** the release workflow SHALL build the solution in Release configuration, run all tests, create a NuGet package using `dotnet pack`, and push the package to NuGet.org

#### Scenario: Release fails if tests fail
- **WHEN** a version tag is pushed but tests fail during the release workflow
- **THEN** the workflow SHALL NOT publish the NuGet package
- **AND** the workflow SHALL report a failure status

### Requirement: Release Security
The release workflow SHALL use GitHub secrets for the NuGet API key and SHALL NOT expose secrets in logs. The API key MUST be stored as a GitHub repository secret named `NUGET_API_KEY`.

#### Scenario: NuGet API key is sourced from secrets
- **WHEN** the release workflow pushes a package to NuGet.org
- **THEN** the workflow SHALL authenticate using the `NUGET_API_KEY` GitHub secret
- **AND** the API key SHALL NOT appear in workflow logs or build output

### Requirement: Dependency Updates
Dependabot SHALL be configured to automatically create pull requests for NuGet package updates on a weekly schedule. The configuration SHALL target the `/` package ecosystem directory.

#### Scenario: Dependabot creates weekly PRs for NuGet updates
- **WHEN** a new version of a NuGet dependency is available
- **THEN** Dependabot SHALL create a pull request to update the dependency
- **AND** Dependabot SHALL check for updates on a weekly schedule
