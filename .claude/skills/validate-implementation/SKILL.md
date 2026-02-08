---
name: validate-implementation
description: Validate that an OpenSpec proposal implementation is complete and correct
user-invocable: true
disable-model-invocation: true
arguments:
  - name: change-id
    description: The OpenSpec change ID to validate (e.g. add-solution-scaffold)
    required: true
---

Validate that the implementation of OpenSpec change proposal `$ARGUMENTS.change-id` is complete and correct.

## Steps

1. **Read the proposal** — Open `openspec/changes/$ARGUMENTS.change-id/proposal.md` and note the acceptance criteria and scope.

2. **Check task completion** — Read `openspec/changes/$ARGUMENTS.change-id/tasks.md`. Count total tasks and completed tasks (`- [x]` vs `- [ ]`). List any incomplete tasks.

3. **Verify spec coverage** — If spec deltas exist in `openspec/changes/$ARGUMENTS.change-id/specs/`, read each one. For every ADDED or MODIFIED requirement, verify corresponding code exists by searching the codebase with Grep/Glob. Note any gaps.

4. **Run OpenSpec validation** — Execute `openspec validate $ARGUMENTS.change-id --strict --no-interactive`. Report any validation errors.

5. **Build check** — If `CSharp-ACDC.sln` exists, run `dotnet build --no-restore`. Report build errors.

6. **Test check** — If the solution exists and tests are present, run `dotnet test --no-build --verbosity minimal`. Report test failures.

7. **Produce validation report** — Output a summary:

```
## Validation Report: $ARGUMENTS.change-id

### Tasks
- Total: X
- Complete: Y
- Incomplete: Z
  - [ ] list incomplete tasks...

### Spec Coverage
- Requirements checked: N
- All covered: yes/no
- Gaps: list any uncovered requirements...

### OpenSpec Validation
- Result: pass/fail
- Issues: list any...

### Build
- Result: pass/fail/skipped (no .sln)

### Tests
- Result: pass/fail/skipped (no tests)

### Verdict
- Ready to archive: YES / NO (reason)
```

## Notes
- If the proposal has a `design.md`, read it for additional architectural constraints to verify.
- For proposals that add new files, verify the files exist at the expected paths.
- For proposals that modify existing files, verify the modifications are present.
