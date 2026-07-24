# serial-commits — overlapping edit commits serialize instead of racing

## Why

The 2026-07-24 review cross-check's last open correctness item: every commit handler in
`TsFieldsEditor` (all six control kinds) and the grid's `Desired_Committed` share the shape
`if (await commit(...)) lastKnown = value; else revert;` — and the control stays live during the await.
A second confirmed value therefore starts a second concurrent gate write: the two ride separate SQLite
connections (db order unguaranteed), both journal, each updates its last-known on its own completion —
so completion order can leave the control's state, the db, and the journal's last write disagreeing.
Worse, the first write's read-back verify can observe the second write's value, report **Failed**, and
spuriously revert a good edit. Narrow window (needs two rapid confirms), but it corrupts the one thing
the guarded gate exists to guarantee: what you see is what verified.

## What Changes

- **A small `CommitChain` helper (UI-thread task chain, no locks):** each commit starts only after every
  earlier commit from the same surface has completed. Pure serialization — nothing disables, nothing is
  refused, confirmation order is preserved; the only observable change is that the race outcomes
  (out-of-order last-known, spurious verify-fail revert, journal last-write ≠ final value) become
  impossible.
- **`TsFieldsEditor`**: one chain per editor instance; all six handler sites route `_commit` through it.
- **`MainWindow.Desired_Committed`**: the grid's inline Desired commits route through a window-level
  chain — the same defect, the same one-line fix.
- Not chosen: disabling the form during a commit (focus side effects — a moved focus fires `TextBox`
  `LostFocus` commits re-entrantly) or refusing mid-flight confirms (bounces a valid value back at the
  user). Serialization keeps today's UX exactly.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `schema-driven-field-editor`: gains the serialization requirement — commits issued from one editing
  surface SHALL apply strictly in confirmation order, one at a time; a later confirm SHALL NOT cause an
  earlier verified write to report failure or revert; the last confirmed value SHALL be what the db, the
  journal's collapsed last-write, and the control agree on.

## Impact

- **App**: new `Shared/CommitChain.cs`; `Controls/TsFieldsEditor.cs` (six call sites);
  `MainWindow.xaml.cs` (`Desired_Committed`). **Tests**: `CommitChainTests` (ordering + no-overlap +
  fault isolation — the editor itself is a WinUI `UserControl`, not unit-testable headless).
  **Docs**: CHANGELOG/ROADMAP digest, same commit.
- **Verification split**: the ordering guarantees are test-pinned; the *feel* of rapid flyout/inline
  editing (unchanged by design, but the editor wiring can't be exercised headless) gets a quick user
  sanity pass before archive — this is a bugfix, not a pure refactor, so it does not auto-archive.
