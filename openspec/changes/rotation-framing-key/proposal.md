# Proposal: rotation-framing-key

## Why

A target's frames can span more than one **framing** — the (sky-rotation, field-center) pair that decides
whether frames share a footprint and can integrate together. Today the grid sums all framings into one
number, which both overstates usable depth and hides integration hazards. Measured against the live library
(2026-07-29 spike, 18,650 frames): six TS-comparable targets carry multiple framings (Barnard 202 has only
28 of 479 frames at the planned rotation; Sh2-101 Tulip's *majority* — 199 of 285 — is an old framing),
~5 more are detectable in mechanical-angle space, and two targets hide single-frame stray framings (M100's
one 135° frame among 104 at 0°) — exactly the "good, low-count, off-footprint reference frame" that can
poison a whole PixInsight integration, invisible today. This was deferred deliberately from
`capture-config-keys` (2026-07-27); the spike has since answered the open questions that forced the deferral.

## What Changes

- The disk plane gains a **framing** dimension: within a scan unit, frames group into framing clusters by
  sky rotation **folded mod 180°** (a 180° pier-flip is the same footprint — geometrically identical, and
  confirmed by every measured flip pair sitting ≤ 0.12° from its counterpart's centroid) plus a
  **per-cluster centroid** check, so a translated stray (M97's one frame 1.45° off-center at the *same*
  rotation) separates too. Framing = (center, angle) — one concept, not two features.
- Each framing cluster carries its own plate-solved centroid and its rotation expression: a **sky angle**
  (comparable to TS), a **mechanical-only angle** (POSANGLE; real rotation the plan cannot be compared
  against — the measured sky−mech zero-point drifts 19–35° across sessions, so conversion is unreliable
  and is not attempted), or none.
- The pairing rule extends: rotation joins the shared-key test **when both planes express it** — a disk
  cluster's sky angle vs the TS target's `rotation`, compared fold-180 within tolerance. The cluster
  agreeing with the plan pairs into `Both`; other clusters render as separate Disk rows. Mechanical-only
  clusters never fail the pairing test on rotation (the camera precedent: expressed by one plane only).
- The grid shows each row's framing (rotation value or mechanical/unknown marking), with rollup treatment
  consistent with the capture-config columns, and a warning-severity row-scoped `framing` badge on disk
  rows whose framing disagrees with the plan — stray framings become findable via the badge filter.
- **Write-back credits only serving frames** (added in-flight, user decision 2026-07-29): both planners
  count a frame toward a plan's `acquired` only when its framing serves the target's rotation — the same
  shared rule the pairing test uses — so a re-framed target stamps its true progress (possibly 0) and TS
  schedules the full re-shoot instead of believing the old framing's frames still count. The surgical
  path surfaces a withheld cell with its reason.
- Tolerances are constants, not knobs: the spike shows real framings sit ≥ 9° apart with ≤ 0.2°
  within-framing jitter, so any grouping tolerance in 1–5° yields identical clusters on the real library.

**Deferred, deliberately out of scope**
- **Overlap-% as a displayed diagnostic** (footprint intersection of a stray cluster vs the plan framing).
  Needs image pixel dimensions that `XisfHeader` does not expose today; the rows themselves carry the core
  diagnostic. Can land later as a column addition without rework.
- **Any WBPP-side enforcement.** WBPP is PixInsight JavaScript, cannot load AL, and reads
  `OBJCTROT`/`POSANGLE` from frames itself — a separate proposal in the PixInsight-scripts lane with zero
  AL/TSM/XFM surface. TSM detects; it exports nothing for WBPP.
- **Per-camera/per-session mechanical→sky conversion** — measured unreliable exactly where it would matter.

## Capabilities

### New Capabilities
- `framing-keys`: framing (fold-180 sky rotation + cluster centroid) as a disk-plane grouping dimension and
  a conditional pairing dimension against the TS target's rotation; flip merging, translated-stray
  separation, mechanical-only expression handling, and the grid presentation of framing.

### Modified Capabilities
- `capture-config-keys`: the disk-bucket identity gains the framing cluster; the `Both` pairing rule's
  shared-key enumeration gains rotation (as expressed — sky-angle clusters only).
- `image-library-scan`: the scan now reads each frame's rotator sky angle, mechanical position angle, and
  plate-solved coordinates, and publishes per-cluster centroids instead of only one consensus centroid per
  unit.
- `write-back`: stamped `acquired` counts only frames whose framing serves the target's rotation (the
  shared serving rule); non-serving cells on the surgical path surface with a stated reason.

## Impact

- **`..\Library\Astronomy.Catalog`** (sibling repo): `ImageLibraryScanner` (read rotation/coords into
  frame readings; framing clustering; aggregate grouping), `FilterAggregate`/`TargetReport` (framing fields,
  per-cluster centroid), resolver/projection surface consumed by the app. `Astronomy.XISF` already exposes
  `RotatorSkyAngleDeg`/`RotatorPosAngleDeg`/`RaDegrees`/`DecDegrees` — **no XISF change**.
- **TSM app**: pairing predicate, grid row rendering (framing display, rollups, badges), row sort untouched
  (capture-config columns stay excluded from sort; framing follows).
- **Tests**: library scanner/clustering fixtures (synthetic — flips, strays, mech-only), app pairing tests.
- **Also affected** (in-flight addition): `WriteBackPlanner` + `SingleTargetPlanner` credit by the shared
  serving rule (`FramingCluster.ServesPlanRotation` — one home for pairing, the badge cue, and crediting).
- **Unaffected**: Hours/Remaining totals (aggregates sum components, as in capture-config);
  sync model; `Catalog.db` (derived, rebuildable); no migration (rule: none ever).
