# Toolbar Floor knob — right-size the up-downs, rename Horizon → Floor

## Why

Two problems in the Visible-Tonight toolbar group, both surfaced by observation `obs-9b52` (2026-07-26):

1. **The Duration and Horizon numeric up-downs are ~110 px each for a 2–3 digit value.** Neither sets
   `Width`, so each auto-sizes to its template content — the inner `TextBox`'s default `MinWidth`
   (`TextControlThemeMinWidth`, 64 px) plus the two inline spin buttons — wasting toolbar width the
   button group needs.
2. **"Horizon" is the wrong word for this knob, and it is overloaded three ways in this tree** — the TS
   schema columns `usecustomhorizon` / `horizonoffset`, the `Astronomy.Core.Horizons` library API
   (`ScalarHorizonProfile`, `IsAboveHorizonForAtLeast`, the geometric horizon), and this toolbar knob.
   The knob is an **altitude floor** — a name the code already uses internally
   (`ScalarHorizonProfile altitudeFloor`, the spec's "Horizon altitude floor"). Renaming the knob to
   **Floor** removes the collision and matches the vocabulary the implementation already reaches for.

Folded in opportunistically because it lives on the same sentences: the button's label was changed in
the working tree from **Find** to **Tonight** and never committed, so the specs and reference docs still
say "Find" in eight places. This change makes that name true everywhere.

## What Changes

**Sizing (presentation):**
- `VisibleDuration` gets an explicit `Width` sized to its 3-digit budget (range 15–480); the renamed
  Floor box gets a `Width` sized to its 2-digit budget (range 0–89). Inline spin buttons stay — they are
  template-fixed width, so only the text area is reclaimed.
- Both boxes get the established narrow-`NumberBox` treatment already used by the grid's inline Desired
  box: a `Loaded` handler zeroing the template-internal `TextBox.MinWidth` and trimming its padding,
  without which an explicit `Width` cannot take effect.

**Rename (deep, knob-scoped):**
- Label `"Horizon:"` → `"Floor:"`; `x:Name="VisibleHorizon"` → `VisibleFloor`; both tooltips reworded.
- Parameter `horizonAltitudeDeg` → `floorAltitudeDeg` through `MainViewModel.RunVisibleTonightAsync`,
  `VisibleTonightPass.PlanTargets`, and all call sites in `MainWindow.xaml.cs` and the tests.
- Test `HorizonAltitudeFloor_GatesLowTargets` → `AltitudeFloor_GatesLowTargets`.
- Requirement/scenario wording in the `visible-tonight-toggle` spec, plus `ARCHITECTURE.md`,
  `DOMAIN.md`, `VERIFICATION.md`.
- Button name **Find** → **Tonight** in the `visible-tonight-toggle` and `busy-exclusion` specs and the
  same three reference docs.

**Explicitly NOT renamed** (different concepts wearing the same word):
- TS schema columns `usecustomhorizon`, `horizonoffset` — external contract (`TS-SCHEMA.md`,
  `TsInboundDiff`, `target-and-plan-flyouts` spec, `ROADMAP.md`).
- `Astronomy.Core` library API — the `Horizons` namespace, `ScalarHorizonProfile`,
  `CoarseVisibility.IsAboveHorizonForAtLeast` — and prose about the *geometric* horizon (e.g. the
  scenario tests pinned at 0°). Shared-library surface; a TSM label is no reason to touch it.
- `CHANGELOG.md`, dated `docs/*.md`, and archived openspec changes keep their historical wording; the
  rename gets a new CHANGELOG entry instead.

No behavior changes: the predicate, ranges, defaults, busy exclusion, and journaling are untouched.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `visible-tonight-toggle`: the knob named in the input-contract requirement and its scenarios becomes
  **Floor** rather than **Horizon**, and the button that runs the pass is named **Tonight** rather than
  **Find**. Predicate, ranges, and defaults unchanged.
- `busy-exclusion`: the busy-sensitive toolbar action named **Find** becomes **Tonight** (same control).

## Impact

- `TargetSchedulerManager.App/MainWindow.xaml` — the two `NumberBox` declarations (widths, names,
  labels, tooltips, `Loaded`) and the group comment. Note: this file carries an **uncommitted**
  Find→Tonight button edit that this change absorbs.
- `TargetSchedulerManager.App/MainWindow.xaml.cs` — the `VisibleTonight_Click` call site, the comment
  block, and the narrow-`NumberBox` `Loaded` handler (generalized from `DesiredBox_Loaded`).
- `TargetSchedulerManager.App/ViewModels/MainViewModel.Reports.cs`,
  `TargetSchedulerManager.App/Services/VisibleTonightPass.cs` — parameter rename + doc comments.
- `TargetSchedulerManager.App.Tests/VisibleTonightPassTests.cs`,
  `TargetSchedulerManager.App.Tests/MainViewModelBusyGateTests.cs` — argument names, one test name.
- Docs: `ARCHITECTURE.md`, `DOMAIN.md` (toolbar map + the narrow-`NumberBox` sizing convention),
  `VERIFICATION.md`, `CHANGELOG.md` (new entry).
- No library (`..\Library`) edits. No database, schema, or TS-contract impact.
