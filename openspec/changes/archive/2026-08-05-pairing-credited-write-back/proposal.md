# Proposal: pairing-credited-write-back

## Why

The write-back pass and the reconciliation grid disagree about which disk frames belong to which TS plan.
The grid pairs on the full capture-configuration key (gain / offset / binning, since `capture-config-keys`
2026-07-27, plus framing since `rotation-framing-key`), but `WriteBackPlanner`'s credit key is still the
original `(target, filter, purpose, seconds)` — so frames the grid correctly shows as a *separate* disk row
still credit a non-matching plan's bucket. Field-observed 2026-08-04 (obs `acfd`): a freshly adopted
Abell 78 Stars-B plan assigned a gain-0 template kept `acquired`/`accepted` = 18 from 18 gain-53 disk
frames; the pass logged "all 657 auto cells already stamped — nothing to do" because the coarse bucket
matched. Disk is truth in every case: a plan with no corresponding disk files must read 0.

Two adjacent defects surfaced by the same observation: adoption seeds born-complete counts even when the
assigned template would not pair with the source disk cell, and template camera-default sentinels (`-1`)
— which the user defines as errors, never relied upon — are invisible in the grid.

## What Changes

- **Write-back credits by the pairing rule** (Library `WriteBackPlanner`): the credit bucket extends to
  gain / offset / binning with expressed-and-equal semantics; template sentinel `-1` never pairs anything.
  A plan whose bucket is empty stamps `acquired` = `accepted` = 0 via the existing zeroing machinery.
  `desired` behavior is unchanged: ratchet-up kept, never lowered. `UnplannedFrames` notes are kept even
  though strict crediting multiplies them (~245 buckets) — the report must be complete; the user iterates
  report → grid fix → re-read.
- **Adoption seeds by pairing** (app `AdoptionPlanner`): when the assigned template would NOT pair with the
  source disk cell (`WouldPair` is already computed for the caution text), seed `desired` = `acquired` =
  `accepted` = 0 instead of born-complete. When it pairs, born-complete stays as shipped. Applies per-cell
  in the bulk rollup dialog too.
- **`sentinel` badge** (app render): a row-scoped warning badge on every plan row whose template carries a
  camera-default sentinel on `gain` or `offset` (−1) — the fields the authoring convention decides
  explicitly, where the sentinel is the designed representation of an incorrect state — recomputed each
  reconciliation; TSM never auto-corrects the value — the badge tells the user where to hand-fix.
  Sentinels that are a field's designed representation of a correct state never badge (refined mid-verify,
  2026-08-04): template `readoutmode` −1 (TS's blank "camera decides" box — measured live: all 20
  templates carry it), plan `exposure` −1, template `ditherevery` −1.
- **Operational (one-time)**: the first load after this ships stamps the historical backlog (~245 disk
  buckets stopped pairing when gain/offset became keys: broadband gain 53→0 in 2024, narrowband 111 since
  2019, offset-50 frames in every filter) as decreases; the next Push review opens with hundreds of
  `N→0`-style lines. Chosen knowingly — disk is truth, the user manages `desired`/enable by hand afterward.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `write-back`: the credit bucket becomes the grid's pairing key — gain/offset/binning expressed-and-equal
  (sentinel never pairs) in addition to `(target, filter, purpose, seconds)` and the framing-serves rule;
  plans with empty buckets stamp 0.
- `disk-row-adoption`: born-complete seeding becomes conditional on pairing — a non-pairing template
  assignment seeds 0/0/0 (per-cell in both the single-cell and bulk dialogs).
- `reconciliation-grid`: new row-scoped `sentinel` warning badge for template camera-default sentinels
  (gain/offset/readoutmode = −1); exempt sentinels enumerated.
- `ts-ambiguity-report`: sentinel templates become report action items naming the cause — template, the
  sentinel field(s), and the plans using it (added mid-verify from field obs b22d 2026-08-04: the report
  must indicate what caused a `sentinel` badge).

## Impact

- **Library (`..\Library\Astronomy.Catalog`)**: `WriteBackPlanner` credit key + tests. `SingleTargetPlanner`
  (surgical path, library-only capability) should adopt the same crediting so the two paths cannot drift.
- **App (this repo)**: `AdoptionPlanner` seeding + tests; badge computation/render (`Badges` /
  `ReconciliationProjection` layer) + tests.
- **Specs**: deltas to `write-back`, `disk-row-adoption`, `reconciliation-grid`.
- **Data**: no schema change anywhere; TS db values change only through the normal journaled write-back →
  reviewed Push flow. First push after shipping carries the historical settling (hundreds of decreases).
