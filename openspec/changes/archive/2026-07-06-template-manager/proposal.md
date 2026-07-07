# Proposal: template-manager

## Why

Exposure templates carry the scheduling knobs the user actually tunes seasonally — twilight level, the moon
avoidance suite, humidity — but TSM can't touch them: only 7 of the 18 user-facing template columns are in
`TsEditableSchema`, and no UI surface reaches `TsTable.ExposureTemplate` at all. Part 3 of the editing
surface (queued 2026-07-06); anchor settled today: toolbar picker **and** a plan-row menu item.

## What Changes

- **Library (`Astronomy.Catalog`, separate repo/commit):** 11 new `TsEditableSchema` exposuretemplate rows —
  `twilightlevel` (new `TwilightLevel` enum map: 0 Nighttime · 1 Astronomical · 2 Nautical · 3 Civil, from the
  TS source; column name verified against TS's EF mapping `twilightlevel_col → "twilightlevel"`),
  `minutesoffset`, the moon suite (`moonavoidanceenabled`, `moonavoidanceseparation`, `moonavoidancewidth`,
  `moonrelaxscale`, `moonrelaxmaxaltitude`, `moonrelaxminaltitude`, `moondownenabled`), `ditherevery`,
  `maximumhumidity` — all cadence-safe (template columns are scoring/filter inputs; nothing touches
  `FilterCadenceItem`). Bounds/units/notes in the design. The generated form lights them up with zero UI code.
- **Toolbar "Templates…" picker:** lists every template from the loaded graph (name · filter · used-by-N-plans),
  including templates no visible plan uses; picking one opens the standard schema-generated flyout.
- **Plan-row "Edit template…"** in the right-click menu (the extension point anticipated it): resolves that
  plan's template through the graph — no row-model change.
- **Shared blast radius is always on screen:** the flyout title reads "Template '<name>' — used by N plan(s)";
  a template edit affects every plan using it, so the scope is stated, not implied.
- **Edit-only** (user decision: add/delete/duplicate templates is major surgery = a TS function).
- Commits ride the existing gate → journal → reviewed push; `gain`/`offset`/`readoutmode` −1 sentinels already
  render as "camera default" checkboxes.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `target-and-plan-flyouts`: adds the template editing surface (toolbar picker + plan-row trigger + the
  used-by-N blast-radius title rule).

## Impact

- **Two repos:** `..\Library\Astronomy.Catalog\TargetScheduler\TsEditableSchema.cs` (+ its
  `TsEditableSchemaTests`) — committed in the Library repo; TSM app (toolbar button + picker, row menu item,
  VM template list/resolution) — committed here. TSM's cross-repo ProjectReference picks the lib up directly.
- **Template list source:** `LoadResult.Graph.Templates` (already retained per load) — key =
  `ImportedFromTsGuid`, used-by = plans grouped by `ExposureTemplateId`. No new library read.
- **Tests:** library — new rows present/typed/bounded + enum map; app — template list + plan→template
  resolution over an injected graph (builders exist from the write-back tests), journal seam for a template
  field. Picker/menu visuals are user-verified.
- **Risk surfaced:** editing `filtername` on a template changes its purpose classification at the next
  resolve (it re-keys write-back cells) — the field already ships today via the schema; the picker just makes
  it reachable. Noted in DOMAIN with the blast-radius convention.
