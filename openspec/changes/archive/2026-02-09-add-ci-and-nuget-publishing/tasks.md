## 1. CI Workflow
- [x] 1.1 Create `.github/workflows/ci.yml`
- [x] 1.2 Configure trigger on push to main and PRs
- [x] 1.3 Add .NET 10 setup and NuGet cache
- [x] 1.4 Add build step with TreatWarningsAsErrors
- [x] 1.5 Add unit test step with coverage collection
- [x] 1.6 Add integration test step
- [x] 1.7 Add coverage report upload

## 2. Release Workflow
- [x] 2.1 Create `.github/workflows/release.yml`
- [x] 2.2 Configure trigger on version tag push
- [x] 2.3 Add build, test, pack, and push steps
- [x] 2.4 Configure NuGet API key from secrets

## 3. Dependency Management
- [x] 3.1 Create `.github/dependabot.yml` for NuGet updates
