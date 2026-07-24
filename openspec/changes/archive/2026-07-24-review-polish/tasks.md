# review-polish — tasks

## 1. Journal (M2 doc + N2)

- [x] 1.1 `TsJournal`: soften class + `Append` docs to the honest boundary (design D1); add
      `CollapsedCount` via a field-key `HashSet` under `_lock` (D2); `SyncBadgeText` reads it.
- [x] 1.2 `TsJournalTests`: `CollapsedCount` tracks appends (same field collapses), `CommitPush`,
      `ReplaceAll`/`Clear`, and reload.

## 2. Editor + window (M7, N1, N3, N7)

- [x] 2.1 `TsFieldsEditor`: `ClampToSchema(field, wanted)` once, both number sites use it.
- [x] 2.2 `MainWindow`: `TryCommitMirroredField` named router (D3); `FireAndLog` wraps the `_ =`
      discards (D4); duplicated `Row_ItemClick` comment removed.
- [x] 2.3 `MainViewModel`: hoist the search needle (N1); `GetMosaicEnabledState` reuses
      `EffectiveEnabled` (m1); one-line deliberate no-`ConfigureAwait` note (N10).
- [x] 2.4 `DiagnosticsWindow`: `mX`/`sX` → `_camelCase` (N7).

## 3. Shared rules (N4, N5, N10)

- [x] 3.1 `Shared/TsValueText.cs` (D5); `TsSync.FormatValue` + `SyncMarks.FormatValue` route through it.
- [x] 3.2 `TsSync.BackupTo`: named constants, retries derived from the busy-timeout, cancel-aware sleep (D6).
- [x] 3.3 Primary ctors: `TsEditGate`, `VisibleRowTree` (`SyncMarks` skipped — private ctor by design).

## 4. Verify + docs + archive

- [x] 4.1 Build + full test run (slnx-only).
- [x] 4.2 ARCHITECTURE sync-model journal note; CHANGELOG + ROADMAP digest; same commit.
- [x] 4.3 Auto-archive (doc/refactor sweep; N3 is strictly-more-logging) — sync the ts-sync-model delta.
