# Design: visible-tonight-applied-states

## Context

`VisibleTonightPass.Plan` today returns one `VisibleTonightPlan` — a flat edit list (targets first, then
project flips) plus five counts — computed entirely from the load snapshot. The VM applies it as one
`ApplyManyAsync` batch and counts failures. Project flips are therefore committed to before knowing
whether their premise (the target flips) landed.

## Goals / Non-Goals

- **Goal**: project `state` derivation consumes what actually landed, not what was intended.
- **Goal**: preserve every existing guarantee — busy scope with no seams, per-edit journal/verify
  contract, per-flip failure tolerance, fail-fast contract abort before any edit.
- **Non-goal**: gate API changes, retries of failed flips, cross-batch transactionality (two ordinary
  sessions are fine — the busy scope is the exclusion boundary, not the session).

## Decisions

### Two-stage API on the same static class

```csharp
internal sealed record VisibleTonightTargetPlan(
    IReadOnlyList<VisibleTonightEdit> Edits, int Enabled, int Disabled, int Unchanged);
internal sealed record VisibleTonightProjectPlan(
    IReadOnlyList<VisibleTonightEdit> Edits, int Activated, int Deactivated);

public static VisibleTonightTargetPlan PlanTargets(ts, site, utcNow, minDuration, horizonAltitudeDeg);
public static VisibleTonightProjectPlan PlanProjects(ts, IReadOnlyList<VisibleTonightEdit> appliedTargetEdits);
```

`PlanTargets` keeps the RA/Dec fail-fast throw (still before any edit). `PlanProjects` is pure state
derivation — no visibility math, cannot throw: it re-derives the processed-project set (Active/Inactive
filter) from the snapshot and computes each target's **effective** active as *applied-edit value if one
landed for that key, else the snapshot value*. Keying uses the same `EditKey` (guid-or-id) convention, so
the overlay dictionary maps `edit.Key → Value` with no reverse lookup.

Why overlay-on-snapshot instead of carrying verdicts out of `PlanTargets`: a failed flip's effective
value is the *old* one, which only the snapshot knows; unchanged targets (no edit) equal their snapshot
value by definition. So `appliedTargetEdits` is the entire phase-2 input — smaller surface, and
`PlanProjects(ts, targetPlan.Edits)` (all-applied) exactly reproduces today's combined behavior, which
keeps the existing test matrix expressible.

### VM two-batch flow

```csharp
targetPlan = VisibleTonightPass.PlanTargets(...);           // may throw: abort, zero edits (unchanged)
targetOutcomes = await _gate.ApplyManyAsync(targetPlan.Edits...);
applied = Zip(targetPlan.Edits, targetOutcomes).Where(o is Applied).Select(e);  // index-aligned per gate contract
projectPlan = VisibleTonightPass.PlanProjects(load.Ts, applied);
projectOutcomes = await _gate.ApplyManyAsync(projectPlan.Edits...);
failed = both batches' non-Applied count;
```

- Both batches + the recompute run inside the one `TryBeginBusy` scope — row edits stay refused between
  batches, so the seam introduced by two sessions is exclusion-covered.
- `PlanProjects` always runs, even with zero target edits (a project can need a flip over already-settled
  targets — existing behavior).
- Reload condition becomes `targetPlan.Edits.Count + projectPlan.Edits.Count > 0`.
- Status line / log formats unchanged; counts now read from the two records (activated/deactivated are
  actual, post-apply).

### Spec deltas (two requirements modified)

1. *Project state derived from post-pass target enables* → derived from **applied** target enables; new
   scenario: a refused/failed target flip contributes its old value, so no orphaned project flip.
2. *One batch* requirement → two sequenced single-session batches under one unbroken busy exclusion;
   "one editor session" scenario reworded per-batch; per-flip failure tolerance restated across both.

## Risks / Trade-offs

- **Two editor sessions per press** — trivial cost (local file open ×2); the exclusion, not the session,
  is the atomicity boundary, and each edit journals/verifies individually as before.
- **Test-double effort**: the VM-level failed-flip test needs an editor whose `TrySetField` refuses one
  specific row; existing busy-gate fixtures are adjacent (BlockingEditor) but a selective-refusal double
  is new. Pass-level tests carry most of the derivation coverage; the VM test only pins the wiring
  (refused target ⇒ no orphaned project edit in the journal).

## Migration Plan

None — no persisted shapes change (rule: no back-compat code). Journal entries, push review, and the
happy-path status line are byte-identical.

## Open Questions

None.
