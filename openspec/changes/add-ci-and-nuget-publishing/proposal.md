# Change: Add CI/CD Pipelines and NuGet Publishing

## Why
Automated CI/CD ensures code quality (build, test, coverage) on every PR and enables automated NuGet package publishing on version tags. This proposal lives entirely in `.github/` — zero merge conflict risk with any code proposal.

## What Changes
- `.github/workflows/ci.yml` — continuous integration workflow:
  - Triggered on: push to `main`, pull requests
  - Matrix: .NET 8 on ubuntu-latest
  - Steps: checkout, setup .NET, restore, build (TreatWarningsAsErrors), unit tests with coverage, integration tests, upload coverage report
  - Separate test steps for `CSharpAcdc.Tests` and `CSharpAcdc.IntegrationTests`
  - NuGet package cache for speed
- `.github/workflows/release.yml` — NuGet release workflow:
  - Triggered on: version tag push (`v*.*.*`)
  - Steps: checkout, setup .NET, restore, build Release, test, pack, push to NuGet.org
  - Uses GitHub secrets for `NUGET_API_KEY`
- `.github/dependabot.yml` — automated dependency updates for NuGet packages on a weekly schedule

## Impact
- Affected specs: ci-and-publishing (new capability)
- Depends on: P1 add-solution-scaffold (solution must exist to build)
- Parallel with: ALL code proposals (P2-P8, P10, P11) — lives in `.github/` only
- Affected code: `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `.github/dependabot.yml`
