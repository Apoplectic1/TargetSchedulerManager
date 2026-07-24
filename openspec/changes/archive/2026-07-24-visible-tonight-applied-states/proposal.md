# Proposal: visible-tonight-applied-states

## Why

The Visible-tonight pass computes project `state` flips from the *intended* post-pass target set before
any edit is applied (review item m5, parked 2026-07-24, now un-parked by user decision). Because edits
apply per-row with per-row failure tolerance, a failed `target.active` write paired with a successful
`project.state` write leaves the local copy internally inconsistent — e.g. a project flipped Active whose
sole visible target never actually got enabled. The journal then carries the project flip without its
sibling target flip, so a later Push would replay the orphaned state change. Rare (never observed) and
reversible via Discard, but cheap to close properly while the code is warm.

## What Changes

- `VisibleTonightPass.Plan` splits into two stages: `PlanTargets` (visibility verdicts → `target.active`
  edits + target counts) and `PlanProjects` (project `state` edits derived from the **applied** target
  edits overlaid on the snapshot — a failed flip means that target keeps its old value).
- `MainViewModel.RunVisibleTonightAsync` applies two sequenced `ApplyManyAsync` batches (targets, then
  projects) with the project recompute between them, all inside the same busy scope. Failure counts sum
  across both batches.
- Status-line activated/deactivated counts become *actual* counts (derived from what landed) rather than
  planned ones — identical on the happy path.
- Free consistency win: if the whole target batch fails (editor session cannot open), the project stage
  derives against unchanged states and emits no misleading flips.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `visible-tonight-toggle`: "Project state derived from post-pass target enables" becomes derived from
  **applied** target enables (recomputed after the target batch lands; a failed flip contributes its old
  value). The single-batch requirement becomes two sequenced single-session batches (targets, then
  projects) under one unbroken busy scope.

## Impact

- **Code**: `TargetSchedulerManager.App/Services/VisibleTonightPass.cs` (stage split, record reshape);
  `TargetSchedulerManager.App/ViewModels/MainViewModel.Reports.cs` (`RunVisibleTonightAsync` two-batch
  flow). No changes to `TsEditGate`, the journal/push path, busy exclusion, XAML, or the Library.
- **Tests**: `VisibleTonightPassTests` restructured to the two-stage API (all-applied overlay reproduces
  the old combined behavior); new failed-flip derivation tests; VM-level test that a refused target flip
  yields no orphaned project flip.
- **Docs**: `visible-tonight-toggle` spec delta; ROADMAP parked-item removal; CHANGELOG entry.
- **Verification**: behavior change manifests only on per-row write failures, which cannot be triggered
  from the app UI — auto-archive after tests pass (standing rule), happy path unchanged.
