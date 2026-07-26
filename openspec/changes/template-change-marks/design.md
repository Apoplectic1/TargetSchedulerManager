# template-change-marks — Design

## Context

Direction marks (`←`/`→`/`⇄`, column 0) are resolved per sweep by `SyncMarks` (App\Services\SyncMarks.cs)
from two fact sources: the journal (outbound) and the session inbound store (pull-time diff,
App\Shared\TsInboundDiff.cs). The sweep (`MainViewModel.Edits.RefreshAllMarks`) resolves leaf rows by their
1:1 plan key (`ForPlan`) and headers by key union (`ForKeys`). Template edits journal under
`TsTable.ExposureTemplate` — a key space the resolver never queries — and the inbound diff's `FieldSet`
omits the `exposuretemplate` table entirely. Result: a template change (local or rig-side) shows no mark
anywhere, while the sync badge counts it (USER_OBS d14e).

Key facts the design leans on:
- A template's TS key is its **integer `Id` as a string** (`TargetResolver.cs:105` — templates carry no
  usable guid provenance), the same key the journal and edit flyout already use (`TemplateInfo.TsKey`).
- The graph retained per load (`_lastLoad.Graph`) holds `Plans` (with `ExposureTemplateId`) and
  `Templates` (with `ImportedFromTsGuid` + `Name`) — the plan→template mapping is fully derivable at
  `SyncMarks.Build` time, covering plans folded into rollup cells (which carry no row-level key), exactly
  as the existing target→plan-key map does.
- `TsEditableSchema.For(TsTable.ExposureTemplate)` publishes the 18 editable columns — the agreed inbound
  diff set.

## Goals / Non-Goals

**Goals:**
- A template change marks every plan row using that template (`→` outbound / `←` inbound / union `⇄`),
  rolls up to headers, and attributes itself in tooltips ("template '<name>': …").
- Rig-side template changes become visible: the pull diff covers `exposuretemplate` mirroring the
  editable schema.
- The Templates… picker shows each template's own mark.

**Non-Goals:**
- No new mark glyphs or colors — the existing three-glyph language is reused.
- No write-back masking for templates (masking is about disk-derived `acquired`/`accepted` — plans only).
- No template create/delete/duplicate detection (edit-only surface, unchanged).
- No library (`Astronomy.Catalog`) changes.

## Decisions

**D1 — Resolve template marks inside `SyncMarks`, not on rows.** `Build` takes the retained graph
(nullable, pre-load = no maps): `Build(journal, inbound, graph)` — replacing the plans-only parameter. It
derives the private maps: planKey→templateKey, templateKey→display name, targetId→distinct templateKeys,
plus targetKey→name and projectKey→name (for D7 attribution) and the existing target→plan-key map.
`ForPlan` then unions the plan's own entries with its template's entries; rows stay dumb (`ApplyMark`
unchanged, no new row state). *Alternative rejected:* threading `TemplateTsKey` through
`ReconciliationRow` — the cell already drops it for folded rollups, and header rollup would still need
the graph map anyway.

**D2 — Header counting: a template field counts once per header, not once per affected plan.** `ForKeys`
unions the distinct template keys reachable from its target ids (+ row-carried plan keys), and counts each
pending (template, field) once. A header over six H900 plans with one template field pending reads
"1 field(s) unpushed" — the header summarizes pending facts; the per-row lines carry the multiplicity.

**D3 — Tooltip attribution.** Template-derived lines read
`→ unpushed — template 'H900': moonavoidanceenabled Off → On` (inbound:
`← BIRDWATCHER — template 'H900': …`), keeping the existing line grammar but inserting the template
name so an inherited mark is never mistaken for a row-level edit. Name lookup falls back to the raw key
if the graph lacks the template (defensive display only, not a contract guard).

**D4 — Inbound diff mirrors the editable schema by derivation, not by a second hand-written list.** The
`FieldSet` entry is `(TsTable.ExposureTemplate, "Id", TsEditableSchema.For(ExposureTemplate) columns)`.
Zero drift when the editable schema grows; the diff's existing skip-absent-columns rule handles TS schema
drift. Key column `Id` matches the journal's Id-string key space (D-key parity is what makes marks line
up). *Alternative rejected:* literal column array like the other tables — a second copy of an
18-column list whose divergence would silently split ← from → coverage.

**D5 — Templates… picker marks come from a fresh `SyncMarks` build.** A new `ForTemplate(tsKey)` returns
(glyph, tooltip) for one template key; the VM exposes it for `ListTemplates` consumers (built once per
picker open — the sweep's instances are transient by design and build cost is trivial). Each picker item
prefixes its glyph and carries the old→new tooltip.

**D6 — Spec reversal is explicit.** The `edit-direction-marks` "Non-marking rows are explicit" requirement
loses its template carve-out (disk-plane rows stay non-marking); the pull-diff requirement's field-set
wording adds `exposuretemplate`. Both are delta-spec'd — this documents a deliberate reversal of the
2026-07 design decision, per the user's direction (USER_OBS d14e).

**D7 — Header tooltips: attributed lines for own-scope fields, counts for rolled-up fields.** (User
decision 2026-07-26, second round: project flyout changes covered as header-only + attribution.) Target-
and project-scope fields mark the header *only* — no child row carries their detail — so the header
tooltip is their one home and lists attributed old→new lines: `→ unpushed — project 'Nebulae - Above 45':
minimumaltitude 30 → 45`, `← BIRDWATCHER — target 'M 81': rotation 0 → 90`. Plan- and template-scope
fields keep the existing direction-count summary at headers (their old→new detail lives on the leaf
rows). The model: detail lives where the mark is authoritative; counts summarize what is detailed
elsewhere. *Alternative rejected:* project changes lighting every child row like templates — the user
chose header-only; a project field is one fact about one entity, not an inherited per-plan behavior
change.

## Risks / Trade-offs

- [~49 rows light up from one template toggle] → Intended and confirmed by the user (honest blast
  radius); tooltip attribution (D3) plus the flyout's "used by N plan(s)" title keep it explainable.
  The sync badge counting fields (2) while dozens of rows mark is understood.
- [Header "N field(s)" count no longer equals sum of child-row lines] → D2 is deliberate (facts, not
  multiplicity); tooltip wording stays "field(s)", which is accurate.
- [`RefreshAllMarks` before any load: no templates] → Same shape as today's `plans ?? []` — no rows exist
  to mark; the picker is also unavailable pre-load.
- [Derived diff columns include `name`/`filtername`] → A rig-side template rename marks every user of the
  template `←`. Correct per "all changes made on nina/ts"; noted so it isn't read as a false positive.

## Migration Plan

None — in-memory session state and display only; no persisted format changes. (Journal sidecars from
older sessions already carry `ExposureTemplate` entries; they simply start resolving.)

## Open Questions

None — scope decisions (arrow on all affected rows, attribution, editable-schema mirror, picker mark)
were settled with the user 2026-07-26.
