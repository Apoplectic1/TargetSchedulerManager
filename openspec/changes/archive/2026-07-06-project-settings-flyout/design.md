# Design: project-settings-flyout

## Context

The flyout stack is fully generic below the trigger layer: `TsFieldsEditor.Create(table, …)` renders any
`TsEditableSchema` table (the cadence filter already excludes `filterswitchfrequency`),
`TsEditGate.ReadFieldsAsync`/`ApplyAsync` take a `TsTable`, and under the sync model every verified write
journals for the reviewed push. `ProjectTsKey` rides every `ReconciliationRow` and surfaces on
`TargetGroupRow`/`PanelGroupRow` children. What's missing is only a trigger for `TsTable.Project` and TS's
one cross-field save rule. Decisions settled with the user 2026-07-06: **right-click anchor only** (no second
hover glyph — the reveal mechanism finds exactly one `EditGlyph` per row template) and **warn-never-block**
for the cross-field rule.

## Goals / Non-Goals

**Goals:**
- Every project knob TS's Database Manager edits (minus cadence-breaking fsf) editable from the grid,
  per-field, journaled, pushed with review.
- The min-time/meridian-window trap surfaced at commit time, without constraining edit order.

**Non-Goals:**
- No hover glyph on the Project column (revisit only if right-click proves undiscoverable in use).
- No `RuleWeights` editing (serialized structure, not a schema column — TS-side surgery).
- No changes to the mosaic parent's dedicated flyout, `filterswitchfrequency` (parked cadence change), or
  project add/delete (user rule: major surgery is a TS function).

## Decisions

### D1 — Trigger: one "Edit project…" menu item, gated by `ProjectTsKey`
`Row_RightTapped` adds the item for every row shape that resolves a project key: `TargetGroupRow` (its
`ProjectTsKey`), `PanelGroupRow` (`Children[0].ProjectTsKey`), `ReconciliationRow` (its own). The mosaic
parent keeps "Edit mosaic project…" as its primary item and gains nothing new (its flyout already covers the
whole-mosaic knobs; the plain project editor is reachable from any of its panels/rows). Flyout title =
`"{Project} — project"` from the row's Project text; opened via the existing `ShowEditFlyoutAsync` with
`TsTable.Project` (its `group`/`row` mirror params null — no project field has an in-grid column, so the
generic `SetTsFieldAsync` path is the only commit route and the mirror rule is trivially satisfied).

### D2 — Cross-field warn lives in the commit callback, not the schema
TS's rule (`ProjectViewVM.Save()`): refuse when `MinimumTime > 2 × MeridianWindow` and `MeridianWindow > 0`.
Here: after a successful commit of either `minimumtime` or `meridianwindow`, evaluate the pair from the
flyout's current values (the seed dictionary updated with each verified commit) and, when invalid, show a
caution line inside the flyout (persistent while invalid, cleared when a later commit fixes the pair) plus
the status line. Never refuse the write — the db takes what the user typed, matching per-field semantics;
TS itself only enforces this at its own Save button, so a warned-but-committed pair is exactly as valid as
one written by TS's API. Schema stays untouched (the rule is a UI-layer courtesy, not a column contract).

### D3 — `state` is a plain enum field (stale gotcha retired)
Verified in the TS source clone: no code path stamps `ActiveDate`/`InactiveDate` on a state transition —
`Project.cs` setters are plain, `ProjectViewVM.Save()` is a plain proxy save, the only date writes are
paste-copy plumbing. The prior session's "TS stamps dates on transitions" note is retired (memory + docs
updated with this change). `state` therefore renders as the standard `ProjectState` dropdown.

## Risks / Trade-offs

- **[Discoverability]** right-click-only is invisible until tried → accepted (user's call); the menu is the
  established secondary gesture and DOMAIN.md documents it.
- **[Stale sibling value in the warn]** the pair is evaluated from seed + in-flyout commits; another session
  editing the sibling concurrently isn't seen → single-user app, local db, negligible.
- **[TS behavior drift]** a future TS version could start using the dates → the source clone is the reference
  we verify against at that point; nothing here writes the date columns, so drift can't corrupt.
