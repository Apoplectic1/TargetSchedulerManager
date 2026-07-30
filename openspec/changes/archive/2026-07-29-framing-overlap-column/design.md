# Design: framing-overlap-column

## Context

See `proposal.md` → Why. The framing machinery already exists end to end:
`FramingClusterer` partitions a unit's frames into clusters (rotation expression → fold-180 angle group →
field-center group), `FilterAggregate` exposes the cluster it belongs to, and `ReconciliationProjection` is
where a plan target's rotation meets a cluster and produces `FramingDisagrees` via
`FramingCluster.FoldDelta` / `RotationToleranceDegrees`. That makes the projection the only place with both
comparands in hand, so the overlap number belongs there — the scan side only needs to carry the footprint.

Everything below is calibrated by a measurement pass over the live library (18,650 light frames, the same
corpus as the `rotation-framing-key` spike). The numbers that shaped decisions:

| Fact | Measured |
|---|---|
| `<Image geometry>` attribute present | **18,650 / 18,650 (100.0%)** |
| `NAXIS1`/`NAXIS2` present | 18,587 / 18,650 (99.7%) |
| geometry vs NAXIS disagreement | **0 frames** |
| `FOCALLEN`, `XPIXSZ`, `YPIXSZ` present | 100.0% |
| Distinct sensors | Z183 5496×3672 (13,496 + 2,947 binned) · Z533 3008×3008 (2,144) |
| Targets spanning both sensors | 5 — IC 405, M51, M81, Mosaic-Andromeda, Mosaic-Pinwheel |
| …of those, having a TS target at all | **1 (M81)** |
| TS targets carrying a rotation | 78 / 101 |

## Goals / Non-Goals

**Goals:**
- Price the hazard the badge already marks, with a number that is **always defined** wherever the badge is.
- Keep the number about *framing error alone* — sensor differences must not leak into it.
- Add the column by the same route `Rot` took, so the grid gains a cell and nothing else changes.

**Non-Goals:**
- Any change to crediting. Write-back stays boolean serve / does-not-serve (see Decisions → last entry).
- Full WCS handling. No `CD` matrix, no distortion, no per-frame astrometric solution — cluster centroids
  and a tangent plane are the resolution this number needs.
- Deciding what to *do* about a low overlap. Per `DOMAIN.md`, TSM detects, WBPP enforces, XFM neither.
- Per-frame overlap. The cluster is the unit, matching every other framing behavior.

## Decisions

### Pixel dimensions come from the XISF `<Image geometry>` attribute, not `NAXIS1`/`NAXIS2`

Geometry is present on 100% of frames and mandatory in the XISF spec; `NAXIS` covers 99.7% (63 frames lack
it) and never disagrees where both exist. Choosing geometry means the read is a **contract** — absent
geometry is a malformed XISF and aborts per the fail-fast rule — rather than a 99.7% read needing a fallback
for the remaining 63 frames. Alternative rejected: read `NAXIS` and fall back to geometry. That is two code
paths and a defensive branch for a case the better source doesn't have.

Cost: `XisfHeaderReader` currently parses only `<FITSKeyword>` descendants, so it gains a read of the
`<Image>` element's `geometry` attribute (`width:height:channels`).

### A frame with no geometry is a corrupt file, and corrupt files stop being silent

Geometry is mandatory, so a frame lacking it is malformed — the same category as the malformed-XML files the
reader already documents. `ImageLibraryScanner` already handles that category: it catches the read failure and
records it in `ImageLibraryReport.SkippedFiles` rather than aborting a whole-library scan for one bad file.
Missing geometry therefore reuses that path, and the reader simply throws `InvalidDataException` as it does for
a bad signature.

An earlier draft of the `image-library-scan` delta demanded the scan **abort**. That was wrong on two counts:
it conflated a corrupt file (an environment fact) with a workflow-bug contract violation, and it was
unimplementable as written — the scanner's catch would have swallowed the throw into a silent skip, producing
exactly the opposite of the requirement.

Implementing it surfaced a **pre-existing hole**, unrelated to this change: nothing in TSM reads
`SkippedFiles`. An unreadable frame today lowers the Actual count with no indication anywhere — a quietly
wrong total, which is the failure this capability's written-down exclusions exist to prevent. Since missing
geometry joins that category, the category is made honest here: the unreadable count is surfaced where the
scan's results are already reported, and shows nothing when it is zero. Scope addition approved by the user
(2026-07-29) rather than assumed.

Alternative rejected: a distinct exception type the scanner's catch deliberately lets through, so a corrupt
file aborts the load. Strictest fail-fast, but it makes one bad file among 18,650 withhold the entire grid,
and measurement found zero unreadable frames today — the cost lands only in the rare real case, and lands as
total unavailability.

### Plate scale is `206.265 × XPIXSZ / FOCALLEN` — no binning multiply

**`XPIXSZ` is already binning-adjusted.** Measured: Z183 at bin 1 reports 2.40 µm with 5496×3672; the same
camera at bin 2 reports **4.80 µm** with 2744×1836. Both yield the same field — 1.423° × 0.951° vs
1.421° × 0.951°. Multiplying by `XBINNING` would double the field for the 2,947 bin-2 frames (15.8% of the
library), and those older bin-2 frames are exactly the history most likely to hold strays. The binning
keywords are therefore **not** inputs to the footprint at all.

### The comparand is the stray's own sensor at the plan's framing

The plan expresses rotation and coordinates but **never a sensor**, so "the planned footprint" has no
intrinsic size and one must be supplied. The plan rectangle is built from **the disagreeing cluster's own
sensor**, centered on the plan target's coordinates, rotated to the plan's rotation. The reported fraction
is `area(cluster ∩ planRect) / area(cluster)` — how much of these frames' own footprint landed where the
plan asked.

Two alternatives were considered and rejected (user decision, 2026-07-29):

- **Against the serving cluster's measured footprint.** Answers "can I stack these with what I already
  have" most directly, but is **undefined when no cluster serves** — a target re-framed before anything was
  shot at the new angle has badges and no comparand — and for M81 it would compare a square sensor against
  a 3:2 one, so sensor shape would enter the number.
- **Plan area as the denominator.** Reports coverage of the planned field, but a small stray sitting
  entirely inside the plan reads as an alarming low percentage when it is entirely usable.

The chosen form is always defined, isolates framing error from sensor mismatch, and prices exactly the
comparison `ServesPlanRotation` and write-back already make — which keeps the badge, the number, and the
crediting decision describing one thing.

### Same-sensor construction makes the number pure framing error

Because both rectangles use the same dimensions, the fraction depends only on the **center offset and the
angle difference**. A cluster perfectly on the plan's rotation but pointing elsewhere scores by its
translation; one perfectly centered but rotated scores by its angle. Neither is contaminated by which
camera took the frames — which matters because camera is deliberately not a reconciliation key, so a
cluster's camera composition is not something the user chose per row.

### Tangent-plane projection with RA scaled by cos(dec)

At ~1.4° fields a gnomonic tangent plane about the plan target's position is accurate far beyond the
precision of a percentage. **RA offsets must be scaled by cos(dec)**: M81 sits at Dec +69.13° where
cos(dec) = 0.355, so treating RA degrees as sky degrees would inflate its east-west offsets ~2.8× and make
the intersection meaningless. This is the single most likely way to get a plausible-looking wrong number,
so it belongs in a test with a high-declination case, not only in a comment.

Note the existing unit mismatch to respect: `FramingCluster.CentroidRaHours` is in **hours**, Dec in
degrees — the Library convention. Conversion happens at the geometry boundary.

### Rectangle intersection lives in `Astronomy.Core` as pure geometry

Convex-polygon clipping (Sutherland–Hodgman) of one rotated rectangle by the other, then the shoelace area.
Both shapes are convex, so the intersection is convex and clipping is exact — no sampling, no Monte Carlo,
deterministic and unit-testable against hand-computed cases (identical rectangles → 1.0; disjoint → 0.0;
90° rotation of a square about its center → 1.0; a known partial overlap → a closed-form value).

`Astronomy.Core` is the home because it already hosts the night-window and visibility math TSM consumes and
this is consumer-agnostic geometry. Per the shared-library discipline it takes caller/consumer framing and
no TSM vocabulary — it knows about rectangles on a tangent plane, not about framings, plans, or grids.
Alternative rejected: putting it in `Astronomy.Catalog` beside `FramingClusterer`, which would bury reusable
geometry inside a reconciliation-specific namespace.

### A cluster spanning sensors uses its dominant sensor and says so

Clusters can span sensors: IC 405 has one mechanical cluster (~99°) holding Z183×123 + Z533×73. Blending
two sensors into an averaged rectangle would describe neither, so the cluster takes its **dominant**
sensor's footprint and is **marked** as mixed-sensor.

This is deliberately the cheap rule, because measurement says the case barely exists where it could matter:
mechanical clusters always serve the plan, so they never carry a badge and never need an overlap number at
all; and of the five multi-sensor targets, only M81 has a TS target — the other four are Disk-only.
**Cross-sensor overlap is exactly one target library-wide.** Elaborate machinery here would be paid for
once and read never.

### The surface shrank from a column to the badge itself — implementation-measurement decision

*(User decision, 2026-07-29, replacing the planned column + 1560→1640 window widen after an honest-opinion
review. The original column design — placement, width, sort exclusion, the widen arithmetic — is preserved
in this file's git history.)*

Implementation produced three facts that weakened the column's case: the number serves **~14 rows
library-wide** (11 badged strays + 3 displaced serving framings — everything else would show the sentinel);
its real range is **57–100%**, so it largely restates what `Rot` already shows for rotation strays; and it
cannot close the stack decision anyway — registration + integration in PixInsight does (the user's own
framing of the boundary). A permanent 58 px column plus an 80 px window widen was column-sized real estate
for badge-sized information.

The number now rides **the `framing` badge inline** — `framing 57%` — with three rules:

- **Deepest visible line only.** Rollups and target summaries keep the bare token: M81 spans three strays
  at 73/57/80%, so no single number can sit honestly above the lines. This is the same reasoning as the
  deepest-visible-level badge rule, applied to the decoration.
- **Render-layer only.** `Badge` strings always hold the bare vocabulary — search, the flagged filter,
  header aggregation and the rollup union never see a percentage. `ReconciliationRow.BadgeText` decorates at
  display time, and `Badges.Canonical` maps the decorated token back for severity/scope classification.
  Chosen over composing "framing 57%" into the Badge string itself, which would have rippled through every
  consumer of the vocabulary.
- **The report carries what the badge cannot.** An off-plan pointing that *serves* (right angle, displaced
  below the on-footprint threshold) has no badge to decorate — it becomes an informational ambiguity-report
  entry, alongside the mixed-sensor qualifier. Rare (3 today), borderline-actionable: report-shaped.

Alternatives rejected: **tooltip on the Badges or Rot cell** (hover-only discoverability, and per-token
tooltips are impossible anyway — badge tokens are text Runs, not UIElements); **shipping the column as
designed** (paying permanent pixels for 14 values).

### A number means "off-footprint", not "badged" — and the threshold never gates a badged row

*(User decision, 2026-07-29, taken during implementation from a measured comparison of three gates.)*

The first draft gated the fraction on `ServesPlanRotation`, so it appeared exactly where the `framing` badge
appears. That left a real hazard unpriced: a **translation stray** — right angle, wrong pointing — separates
into its own row, carries no badge (the serve rule is rotation-only), and would have shown the sentinel. The
column now prices being off-footprint **for any reason**, and the sentinel means "nothing to say".

That needs a threshold, or every ordinary row would restate a full overlap. Measured over the same 18,650-frame
corpus, reproducing the clusterer and the tangent-plane geometry:

| | framings | overlap |
|---|---|---|
| Serving (angle agrees) | 60 | 52 at ≥ 99.5%; **8 spread 85.6% – 97.8%** |
| Straying (badged) | 11 | **57.5% – 91.9%** |

`FramingCluster.OnFootprintFraction = 0.95` therefore silences the 5 serving framings whose shortfall is
ordinary between-filter scatter and leaves the 3 genuinely displaced ones (0.05°–0.13° off centre) reporting.

The threshold applies to **serving framings only**. A disagreeing framing always reports, whatever its
overlap: a stray just past the 5° tolerance still covers ~95% of the plan's footprint, and silencing it for
being close would leave the badge pointing at a row with nothing to read.

**Gotcha the measurement exposed:** the fraction's practical floor is **~57%, not 0%**. Two same-size
rectangles centred together still share ~67% of their area at a full 90° rotation, so a real value lives in
57–100% and the column reads compressed. It is honest — frames rotated 90° genuinely do share two-thirds of
their sky area — but "38% overlap" is not a number this library can produce for a rotation stray. Only a
pointing far enough to clear the field entirely would approach zero.

### One claim in the spec was wrong: sensor *shape* does enter the number

The delta originally asserted that two framings displaced identically but captured through differently shaped
sensors report the **same** fraction. They do not, and cannot: a 10° turn of a 3:2 rectangle loses a different
share than a 10° turn of a square, and a 0.1° offset costs more of a 1.22° field than of a 1.42° one. The
same-sensor construction buys something narrower and still worth stating — **no other framing's sensor can
move a given framing's number**, because each is measured against a plan rectangle built from its own. That
is what the rejected serving-cluster comparand would have broken (M81's square-sensor stray measured against
a 3:2 serving cluster), and it is what the spec and its test now say.

### Overlap never affects crediting — stated, not merely implied

Write-back's sum stays gated on the boolean `ServesPlanRotation`. It is easy to imagine a later change
making `acquired` proportional to overlap; that would be wrong — a partially-overlapping frame is not a
fractional frame, it is a frame that either belongs in the stack or does not, and PixInsight (not TSM)
decides. The `framing-keys` spec carries this boundary explicitly so the temptation is answered in the
contract rather than rediscovered.

## Risks / Trade-offs

- **A wrong-but-plausible number is worse than none** (the cos(dec) trap, or a binning multiply) → both are
  pinned by tests with the measured real-library values, and the two cases that would silently pass a naive
  implementation (bin-2 Z183, high-declination M81) are named test cases, not incidental coverage.
- **The percentage invites over-reading.** It prices footprint overlap, not stackability — registration,
  star-field commonality and reference-frame choice all matter and none are TSM's business → the spec keeps
  it diagnostic, and the crediting boundary above is explicit.
- **Reading the geometry attribute touches the one reader every consumer shares.** `Astronomy.XISF` is
  consumed beyond TSM → the change is purely additive (a new property; no existing accessor's behavior
  changes), so a consumer that ignores it is unaffected.
- **A target with no serving cluster** still produces numbers for every stray, by construction — this is the
  main reason the chosen comparand beats the serving-cluster alternative, and is worth a test.
- **Sensor dimensions are per-frame but the footprint is per-cluster** → the dominant-sensor rule plus the
  mixed marking; the alternative (splitting clusters by sensor) would change cluster identity and thus row
  separation, which is out of scope and would undo a shipped contract.

## Open Questions

None that affect the specs, the approach, or the task breakdown. The two candidates were resolved by
measurement and by the user's comparand decision, both recorded above.
