# noop-edit-pruning — Tasks

## 1. TsJournal pruning

- [x] 1.1 `_baselineOld` map beside `_fieldKeys` (same lock); shared `RebuildIndexesLocked()` used by
      `Load` and `ReplaceAllLocked`
- [x] 1.2 `Append` → `TsJournalEntry?`: value-text (`TsValueText.From(Canonicalize(value))`) equals the
      field's baseline (existing map entry, else the incoming `old`) ⇒ prune the field's entries
      (crash-safe rewrite; first-touch ⇒ no file touch) and return null; otherwise append as today,
      recording the baseline on first touch

## 2. Tests

- [x] 2.1 `TsJournalTests`: round-trip prunes (entries + file + `CollapsedCount`); first-touch same-value
      returns null and journals nothing; a real change still appends; baseline is the FIRST old across
      multiple writes; push retention resets the baseline; `Load` rebuilds baselines from the sidecar
- [x] 2.2 Surface assertions: SyncMarks row/`ForField` blank after revert; pre-existing inbound `←`
      survives the round-trip; badge count excludes the reverted field

## 3. Docs + verification

- [x] 3.1 `ARCHITECTURE.md` (journal bullet gains the pruning invariant) + `CHANGELOG.md`
- [x] 3.2 Build + tests green; user verifies visually (toggle round-trip clears marks live in flyout +
      grid) — behavior-changing, archive waits for the verify word
