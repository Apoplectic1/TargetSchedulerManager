# review-polish — the 2026-07-24 review's remaining small items, one sweep

## Why

Six review changes shipped the correctness and structure items; what remains is a set of small,
independent quality items cheapest to land as one pass: a doc that over-promises durability (M2), a
UI-thread journal collapse per badge refresh (N2 + M2's second half), two verbatim clamp copies and a
stringly commit-router (M7), per-row search-trim allocation (N1), fire-and-forget discards that swallow
unexpected exceptions (N3), a duplicated value-format rule (N4), magic-twin retry constants (N5), ported
naming conventions + a duplicated comment (N7), idiom leftovers (N10), and a twice-spelled
pending-override rule (blind m1).

## What Changes

- **M2 (doc softening — the Flush(true) fix was rejected):** `TsJournal`'s docs claim "persisted before
  visible / survives crashes". Honest boundary: `AppendAllText` survives **process** crashes; an
  OS/power failure can lose the tail line — and because the SQLite commit and the journal append are two
  separate durability events, no flush here could close that (journal-first would be a design change,
  not taken). Docs state it; the spec's journal requirement gains the boundary.
- **N2:** `TsJournal.CollapsedCount` — maintained under the existing lock (a field-key set), so
  `SyncBadgeText` stops running `Collapse()` (two dicts + a sort) on the UI thread per raise, and badge
  reads stop contending on I/O-length critical sections.
- **M7:** one `ClampToSchema` helper replaces the two verbatim Min/Max/Whole blocks in `TsFieldsEditor`;
  the flyout's inline column-routing lambda becomes a named `TryCommitMirroredField` switch (the
  "these columns have in-grid mirrors" table, findable by name).
- **N1** hoisted search needle · **N3** `FireAndLog` wrapper for the `_ =` discards (unexpected faults
  land in tsm.log, per fail-loud doctrine) · **N4** one `TsValueText.From` for the journal-value display
  rule (TsSync keeps its `"null"` spelling, SyncMarks its null — the difference is each display's
  contract) · **N5** retry count derived from the busy-timeout + cancel-aware sleep · **N7**
  `DiagnosticsWindow` `m`/`s` prefixes → `_camelCase`, duplicated `Row_ItemClick` comment removed ·
  **N10** primary ctors (`TsEditGate`, `VisibleRowTree`; `SyncMarks` skipped — its ctor is deliberately
  private behind `Build`) + the deliberate no-`ConfigureAwait` note on the VM side · **m1**
  `GetMosaicEnabledState` reuses `EffectiveEnabled` (one pending-override rule).

**Deliberately skipped** (recorded here so "remaining" is closed, not forgotten): N6 (owner scan —
fine at scale, revisit on a trace), N8 (blocking Stat — by design, callers wrap), N9 (`AmbiguityReport`
split — pure and reads well; the mildest finding, not worth the churn), m5 (visible-tonight project
flips derive from planned not applied states — rare per-row-failure edge, status already reports the
count; parked), m6 (cross-repo haversine copy — acknowledged in its doc comment; a Library-side item),
m7/N1-debounce (per-keystroke rebuild — first knob if the library grows). M4 (view-model split) follows
as its own change.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `ts-sync-model`: the journal requirement gains the honest durability boundary — entries survive
  process crashes; an OS/power failure may lose the final entry (the local db still holds the write;
  only its replay is lost), and the dirty flag is derived from the journal file, never stored.

## Impact

- **App**: `Shared/TsJournal.cs`, `Shared/TsSync.cs`, `Shared/TsValueText.cs` (new), `Shared/TsEditGate.cs`,
  `Controls/TsFieldsEditor.cs`, `MainWindow.xaml.cs`, `Support/DiagnosticsWindow.cs`,
  `ViewModels/MainViewModel.cs`, `ViewModels/VisibleRowTree.cs`, `Services/SyncMarks.cs`.
- **Tests**: `TsJournalTests` (CollapsedCount). **Docs**: ARCHITECTURE journal note + CHANGELOG/ROADMAP.
- All behavior-preserving except N3 (unexpected UI faults now logged — strictly more fail-loud).
  Pure-refactor/doc sweep ⇒ auto-archives after the suite passes.
