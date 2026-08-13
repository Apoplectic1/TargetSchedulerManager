# Proposal: add-inbox-v2-emission

## Why

The ISM inbox contract moves to v2 (ISM change `add-inbox-contract-v2` — the paired half of one
coordinated v-bump; the contract authority is ISM's `catalog-inbox-contract.md`). v1 accepted
that project settings, mosaic structure, and the template moon-relax triplet never travel —
field-hit 2026-08-12 when a pushed `minimumaltitude 0→30` reached TS but not Catalog.db. TSM's
emitter is the writer half: the same ops widen, the envelope becomes `v: 2`, and everything TSM
emits it already holds in the local working copy — no new data source, no new emission point.

## What Changes

- **Envelope**: records carry `v: 2`.
- **`project-upsert` widens**: the settings block (`minimum_time_minutes`,
  `minimum_altitude_deg`, `maximum_altitude_deg`, `use_custom_horizon`, `horizon_offset_deg`,
  `meridian_window_minutes`, `filter_switch_frequency`, `dither_every`, `smart_exposure_order`)
  plus `is_mosaic` — full committed values from the local working copy, as with every v1 field.
- **`exposure-template-upsert` widens**: the moon-relax triplet (`moon_relax_scale`,
  `moon_relax_max_altitude_deg`, `moon_relax_min_altitude_deg`) joins the mirror.
- **Sentinel translation extends**: TS sentinels in the new fields translate to JSON `null`
  exactly as the one-time import translated them (`minimumaltitude 0.0 → null`,
  `maximumAltitude 0.0 → null`, remainder per the contract's v2 table, transcribed from AL's
  `TsIntentImporter`). Boundary translation per the contract, not a fallback.
- **Observed emission extends to project rows** (user decision 2026-08-13 — the v2 lane the
  `add-target-rename` spec pointed project rows at): the existing pull-diff pass additionally
  correlates **project-table** rows across the pull and emits a full-value v2 `project-upsert`
  for each existing row with observed field changes — so settings edited in TS's UI on
  BIRDWATCHER flow to the store. Same narrow posture as targets: existing rows only
  (remotely-added projects stay silent), plan/template rows stay silent, no origin bookkeeping
  (project columns are user-authored by construction — verify at implementation that neither
  TS's runtime nor TSM's write-back writes project rows).
- **No new ops**: push funnel unchanged; `target-upsert` / `exposure-plan-upsert` unchanged;
  the actuals/ratchet exclusion unchanged.

## Capabilities

### Modified Capabilities

- `catalog-export`: emitted ops carry the contract v2 field sets under the v2 envelope;
  sentinel-translation table extends to the new fields.

## Impact

- **This repo**: emitter record types + envelope constant + fixture tests; docs (ROADMAP note —
  TSM stays "to bed" after this one duty rider ships).
- **ISM**: paired change `add-inbox-contract-v2` (ingest accepts v2 only). **Ship together** —
  neither side is releasable alone; install procedure drains the inbox on current builds before
  the pair goes in (ISM design D3).
- **Store / AL**: none — TSM still never opens `Catalog.db`.
