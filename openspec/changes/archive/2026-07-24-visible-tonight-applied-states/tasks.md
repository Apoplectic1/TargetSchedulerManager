# Tasks: visible-tonight-applied-states

## 1. Pass stage split

- [x] 1.1 `VisibleTonightPass.cs`: replace `VisibleTonightPlan` with `VisibleTonightTargetPlan`
      (Edits/Enabled/Disabled/Unchanged) + `VisibleTonightProjectPlan` (Edits/Activated/Deactivated);
      `Plan` → `PlanTargets` (verdicts + target edits; RA/Dec fail-fast unchanged) + `PlanProjects(ts,
      appliedTargetEdits)` (pure derivation: applied-edit overlay on snapshot by `EditKey`, no throw)
- [x] 1.2 Doc comments: stage contract (why overlay-on-snapshot; failed flip ⇒ old value)

## 2. VM two-batch flow

- [x] 2.1 `MainViewModel.Reports.cs` `RunVisibleTonightAsync`: batch 1 (targets) → filter Applied
      outcomes by index → `PlanProjects` → batch 2 (projects); failure count sums both; reload when
      either batch had edits; status/log lines read from the two records
- [x] 2.2 `PlanProjects` always runs (zero-target-edit passes can still flip projects)

## 3. Tests

- [x] 3.1 `VisibleTonightPassTests`: retarget helper to two-stage (all-applied overlay reproduces the
      old combined matrix); split combined-edit-order asserts into per-stage asserts
- [x] 3.2 New derivation tests: refused sole-visible-target flip ⇒ no project Activate; failed disable
      ⇒ project stays Active (effective value is the old enabled); empty applied set ⇒ derivation
      matches the snapshot
- [x] 3.3 VM-level test: selective-refusal editor double — target flip refused ⇒ journal holds no
      orphaned `project.state` edit; combined failure count on the status line

## 4. Verify + docs (same commit as code)

- [x] 4.1 `dotnet build` + both test projects green (slnx only)
- [x] 4.2 Spec delta synced to `openspec/specs/visible-tonight-toggle/spec.md`
- [x] 4.3 ROADMAP: parked-m5 entry resolved; CHANGELOG entry; ARCHITECTURE busy-exclusion bullet's
      visible-tonight sentence updated if wording drifts (CLAUDE.md mirror likewise)
- [x] 4.4 Auto-archive (pure consistency fix — no user-verifiable happy-path change)
