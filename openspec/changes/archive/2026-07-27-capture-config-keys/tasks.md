## 1. Library — read the capture configuration (`..\Library`)

- [x] 1.1 Switch the scanner's offset source from `OffsetNormalized` to the raw recorded value (`ImageLibraryScanner.cs:353`), so disk offset and `exposuretemplate.offset` share one scale
- [x] 1.2 Delete `XisfHeader.OffsetNormalized` and its per-camera divisor table; update the `TypicalSettings.Offset` doc comment that references it
- [x] 1.3 Delete the `OffsetNormalized` tests in `Astronomy.XISF.Tests` (`XisfHeaderTests`, `XisfHeaderReaderTests`) — including the fixture assertion that currently blesses `10 → 2`
- [x] 1.4 Add the frame's camera-directory name and its recorded camera identifier to the scanner's per-frame reading, so a disagreement between them is detectable
- [x] 1.5 Verify the library builds and its existing tests pass before any key changes

## 2. Library — key the disk plane by capture configuration

- [x] 2.1 Extend the scanner's aggregate key to `(filter, purpose, exposure bucket, gain, offset, binning, camera)`
- [x] 2.2 Replace `FilterAggregate.CamerasSeen` with a single camera plus the raw directory name, and add gain/offset/binning to the aggregate's identity; update its doc comment (the plural existed only because camera was not a key)
- [x] 2.3 Carry the camera-directory-vs-recorded-identifier disagreement as a flag on the aggregate
- [x] 2.4 Add the new fields to `InventoryFilter`, `schema.sql` (including the primary key), `Mappers.cs` and `CatalogStore`
- [x] 2.5 Update `TargetResolver.ToInventoryFilter` for the new fields, removing the comma-joined camera list
- [x] 2.6 Update `Astronomy.NINA`'s `ReportToTargetAdapter` test that constructs a `FilterAggregate`
- [x] 2.7 Add scanner tests: frames differing only in gain, only in offset, and only in binning each produce separate aggregates; identical configuration produces one

## 3. Library — carry the configuration to the consumer

- [x] 3.1 Add gain, offset, binning and camera to `ReconciliationCell`
- [x] 3.2 Key the disk side of `ReconciliationProjection`'s accumulator on the capture configuration; populate the TS side from the plan's exposure template
- [x] 3.3 Add projection tests covering a plan meeting a matching disk bucket, and a plan meeting a bucket that differs by gain or by offset
- [x] 3.4 Confirm by test that `WriteBackPlanner` still totals the same `acquired` when a target's frames occupy several finer disk buckets

## 4. App — grid geometry

- [x] 4.1 Add Camera, Gain, Offset and Bin to the `GridColumns` ruler between Project and Filter
- [x] 4.2 Shift every `Grid.Column` index in `MainWindow.xaml` — the header grid and all three row templates — and add the four cells to each
- [x] 4.3 Add column captions to the header grid
- [x] 4.4 Run the app and confirm the header and all row kinds align, and that no cell has collapsed into column 0

## 5. App — pairing and row shaping

- [x] 5.1 Implement the pairing test in `ReconciliationLoader.BuildRows`: render `Both` only when the disk bucket matches the plan on every shared key, otherwise emit a TS row plus Disk rows
- [x] 5.2 Carry the capture configuration onto `ReconciliationRow`, with camera absent on TS-plane rows
- [x] 5.3 Add the sort exception — configuration ordered after exposure, never before filter — and update the sort-precedence comment to record that this deliberately departs from column order
- [x] 5.4 Confirm a one-plan row remains inline-editable after separating from its disk frames (the plan key and desired-count editing must survive)
- [x] 5.5 Add loader tests for: matching configuration pairs; a gain disagreement separates; an offset disagreement separates the mismatched subset only; a camera difference never prevents pairing

## 6. App — rollups, badges, search

- [x] 6.1 Compute per-rollup uniformity for each configuration column in `RowAggregates`, exposing either the shared value or a mixed marker
- [x] 6.2 Render the uniform value or a caution-emphasis `mixed` marker on target headers, panel headers and filter rollups
- [x] 6.3 Add the camera alias resolution to `Models/Format.cs` (183, 533, 178, 144 → their aliases; otherwise unresolved)
- [x] 6.4 Add `camera` and `cam≠` tokens to `Models/Badges.cs` at warning severity, and widen the severity doc so "repair outside TSM" explicitly covers disk-side repairs
- [x] 6.5 Attach both badges per row rather than per target, and confirm they roll up to ancestors without spreading to sibling rows
- [x] 6.6 Add camera to the search predicate in `ReconciliationRow.Matches`; leave the numeric columns out
- [x] 6.7 Add tests for alias resolution (including an unresolved directory) and for per-row badge scoping

## 7. TS authoring check

- [x] 7.1 Detect duplicate exposure template names and report them as repairable authoring
- [x] 7.2 Surface the finding where other TS authoring problems already surface
- [x] 7.3 Add a test for duplicate names, and confirm the current 20 templates produce no finding

## 8. Verify and document

- [x] 8.1 Build both repos; run the library and app test suites
- [x] 8.2 Run the app against the live library and confirm against known cases: Abell 21's H/O/S rows each separate an offset-50 Disk row; its Stars B 60 s row separates on gain; the target rollup reads camera and binning uniform with gain and offset mixed
- [x] 8.3 Confirm the target-level Hours figure is unchanged by the re-keying (Abell 21 stays at −18.3)
- [x] 8.4 Record the standing truths in `DOMAIN.md`: the two purposes, disk-is-history, TS-is-the-future, and the camera-agnostic `desired` rule
- [x] 8.5 Update `ARCHITECTURE.md` / `CONVENTIONS.md` for the new key set, the pairing rule, and the sort exception; update the add-a-UI-element checklist if the column-insert steps changed
- [x] 8.6 Note in `ROADMAP.md` that rotation, RA/DEC and the telescope section are deferred follow-ups
