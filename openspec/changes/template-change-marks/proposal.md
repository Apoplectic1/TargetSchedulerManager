# template-change-marks — Proposal

## Why

A template edit (e.g. enabling moon avoidance on 'H900', used by 49 plans) journals and pushes correctly,
but shows **no directional mark anywhere in the grid** — the user toggled a template field, saw "2 unpushed"
in the sync badge, and found no `→` in column 0 (USER_OBS d14e, 2026-07-26). Worse, a template changed on
the rig side is invisible entirely: the pull diff does not cover the `exposuretemplate` table, so `←` can
never appear for it — while a plan *reassigned* to a different template does mark (the plan's
`exposureTemplateId` is diffed). The original carve-out ("template edits mark no row") traded honesty for
quiet; the user has now explicitly reversed it: **all flyout changes show directional arrows in column 0,
including changes made on NINA/TS**.

## What Changes

- **Reverses a spec decision**: `edit-direction-marks`' "Exposure-template edits SHALL mark no row"
  requirement is replaced — a template change now marks **every plan row using that template** (and rolls
  up to headers like any plan-scope change).
- Outbound: the marks sweep resolves `TsTable.ExposureTemplate` journal entries onto plan rows via the
  graph's plan→template mapping (each `ExposurePlan.ExposureTemplateId` → template TS key).
- Inbound: the pull-time field diff adds the `exposuretemplate` table, mirroring the `TsEditableSchema`
  editable column set (18 columns: name, filtername, gain, offset, bin, readoutmode, defaultexposure,
  twilightlevel, minutesoffset, the six moon-avoidance fields, moondownenabled, ditherevery,
  maximumhumidity).
- Tooltips attribute inherited changes: rows marked via a template read "template '<name>': <field> old → new",
  distinguishable from direct row edits; header count summaries include template-derived fields.
- **Header tooltips gain attribution for their own-scope fields** (user decision 2026-07-26, second round):
  target-scope and project-scope pending fields — which mark the header only, never child rows — list
  attributed old→new lines ("project '<name>': minimumaltitude 30 → 45") instead of bare direction
  counts; fields rolled up from plans/templates beneath stay summarized as counts (their detail lives on
  the leaf rows).
- The Templates… picker list shows the row's `←`/`→`/`⇄` mark beside each changed template's name.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `edit-direction-marks`: the "Non-marking rows are explicit" carve-out for templates is replaced by
  template-scope marking (outbound via journal + graph mapping, inbound via a new `exposuretemplate` diff
  table set, attribution in tooltips, header rollup); header tooltips list attributed old→new lines for
  their own target/project-scope fields (counts remain for rolled-up plan/template fields).
- `target-and-plan-flyouts`: the Templates… picker rows additionally surface the sync-direction mark for
  templates with pending inbound/outbound changes.

## Impact

- `TargetSchedulerManager.App\Services\SyncMarks.cs` — template key space + plan→template map; tooltip
  attribution lines; header field counting.
- `TargetSchedulerManager.App\Shared\TsInboundDiff.cs` — `exposuretemplate` added to `FieldSet`.
- `TargetSchedulerManager.App\ViewModels\MainViewModel.Sync.cs` (marks sweep call sites) — pass templates
  into `SyncMarks.Build`.
- `TargetSchedulerManager.App\MainWindow.Flyouts.cs` — Templates… picker rows gain the mark glyph.
- Tests: `SyncMarks`/inbound-diff/marks-sweep coverage in `TargetSchedulerManager.App.Tests`.
- No library (`Astronomy.Catalog`) change expected — the editable column list is already published by
  `TsEditableSchema`.
