# Design: template-manager

## Context

`TsFieldsEditor` renders any `TsEditableSchema` table; the gate/journal/push path is table-agnostic; the
loaded `CatalogGraph` already carries every template with its TS key (`ImportedFromTsGuid`) and the plans
pointing at it. Templates have no grid rows, so the surface needs a list-first entry (toolbar picker) plus
the pre-anticipated plan-row item. Settled with the user 2026-07-06: toolbar + row item; edit-only.

## Goals / Non-Goals

**Goals:**
- Every user-facing template column editable (18 total; 7 ship today, 11 added), per-field, journaled, pushed
  with review.
- The shared-scope fact ("used by N plans") visible at every entry point.

**Non-Goals:**
- No add/delete/duplicate (TS function, user decision). No `profileId`/`guid` editing (identity).
- No new library reads — the picker lists from the load's graph (a template created in TS mid-session appears
  after the next load, like every other TS-side change).

## Decisions

### D1 — The 11 new schema rows (bounds from the TS source/UI semantics)

| Column | Type | Bounds/Unit | Notes |
|---|---|---|---|
| `twilightlevel` | Enum `TwilightLevel` | — | 0 Nighttime · 1 Astronomical · 2 Nautical · 3 Civil (TS `TwilightCircumstances.cs`); column name per TS EF mapping |
| `minutesoffset` | Whole | −720…720 min | shifts the twilight window; negative allowed |
| `moonavoidanceenabled` | Bool | — | master switch for the avoidance suite |
| `moonavoidanceseparation` | Real | 0…180 ° | Lorentzian separation at full moon |
| `moonavoidancewidth` | Whole | 0…30 days | Lorentzian width |
| `moonrelaxscale` | Real | 0…10 | 0 disables relax |
| `moonrelaxmaxaltitude` | Real | −90…90 ° | relax ceiling |
| `moonrelaxminaltitude` | Real | −90…90 ° | relax floor (TS default −15 — negative is normal) |
| `moondownenabled` | Bool | — | require moon below relax-min altitude |
| `ditherevery` | Whole | 0…999 | template-level dither cadence |
| `maximumhumidity` | Real | 0…100 % | 0 = disabled |

All `CadenceSafe: true` — template columns feed scoring/filtering; TS clears `FilterCadenceItem` only on plan
enable/fsf changes. UI order: the table above (twilight → moon suite → dither → humidity), appended after the
existing 7.

### D2 — Picker: a toolbar Flyout listing the graph's templates
Toolbar button **"Templates…"** (after Pull now) opens a flyout hosting a simple list built from
`_lastLoad.Graph`: one line per template — `Name · Filter — used by N plan(s)` — ordered by name (natural).
Clicking a line closes the picker and opens the standard editor flyout anchored at the button. The VM exposes
`ListTemplates()` → `(TsKey, Name, Filter, UsedByPlans)` records and keeps no template state (recomputed per
open from the last load; empty/no-load → the button shows a "load first" status note). Templates whose
`ImportedFromTsGuid` is null (never — resolver always carries it for TS-sourced templates) are skipped
defensively with a log line.

### D3 — Plan-row item resolves the template through the graph
"Edit template…" appears in a filter row's menu when the row has a `PlanTsKey` and the VM can resolve it:
plan (`ImportedFromTsGuid == PlanTsKey`) → `ExposureTemplateId` → template. Exposed as
`TryGetTemplateForPlan(planTsKey)` returning the same record shape as D2 — no `ReconciliationRow` change.

### D4 — Blast radius in the title, everywhere
Both entry points open `ShowEditFlyoutAsync(TsTable.ExposureTemplate, key, $"Template '{name}' — used by
{n} plan(s)", null, null)`. The generic commit path (`SetTsFieldAsync`) already journals with that label, so
the push review carries the same scope statement.

## Risks / Trade-offs

- **[Stale used-by count]** counts come from the last load; an in-session `desired`/enable edit doesn't change
  plan→template edges, so staleness is limited to TS-side edits since the pull — acceptable (the count is a
  scope hint, not a contract).
- **[`filtername` edits re-key write-back]** already-shipped field, now reachable: renaming a template's
  filter reclassifies its cells at the next resolve. The blast-radius title + DOMAIN note carry the warning;
  no guard (legitimate operation).
- **[minutesoffset bounds]** TS's UI doesn't publish hard bounds; ±720 is a sanity clamp, not TS semantics —
  noted in the row's `Notes` so a legit out-of-range need is a one-line bound change.
