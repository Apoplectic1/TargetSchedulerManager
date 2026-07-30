## 1. Library — read the sensor geometry (`..\Library`, Astronomy.XISF)

- [x] 1.1 `XisfHeaderReader`: read the `<Image>` element's `geometry` attribute (`width:height:channels`)
      alongside the existing `<FITSKeyword>` harvest, and throw `InvalidDataException` naming the file and the
      missing declaration when it is absent or unparseable — it is mandatory in the XISF spec, so there is no
      fallback and no `NAXIS` path. The scanner's existing catch records it in `SkippedFiles` (see group 4a),
      which is the corrupt-file category this belongs in
- [x] 1.2 `XisfHeader`: expose the pixel width and height; add a derived angular-footprint accessor from
      dimensions + `FOCALLEN` + `XPIXSZ`/`YPIXSZ` (`206.265 × pixelSize / focalLength`), with **no binning
      factor anywhere in the derivation** — doc-comment why, since the naive `× XBINNING` is the trap
- [x] 1.3 Tests: geometry parsed from a real-shaped XISF header; a missing `<Image geometry>` aborts;
      **a bin-2 frame (half dimensions, double pixel size) yields the same footprint as its bin-1
      counterpart** — the regression that guards 15.8% of the library
- [x] 1.4 Tests: a longer focal length at identical dimensions/pixel size yields a smaller footprint

## 2. Library — rectangle overlap geometry (Astronomy.Core)

- [x] 2.1 Add a rotated-rectangle overlap helper: two centers, one shared size, two rotations → the
      intersection area as a fraction of the measured rectangle's area. Convex-polygon clipping
      (Sutherland–Hodgman) then shoelace area — exact, deterministic, no sampling
- [x] 2.2 Project to a tangent plane about the reference center with **RA offsets scaled by cos(dec)**;
      accept RA in the Library's hours convention and convert at this boundary
- [x] 2.3 Keep the surface caller/consumer-framed — rectangles on a tangent plane, no framing/plan/grid
      vocabulary — per the shared-library discipline
- [x] 2.4 Tests: identical rectangles → 1.0; disjoint → 0.0; a square rotated 90° about its own center →
      1.0; a hand-computed partial overlap → its closed-form value
- [x] 2.5 Test: **a high-declination pair (Dec ≈ +69°, M81's) whose overlap is wrong if RA is not scaled by
      cos(dec)** — the other silent-wrong-number guard

## 3. Library — carry the footprint and compute the fraction (Astronomy.Catalog)

- [x] 3.1 `FramingCluster`: carry the cluster's angular footprint and a flag for spanning more than one
      sensor geometry
- [x] 3.2 `FramingClusterer`: populate both — **dominant** sensor's footprint when frames span sensors,
      never a blend of two
- [x] 3.3 `ReconciliationProjection`: where `FramingDisagrees` is already computed from `TargetRotationDeg`,
      compute the overlap fraction — plan rectangle from **the measured cluster's own** footprint, centered
      on the plan target's coordinates, rotated to the plan's rotation — and carry it on the cell beside
      `DiskRotation`/`DiskRotationFoldDeg`/`FramingDisagrees`
- [x] 3.4 Leave the fraction absent for a mechanical/unknown cluster, a plan with no rotation, and a cluster
      with no derivable footprint — absent, not zero. **Revised (user decision, see design):** a *serving*
      cluster is absent only while it stays on the plan's footprint — `FramingCluster.OnFootprintFraction`
      (0.95); a disagreeing cluster always reports, whatever its overlap
- [x] 3.5 Confirm `WriteBackPlanner` and `SingleTargetPlanner` are untouched: crediting stays the boolean
      `ServesPlanRotation`, unscaled by overlap — verified, both still call only `ServesPlanRotation`
- [x] 3.6 Tests: a rotated stray reports; a translated stray at the plan's own angle reports; a serving
      cluster on-footprint reports nothing; a mechanical/unknown cluster and a rotation-less plan report
      nothing; a cluster with no footprint reports nothing (not zero); a just-over-tolerance stray reports
      despite a near-full overlap; **a target whose every cluster strays still reports for all of them**
- [x] 3.7 Test: a cluster's fraction is unchanged by the presence of same-target clusters captured on a
      differently shaped sensor — each measured against its own. (The delta's original claim, that identical
      displacements on different sensors give the *same* fraction, is false and was corrected — see design.)
- [x] 3.8 Tests: a mixed-sensor cluster measures by its dominant sensor and is marked; a bin-2 frame's
      footprint matches its bin-1 counterpart at the clusterer level too
- [x] 3.9 Fix fallout from 1.1: the scanner tests' synthetic XISF fixtures carried no `<Image geometry>`, so
      every frame skipped as corrupt and 12 tests failed. The fixture writer now emits one

## 4a. App — stop losing unreadable frames silently

Pre-existing hole found while implementing 1.1: `ImageLibraryScanner` records every failed frame read in
`ImageLibraryReport.SkippedFiles`, and **nothing in TSM reads it** — so an unreadable frame today lowers the
Actual count with no indication. Missing geometry lands in that same category, so the category has to stop
being silent. (User-approved scope addition, 2026-07-29.)

- [x] 4a.1 Surface the unreadable-frame count on the load status line — `⚠ N unreadable file(s) — see
      Ambiguities…` (the ⚠ glyph is the caution emphasis a single-brush status TextBlock can carry) — and
      show nothing at all when the count is 0. `LoadResult` carries the scan's `SkippedFiles` for it
- [x] 4a.2 Make the offending paths reachable: a **"Fix on disk — unreadable files"** action section in the
      ambiguity report, one item per path with its reason — action, not info, because each one silently
      lowers the Actual counts until repaired
- [x] 4a.3 Tests: a report with skipped files carries the section + counts them as actions; a clean report
      shows the affirmative marker; clean-report section count updated (5 `✓ none` sections now)

## 4. App — the number rides the badge (revised surface — user decision 2026-07-29, see design)

The planned grid column (original 4.1–4.9: GridColumns index 9, Format/RowAggregates work, the 1560→1640
window widen) was **dropped at implementation**: measurement showed ~14 populated rows library-wide in a
compressed 57–100% range — badge-sized information. Preserved in git history; replaced by:

- [x] 4.1 `Badges`: `FramingWithOverlap(fraction)` → `framing 57%`, plus `Canonical` so `IsWarning` /
      `IsRowScoped` classify a decorated token exactly as bare `framing` (BadgeRuns colouring, badge filter)
- [x] 4.2 `RowConfig`: carry `FramingOverlapFraction`; the loader threads it from the cell (disk-backed rows
      only, like the rest of the provenance)
- [x] 4.3 `ReconciliationRow.BadgeText`: decorate the framing token with the row's own fraction on a LEAF
      (its own deepest visible level); rollups collapsed show the bare token, expanded drop it (unchanged);
      `Badge` itself never carries the number — search / flagging / header aggregation stay bare
- [x] 4.4 `AmbiguityReport`: informational entries for the overlap facts with no badge — an off-plan
      pointing that serves (below the on-footprint threshold), and the dominant-sensor qualifier for a
      priced mixed-sensor framing. Cells re-projected inside `Build` via `ReconciliationProjection` (pure,
      ~ms — the same pattern as the write-back re-plan)
- [x] 4.5 Tests (`BuildRowsTests`): the badged leaf reads `framing 92%` while `Badge` stays bare and
      decorated tokens classify as warnings; the rollup never shows a number at any expansion state; the
      header union stays bare; a badged row with no footprint shows bare `framing`, never `framing 0%`
- [x] 4.6 Tests (`AmbiguityReportTests`): off-plan pointing renders as info (0 actions) with the fraction;
      an on-footprint framing reports nothing

## 4b. App — verify-pass formatting feedback (user obs 53c5, 2026-07-29)

- [x] 4b.1 Mechanical marker `°m` → `°(M)` (`Format.Rotation`) — the bare `m` read as a stray character
- [x] 4b.2 `Rot` column 58 → 76 px so `110.5°(M)` fits without crowding Filter
- [x] 4b.3 **Every data column centered — header, values, and edit boxes** (config + count columns across
      all three row templates + the header grid; Seconds margins dropped; Desired NumberBox centered).
      Source/Target/Project/Badges stay left. Replaces the right-aligned-numerics convention — recorded in
      DOMAIN.md with the units-digit tradeoff
- [x] 4b.4 (obs deea) The Filter letter centers ALONE — the hover edit glyph overlays the column's right
      edge; sharing a centered StackPanel with the invisible-but-laid-out glyph shifted the letter by a
      per-row-kind amount
- [x] 4b.5 (obs 27ec) **Uniform column spacing by construction**: every data column Camera→Plans is
      content-max + one shared `Gutter` constant (24 px) in the ruler — centered cells make each column's
      slack the visible gap, so one constant makes the gaps equal. Change spacing at the constant, never
      per column
- [x] 4b.6 (obs 27ec) Window 1560 → **1710** so `Target` (the one elastic column) never truncates a real
      name — longest today "Mosaic - Cygnus Loop · 16 panels" ≈ 250 px; Target gets ~300

## 4c. App — the Hours gauge (user obs 01b7, decisions in-chat 2026-07-29)

Replaces the signed-sum Hours (every row a signed contribution; parents the literal sum) with a **progress
gauge**: time still owed beneath (negative, caution) or the captured disk total once nothing is owed
(green, unsigned — a positive value is a TOTAL, never a surplus; the `+` prefix died). Decisions: remaining
is **acquired-based** (framing-aware — the recommended default, Q1 returned unanswered), complete shows the
**total** not the surplus, and **debt survives a disable** (Visible-Tonight flips `target.active` nightly).
New `reconciliation-grid` delta carries the contract.

- [x] 4c.1 `RowNumbers.RemainingHours` (Σ per-plan-cell max(0, desired − acquired) × sec, clamped per cell);
      loader computes it at every construction site (`CellRemainingHours`); inline edits recompute it
- [x] 4c.2 `ReconciliationRow`: gauge `Hours`/`HoursText`/`HoursBackground` — Disk line plain total, TS line
      owed-or-dash (desired-0 keeps its critical tripwire), Both line debt-or-total
- [x] 4c.3 `RowAggregates`/`AggregateHeaderRow`: `HoursDelta` → the two gauge components (`RemainingHours` +
      `DiskHours`); same display rule at every header level
- [x] 4c.4 The `Remaining` sort key moves to the same acquired basis, so "Sort: remaining ↓" and the gauge
      can never call one target differently
- [x] 4c.5 Tests: TS remaining-not-commitment (+complete → dash); Both incomplete shows remaining not the
      disk gap (the M81-R shape); Both complete shows total not surplus; desired-0 tripwire; ApplyDesired
      recomputes the debt; aggregate components sum; acquired-based sort key
- [x] 4c.6 Docs: DOMAIN visual-language bullet rewritten (gauge + fills), XAML cell comments,
      `reconciliation-grid` spec delta

## 5. Verify

- [x] 5.1 Build + full test pass, library then app — Catalog 236 · XISF 51 · Core 472 · NINA 45 ·
      Contracts 61 · App 320
- [ ] 5.2 Run the app: **Barnard 202** — the 60°/451-frame line reads `framing 92%` expanded; the rollup
      shows bare `framing` collapsed; the 50° line has no badge
- [ ] 5.3 **M81** (plan 65.11°): the 115°, 125° and 0° lines each carry their own percentage (73/57/80-ish);
      the 65° Z183 line is bare; the summary row shows bare `framing`
- [ ] 5.4 Status line stays free of any unreadable-frame text (measured library: 0 skipped); the ambiguity
      report's Info section shows the three off-plan pointings (Markarian's Chain ~86%, FishHead ~88%,
      M51 ~94%)
- [ ] 5.5 Spot-check that no count, total, row separation, sort order, or window geometry changed anywhere
      against the pre-change grid

## 6. Docs (same commit as the code)

- [x] 6.1 `ARCHITECTURE.md`: extend the framing key fact with the overlap price, its off-footprint-for-any-
      reason meaning, the serving-only threshold, and the crediting boundary; the cos(dec) + no-binning
      derivation rules beside the measurement values
- [x] 6.2 `CLAUDE.md`: mirror the invariant bullet's framing sentence (condensed, as always — edit both)
- [x] 6.3 `DOMAIN.md`: the hazard is now priced but still only detected — TSM detects, WBPP enforces, XFM
      neither; the 57–100% compressed-range reading note
- [x] 6.4 `ROADMAP.md`: close the overlap-% deferred item; `CHANGELOG.md`: the shipped entry with measured
      values and the column→badge surface decision
- [x] 6.5 `NOTEBOOK.md`: the two silent-wrong-number traps (binning-adjusted `XPIXSZ`, cos(dec)) + the
      57–100% floor
