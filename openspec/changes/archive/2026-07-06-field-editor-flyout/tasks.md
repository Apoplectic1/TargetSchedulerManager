# Tasks: field-editor-flyout

Two-repo change (group 1 edits `..\Library` — session needs `--add-dir ..\Library`; library and TSM commits
stay separate, each with its docs updated in the same commit as the code).

## 1. Library — enum value maps (`TsEditableSchema.cs`)

- [x] 1.1 Add the declarative enum map: `EnumValues(string enumName)` → ordered `(int Code, string Label)` list for `TargetPriority` (−1 Default…), `ProjectState`, `ProjectPriority`; empty for unknown names; consumer-neutral doc comments pinned to the TS source enums
- [x] 1.2 Tests: every `TsFieldType.Enum` field's `EnumName` resolves to a non-empty map; `TargetPriority` includes −1 "Default"; unknown name → empty
- [x] 1.3 Build library + run `Astronomy.Catalog.Tests`; update Library docs in the same commit (`6a2cabf`, 156/156 green)

## 2. TSM — read-seeding path

- [x] 2.1 Add `ReadFieldsAsync(TsTable, key)` to `TsEditGate` (editor `ReadField` per schema field, off the UI thread, current source path); extend `ITsEditor` seam accordingly (+ `IsFieldAvailable` so drifted dbs omit fields instead of failing the form)
- [x] 2.2 Seam tests: seeding returns db values; read failure yields an error outcome (no fabricated defaults)

## 3. TSM — `TsFieldsEditor` control

- [x] 3.1 Build the generated form: schema-ordered controls by `TsFieldType` (ToggleSwitch / NumberBox int / NumberBox decimal with Min/Max clamp / ComboBox from enum map / TextBox), `Unit` suffix, `Notes` tooltip, cadence-breaking fields excluded via `IsCadenceBreaking` (`Controls/TsFieldsEditor.cs`)
- [x] 3.2 Wire per-field commit: change/focus-loss → `ApplyAsync` with audit label; success keeps value + refreshes in-grid mirrors (`ApplyDesired`/recompute, active checkbox now `Mode=OneWay` + `ApplyEnabled`); refusal/failure reverts control + surfaces existing wording
- [x] 3.3 Loading/error states: form appears only after seeding; read failure shows error, no controls

## 4. TSM — row triggers + flyout hosting

- [x] 4.1 Hover edit glyph on target group rows (`TsTargetKey` gated) and filter rows (`PlanTsKey` gated); DOMAIN.md "add a UI element" checklist
- [x] 4.2 Right-click `MenuFlyout` per row type ("Edit target…" / "Edit exposure plan…"), items gated by key presence, structured for future appends (template, cadence actions)
- [x] 4.3 Host `TsFieldsEditor` in a `Flyout` anchored at the row, titled with entity identity; confirm dismissal never blocks (per-field commit) and scroll/expansion survive open/close

## 4b. Mosaic special case (added during the visual pass, user decision 2026-07-06)

- [x] 4b.1 Plumb `ProjectTsKey` (library `TargetCells`) through the loader into rows; `TargetGroupRow.ProjectTsKey`/`IsMosaic`, `PanelGroupRow.TsTargetKey`
- [x] 4b.2 Mosaic-parent flyout: "Enable all panels" (VM `SetMosaicEnabledAsync` fan-out + `GetMosaicEnabledState` tri-state) + project priority (one `project.priority` write); glyph/menu on mosaic parents
- [x] 4b.3 Panel mini-header rows: standard target glyph/flyout ("Edit panel target…")

## 5. Verify + docs

- [x] 5.1 Build + all tests ✔ (final: 0 warnings, 91/91 App.Tests, 160/160 lib) — visual pass done on LOCAL across four USER_OBS sessions (glyphs, menus, seeding, edits, sentinel, mosaic, rotation guard — all "works"-confirmed); LIVE-mode spot-check accepted-by-use at archive time (same guarded gate `desired` already proved live in NINA)
- [x] 5.2 Update TSM `ARCHITECTURE.md` (flyout editing seam), `ROADMAP.md` (priority editing shipped; Parts 2/3 queued), `DOMAIN.md` (trigger/flyout conventions) — same commit as the TSM code
