# framing-overlap-column

## Why

The `framing` badge that shipped 2026-07-29 marks that a row's frames point somewhere the plan did not
ask for — but it says nothing about **how far off**. A 451-frame cluster 10° from the plan may still share
most of its footprint and remain usable; one 50° off shares almost none. Both look identical today, so the
user cannot tell a recoverable framing from a wasted one without opening the frames elsewhere. This is the
one explicit follow-up `rotation-framing-key` left on the table, deferred only because the footprint needed
pixel dimensions nothing exposed.

That blocker turns out to be thinner than recorded. Measurement over the live library (18,650 light frames)
shows the mandatory XISF `<Image geometry>` attribute present on **100%** of frames — `XisfHeaderReader`
simply never reads it, because it harvests only `<FITSKeyword>` elements. Focal length and pixel size are
already exposed and already 100% covered. The footprint is computable now.

## What Changes

- The scan reads each frame's **sensor pixel dimensions** from the XISF `<Image geometry>` attribute, and
  derives each framing cluster's **angular footprint** (field of view in degrees) from geometry, focal
  length and pixel size.
- A framing cluster that is **off the plan's footprint** — turned away from its rotation, pointed away from
  its coordinates, or both — gains an **overlap fraction**: how much of that cluster's own footprint falls
  inside where the plan wanted it, the plan rectangle being the same sensor, centered on the plan target's
  coordinates, rotated to the plan's rotation.
- The `framing` badge **prices itself inline** — `framing 57%` — on the deepest visible line only; rollups
  and summaries keep the bare token. *(Revised from a dedicated grid column, user decision 2026-07-29:
  measurement showed the number serves ~14 rows library-wide in a compressed 57–100% range — badge-sized
  information, not column-sized. See design → surface decision.)* The overlap facts with no badge to
  decorate — off-plan pointings that serve, the mixed-sensor qualifier — go to the **ambiguity report** as
  informational entries.
- **Unreadable frames stop being silent** (group 4a): the count on the load status line when nonzero, each
  path + reason as an action item in the ambiguity report.
- `framing-keys` **reverses its own out-of-scope clause.** Its shipped spec states "The badge marks the
  hazard; quantifying it (footprint-overlap percentage) is deliberately out of scope." That sentence is
  what this change exists to retire.
- **Crediting is explicitly unaffected.** Write-back stays a boolean serve / does-not-serve decision.
  Overlap is diagnostic display only — nothing may later make `acquired` proportional to it.

## Capabilities

### New Capabilities

None. The grid contract for a framing dimension already lives in `framing-keys` (that is where the `Rot`
column's requirement went, not in `reconciliation-grid`), and the rectangle-intersection math is
implementation detail, not separately observable behavior.

### Modified Capabilities

- `framing-keys`: gains the overlap fraction — its definition, when it exists, the always-defined
  comparand, the mixed-sensor case, the column's display and sort exclusion, and the explicit boundary that
  it never affects crediting. Retires the clause scoping overlap out.
- `image-library-scan`: the scan must read each frame's sensor pixel dimensions and carry them so a
  cluster's angular footprint can be derived; states which source is authoritative and that it is a
  contract, not a best-effort read.

## Impact

**Library (`..\Library`, separate repo)**

- `Astronomy.XISF`: `XisfHeaderReader` reads the `<Image geometry>` attribute (today it parses only
  `<FITSKeyword>` elements); `XisfHeader` exposes the pixel dimensions.
- `Astronomy.Core`: a rotated-rectangle overlap helper — pure geometry, caller/consumer-framed, no TSM
  terminology, matching the shared-library discipline.
- `Astronomy.Catalog`: `FramingCluster` carries its footprint; `FramingClusterer` populates it;
  `ReconciliationProjection` computes the overlap where the plan already meets the framing (it is the
  existing home of `FramingDisagrees`).

**App (this repo)**

- `Badges`: the decorated token (`FramingWithOverlap`) + canonicalization so severity/scope classify the
  decorated form as `framing`. `ReconciliationRow`: the fraction on `RowConfig`; `BadgeText` decorates the
  deepest visible line. `ReconciliationLoader`: threads the fraction and carries the scan's `SkippedFiles`
  on `LoadResult`. `AmbiguityReport`: the unreadable-files action section + the framing info entries.
  `MainViewModel.Sync`: the status-line unreadable count.
- **No layout change**: no new column, no window resize — the grid's geometry is untouched.

**Not affected**

- Write-back crediting, unit formation, matching/anchoring, and every existing total. No cell that pairs
  today stops pairing, and no count changes.

**Operational**

- Purely additive to the scan's per-frame read; no stored state, so nothing to reset. The catalog remains
  derived and rebuildable.
