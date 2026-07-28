## Why

The grid reconciles disk against TS on `(target, filter, purpose, seconds)`, so frames captured at **different gain, offset or binning fold into one row and read as one stack** — when they will not stack at all. The library's own history proves the cost: broadband moved from gain 53 to gain 0 in **2024**, and offset-50 frames are scattered through every filter (10 of 34 H frames on Abell 21) while every TS template specifies offset 10. Today a target shows one tidy `Both` row asserting that all of it counts toward the plan.

TSM serves two purposes — **modify TS on BIRDWATCHER** (primary) and **display the entire imaging history** (secondary). The second is currently lossy: the capture configuration is invisible, so a discrepancy between what was shot and what is planned cannot be seen, only inferred from numbers that do not add up.

## What Changes

- **Gain, offset and binning become reconciliation keys.** Disk buckets go from 471 to 542 (+15%); the TS side is unaffected (all 650 plans are already unique on `(target, filter, exposure)`).
- **Camera becomes a displayed label**, sourced from the capture directory name (`Captures/<Camera>/<Filter>/`). It splits zero buckets today and is a tripwire for the day that changes.
- **The pairing rule becomes explicit**: a row is `Both` **if and only if** the disk bucket matches the TS plan on every shared key. Otherwise it renders as a TS row plus one or more Disk rows — and that split is the diagnostic, showing *how* history differs from the plan. **BREAKING** for the reader: roughly 245 of 542 buckets stop pairing, so many targets gain Disk rows where one merged row stood.
- **Four new grid columns** — Camera, Gain, Offset, Bin — between Project and Filter (+200 px fixed width).
- **Sort deliberately skips the four new columns**, staying on Target → Project → Panel → Filter → Purpose → Seconds → config → plane, so one filter's rows remain contiguous. This is an explicit exception to the grid's "sort follows column order" rule.
- **Rollup rows show the value when children agree, a `mixed` caution pill when they do not** — extending the idiom the Seconds column already uses.
- **Two new badges**: `camera` (warning) when a capture directory name resolves to no known camera, and `cam≠` when the directory name disagrees with the file's own `INSTRUME` value. Both mark the affected row and every summary row above it, never unaffected siblings.
- **Offset is read raw.** XFM does not divide — its per-camera divisor text is descriptive only — so disk offset and `exposuretemplate.offset` are already the same scale. The scanner stops calling `OffsetNormalized`, which currently reports 2 for a Z183 frame whose offset is 10.
- **`XisfHeader.OffsetNormalized` is removed** once the scanner (its only production caller) stops using it. **BREAKING** for the shared library's public surface.
- **New TS authoring check**: exposure template **names** must be unique. Flags zero templates today.

## Capabilities

### New Capabilities
- `capture-config-keys`: the capture configuration as first-class reconciliation data — which dimensions key the disk plane, which key the TS plane, and the rule deciding when the two pair into a `Both` row versus separating into TS and Disk rows.

### Modified Capabilities
- `reconciliation-grid`: column geometry gains four columns; sort precedence gains a documented exception; rollup cells gain the uniform-or-`mixed` rendering; the badge vocabulary gains `camera` and `cam≠` with row-plus-ancestors scope.

## Impact

**This repo (TSM app)**
- `GridColumns.cs` — four ruler entries; every `Grid.Column` index in `MainWindow.xaml` shifts (header plus three row templates).
- `ViewModels/Rows/ReconciliationRow.cs`, `RowAggregates.cs`, `AggregateHeaderRow.cs`, `PanelGroupRow.cs`, `TargetGroupRow.cs` — carry and roll up the new values.
- `Services/ReconciliationLoader.cs` — the pairing decision and the sort exception.
- `Models/Format.cs` — camera alias resolution; `Models/Badges.cs` — two new tokens.
- `ViewModels/MainViewModel.cs` — camera joins the search predicate.

**Sibling repo `..\Library` (`Astronomy.Catalog`, `Astronomy.XISF`)**
- `Scan/ImageLibraryScanner.cs` — aggregate key gains gain/offset/bin/camera; offset switches to the raw value.
- `Scan/FilterAggregate.cs`, `Schema/Tables.cs`, `Schema/schema.sql`, `Schema/Mappers.cs`, `CatalogStore.cs` — carry the new fields.
- `Reconcile/ReconciliationProjection.cs` — `ReconciliationCell` carries the capture config.
- `Astronomy.XISF/XisfHeader.cs` — `OffsetNormalized` deleted, with its tests.
- `TargetScheduler/WriteBackPlanner.cs` — **verified unchanged**: its key is coarser and it sums inventory rows, so finer disk buckets still total the same `acquired`.

**Not affected**
- Hours and Remaining totals — `RowAggregates` sums components rather than per-row gaps, so target-level figures are identical whether a bucket renders as one `Both` row or as TS + Disk rows.
- `Catalog.db` on disk — derived and rebuildable; no app reads it.

**Deferred, deliberately out of scope**
- Rotation as a key (needs a circular tolerance, disk-side clustering with no precedent in the codebase, and a meridian-flip rule guarded by an RA/DEC centroid).
- RA/DEC refinements.
- Telescope as its own UI section — deferred with the disk directory-layout change a second telescope would bring.
- Comets — never scheduled in TS, captured manually.
