# Proposal: add-target-rename

> **Seeded 2026-08-12 by the ISM session** (proposal only — TSM sessions own specs/design/tasks
> under this repo's conventions). User directive same day: "add the rename verb to tsm."
> Motivating case: TS-authored `Cygnus Loop P9` among 15 `CygnusLoop Pn` siblings; the user has
> **already renamed it in TS's UI on BIRDWATCHER**, which exposed the second gap below. ISM-side
> context: ISM ROADMAP item 2 records the rename requirement and why ISM cannot own renames
> during coexistence (`name` is feed-carried, last-write-wins — an ISM rename silently reverts
> on the next TSM touch of the target).

## Why

TSM's editing surface has no target-rename verb — target names are TS-authored and TSM only
projects them. During the coexistence window TSM is the *only* correct authoring home for a
rename: it writes TS (so NINA's per-target file naming follows), and its export duty already
emits the full-value `target-upsert` that carries the name to Catalog.db. Renaming on
BIRDWATCHER instead (the user's workaround today) leaves the feed blind: the export duty
projects TSM's own writes only, so an externally-authored rename never reaches ISM.

## What Changes

- **Rename verb in TSM's target surface**: edit the target name → journaled edit → reviewed
  push-as-replay → TS write, like any intent edit. The existing feed emit (`target-upsert`,
  full-value) carries the new name to the Catalog inbox with **no contract change** — same op,
  same fields.
- **Design question (settle in design.md): pull-diff emission for externally-authored target
  edits.** The BIRDWATCHER rename case: at pull, diff the prior working copy against the fresh
  pull and emit `target-upsert`s for TS-committed target changes TSM merely observed. Precedent:
  the template mirror was widened 2026-08-12 (TSM design D5) to exactly this posture — "mirror
  TS-committed state, whichever surface changed it" — idempotent, no version bump. Deciding
  *against* it is also acceptable (scope discipline), but then the observed-rename gap stays
  open and should be recorded as an accepted residual on both sides.
- **Immediate data note**: the pending `Cygnus Loop P9` → `CygnusLoop P9` rename is already
  committed in TS. If pull-diff emission ships, the next TSM session emits it automatically;
  if not, one TSM-side touch of that target (or the new rename verb re-committing the name)
  flushes it to the feed.

## Capabilities

*(TSM sessions refine at spec time per this repo's spec organization — likely a delta on the
existing editing/push capability plus, if pull-diff emission is adopted, the export-duty
capability. The inbox contract itself needs no change: same ops, same fields, v1.)*

## Impact

- **TSM**: target grid/dialog gains rename; journal + replay legs extend to the name field;
  export duty possibly gains the pull-diff emitter.
- **TS / NINA**: rename lands in TS via the normal push; future image files carry the new name
  (historical files still resolve — disk matching is coordinate-based).
- **ISM**: no code change — the ingest already applies `target-upsert` renames full-value by
  provenance (live-verified 2026-08-12). ISM's own rename surface stays deferred to its
  planner change (ISM ROADMAP item 2: per-field authored-override + hardening ceremony).
