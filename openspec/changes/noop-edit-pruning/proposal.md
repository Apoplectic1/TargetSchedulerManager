# noop-edit-pruning — Proposal

## Why

Toggling a field and toggling it back leaves a phantom edit: the field marks `→` on every surface, counts
in "N unpushed", shows as "On → On" in the push review, and replays a no-op write to BIRDWATCHER — which,
for cadence-clearing fields (`enabled`, `filterswitchfrequency`), clears the remote target's filter-cadence
rows for nothing. Worse, even a *single* re-commit of an unchanged value journals (the editor short-circuits
same-value writes as verified without touching the db; the gate journals every verified write). User
observed 2026-07-26 in the flyout marks and chose net-no-op pruning (option 1), applied uniformly to every
SyncMarks surface.

## What Changes

- `TsJournal.Append` prunes on baseline revert: when a write returns a field to its **baseline** (the first
  journaled Old since the last push — the user-identified state to remember), the field's entries are
  removed (crash-safe rewrite) instead of a new entry appending; a first-touch same-value commit journals
  nothing. `Append` becomes nullable-returning (null = the field resolved clean).
- Every consumer heals from the one source, with **zero per-surface code**: grid row/header/template-picker/
  flyout marks, the unpushed badge, the push review and replay, and the dirty-open prompt all read the
  journal.
- The field's *initial sync state* is preserved (the user's gotcha): inbound facts live in a separate
  store, so a field that carried `←` before the edit round-trip shows `←` again after it — not blank.
- Value equality uses the one invariant display-text rule already shared by the journal tooltips and the
  editor's own no-op short-circuit; a formatting mismatch fails safe (keeps today's marked behavior).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `ts-sync-model`: the "All edits write locally and journal" requirement gains the baseline-revert pruning
  contract (journal never holds a net-no-op field; baseline resets at push).
- `edit-direction-marks`: adds "reverted fields read clean on every mark surface" (incl. the inbound-state
  preservation scenario).

## Impact

- `TargetSchedulerManager.App\Shared\TsJournal.cs` — baseline map beside the field-key set, prune logic in
  `Append`, shared index rebuild for `Load`/`ReplaceAllLocked`.
- Tests: journal pruning units; mark-surface + badge assertions.
- No library changes; no consumer changes (SyncMarks/badge/push read the journal as today).
