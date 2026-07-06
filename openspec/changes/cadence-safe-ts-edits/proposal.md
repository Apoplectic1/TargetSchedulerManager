# Proposal: cadence-safe-ts-edits

## Why

TSM's TS-database editor performs plain single-column UPDATEs. For two editable fields — `exposureplan.enabled` and `project.filterswitchfrequency` — that leaves stale `filtercadence` rows behind: persisted planner state that TS restores **verbatim** on every planning pass (surviving NINA restarts and imaging-PC reboots), whose plan-list indices no longer match the edited plan set. The user-visible failures are silent and land on TSM's primary use case (editing in-progress targets, which always have cadence rows): a newly enabled filter is never imaged, remaining filters shoot the wrong proportions, or the target is silently skipped every pass. In TS's own code every plan-set-changing edit clears these derived rows in the same breath; a plain-UPDATE TSM is the only writer that breaks that db invariant. This gates the roadmap item "per-filter `enabled` editing."

## What Changes

- **Library (`Astronomy.Catalog`)**: `TsField.CadenceSafe` (bool) becomes a clear-scope enum (`None` / `Target` / `Project`) declaring which derived `filtercadence` rows an edit invalidates. `TargetSchedulerEditor.SetField` deletes those rows **in the same transaction** as the UPDATE when the scope is non-`None` and the value actually changed. Deliberately invalidate-only: TSM never emulates TS scheduling behavior, it just refuses to leave derived rows contradicting the data it wrote (empty is always safe — TS regenerates).
- **Library**: edits to a target with `overrideexposureorder` rows are **refused** (new `RefusalReason`), not auto-cleared — OEO is user-authored data; deleting it is TS-editor business.
- **TSM app**: per-filter `enabled` checkbox on filter rows (first cadence-breaking consumer), riding the existing `TsEditGate` path, with a confirmation dialog that states the cadence reset and escalates wording when the source is LIVE (a target NINA is actively imaging cannot be safely cadence-edited externally; any other target can).
- Cadence-clear behavior is keyed to the declarative `TsEditableSchema` — `project.filterswitchfrequency` becomes shippable later with **no further library work**, just UI.

## Capabilities

### New Capabilities

- `cadence-safe-ts-editing`: the library contract for editing cadence-affecting TS fields — clear-scope metadata on `TsField`, transactional invalidation of derived `filtercadence` rows, value-unchanged skip, and OEO refusal.
- `per-filter-enabled-editing`: the TSM UI capability — in-grid enable/disable of individual exposure plans with cadence-reset confirmation and LIVE-mode warning.

### Modified Capabilities

_None (no existing specs; `openspec/specs/` is empty)._

## Impact

- **Two repos**: `..\Library\Astronomy.Catalog\TargetScheduler\` (`TsEditableSchema.cs`, `TargetSchedulerEditor.cs`) and TSM (`TsEditGate.cs`, `MainWindow.xaml`, row view-models). Library work must respect shared-library discipline (consumer-neutral wording; TSM session needs `--add-dir ..\Library`).
- **Library API**: `TsField.CadenceSafe` → `ClearScope` is a **breaking** signature change; per portfolio rule there is no back-compat shim — consumers rebuild from source.
- **Tests**: `Astronomy.Catalog.Tests` (`TsEditableSchemaTests`, editor tests) extend to cover the transactional clear, skip-when-unchanged, and OEO refusal; TSM `TsEditGate` seam tests cover the new outcome.
- **Behavior source of truth**: clear semantics mirror TS source (`SchedulerDatabaseContext.ToggleExposurePlan` / `SaveProject`); the invalidate-only design means TSM only depends on the weaker invariant "empty cadence ⇒ TS regenerates," which is structural in `FilterCadenceFactory.Generate`.
- **Not in scope**: OEO clearing, structural edits (add/delete plans), `TargetEditGuard` / running-session synchronization (accepted: a restart of the NINA sequence is the user's remedy for runtime re-sync; the db itself is always left consistent).
