# exposure-zero-literal

## Why

The Library adjudicated exposure-0 semantics against the TS source (2026-07-07, `d26b75e` in `..\Library`):
the planner's defer-to-template sentinel test is exactly `!= -1`, so **0 is a literal zero-second exposure**,
and `TargetSchedulerEditor.ReadPlanEffectiveExposure` now returns 0 (not the template default) for an
exposure-0 plan. TSM still carries two `> 0` filters that encode the old "non-positive = unknown" worldview,
so writing 0 into the exposure editor leaves the Seconds cell stale until the next reload — breaking the
standing "a flyout edit reflects in its column at once" rule for exactly the value 0, and drifting from the
pinned contract (Library `CONSUMERS.md` #19/#20: TSM seeds from the resolved value under 0-is-literal
semantics).

## What Changes

- `TsEditGate.ReadPlanEffectiveSecondsAsync` (`TsEditGate.cs:116`): accept a resolved effective exposure of 0
  as a real value — only a missing row/template or a fault maps to null. Doc comment loses the
  "non-positive value" = unknown wording.
- `MainWindow.xaml.cs:366` (exposure commit path): treat only the −1 sentinel as "resolve via the db";
  a committed 0 mirrors as 0 directly (`v >= 0` instead of `v > 0`).
- New test pinning the mirror-at-0 behavior (a verified exposure-0 write mirrors the Seconds cell to 0
  without a reload).
- No Library changes — the Library side already shipped and is pinned by its own contract tests.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `schema-driven-field-editor`: the in-place-mirror commit requirement gains the exposure-0 rule — the
  effective-exposure resolution behind the sentinel control treats 0 as a literal value (only the negative
  sentinel defers to the template), so a committed 0 mirrors immediately like any other value.

## Impact

- **Code**: `TargetSchedulerManager.App\Shared\TsEditGate.cs`, `TargetSchedulerManager.App\MainWindow.xaml.cs`
  (~3 lines + doc comment), one new test in `TargetSchedulerManager.App.Tests`.
- **Behavior**: user-visible only when a plan's exposure is exactly 0 — a degenerate but TS-legal value.
  No change for positive overrides or the −1 sentinel.
- **Dependencies**: relies on Library ≥ `d26b75e` (`ReadPlanEffectiveExposure` 0-is-literal). Local-disk
  `ProjectReference` — already satisfied.
- **Docs**: none beyond the delta spec; ARCHITECTURE/DOMAIN don't describe the 0 corner.
