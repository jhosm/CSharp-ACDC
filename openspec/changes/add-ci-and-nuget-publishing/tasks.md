## 1. CI Workflow
- [ ] 1.1 Create `.github/workflows/ci.yml`
- [ ] 1.2 Configure trigger on push to main and PRs
- [ ] 1.3 Add .NET 10 setup and NuGet cache
- [ ] 1.4 Add build step with TreatWarningsAsErrors
- [ ] 1.5 Add unit test step with coverage collection
- [ ] 1.6 Add integration test step
- [ ] 1.7 Add coverage report upload

## 2. Release Workflow
- [ ] 2.1 Create `.github/workflows/release.yml`
- [ ] 2.2 Configure trigger on version tag push
- [ ] 2.3 Add build, test, pack, and push steps
- [ ] 2.4 Configure NuGet API key from secrets

## 3. Dependency Management
- [ ] 3.1 Create `.github/dependabot.yml` for NuGet updates
