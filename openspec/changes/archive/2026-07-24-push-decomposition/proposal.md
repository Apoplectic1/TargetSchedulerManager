# push-decomposition — `TsSync.Push` from one ~170-line method to an orchestrator + named legs

## Why

Review finding M1 (2026-07-24): `Push` is the file most likely to be edited under pressure (push bugs are
the scary ones) and currently holds five concerns in one method — the probe/refusal gate, the write-back
replay leg, the field replay leg with its abort cascade, journal retention, and the closing-pull decision
(grown further by `truthful-outcome`'s containment) — plus two local functions (`Fail`,
`RefusedStructurally`) mutating captured state. A reader must hold the whole method to know which leg
mutated what. Now is the right moment: the truthful-outcome tests just surrounded the method, so the
decomposition has a tight behavior lock.

## What Changes

- **One orchestrator, one state carrier, one method per leg** — bodies move verbatim:
  - `PushReplayState` (private class): `FailedSeqs` + `Failures` + the `Fail(entries, detail)` rule —
    replay state becomes an explicit parameter instead of closure captures.
  - `ProbePushPreconditions()` — unreachable/busy refusals before any write.
  - `ReplayWriteBackLeg(collapsed, state)` — the writer leg; non-null return = whole-db structural
    refusal (nothing was written), null = leg completed with per-plan failures in `state`.
  - `ReplayFieldLeg(collapsed, state)` — per-field guarded replay in seq order with the whole-db-refusal
    abort cascade.
  - `CommitAndClose(collapsed, state, pullProgress, pullCancel)` — seq-aware retention, the
    partial/mid-push-edit returns, and the contained closing pull.
  - `Push` itself becomes ~10 lines of sequence.
- **Behavior-preserving** — `TsSyncTests` (incl. the truthful-outcome group) green is the definition of
  done. One coverage gap gets a test while we're here: the field-leg **abort cascade** (a whole-db
  refusal fails every remaining field as "not attempted" without hammering the db) has no direct test.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `ts-sync-model`: codifies two existing, test-visible replay behaviors the decomposition must preserve
  (currently enforced by code + comments only): the **leg order** (write-back replays before manual
  fields, in seq order, so an explicit later desired edit outranks the writer's ratchet) and the
  **abort cascade** (a whole-db refusal mid-field-leg fails every remaining entry without attempting it;
  a structural refusal in the write-back leg refuses before any field write).

## Impact

- **App only, one file of logic**: `Shared/TsSync.cs`. Tests: `TsSyncTests` (existing suite = lock; one
  new abort-cascade test). Docs: CHANGELOG/ROADMAP digest, same commit (no ARCHITECTURE change — the
  external contract is untouched; the spec delta records the codified behaviors).
