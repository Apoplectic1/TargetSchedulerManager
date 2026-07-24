# push-rule-dedup — tasks

## 1. Dedup

- [x] 1.1 `TsSync`: add `BaselineMatches(TsDbStat probe)`; `ShouldPull` becomes
      `probe.HasSidecar || !BaselineMatches(probe)`; `PreparePush`'s `RemoteChangedSinceBaseline` becomes
      `probe is not null && _state.Baseline is not null && !BaselineMatches(probe)` (the explicit null
      guard preserves "no baseline ⇒ no warning" — see design D2).
- [x] 1.2 `TsSync.PreparePush`: replace the inline `acquired ?? accepted` + separate label fallback with
      `CountEntry(plan)`; desired-only ⇔ the returned entry's column is `desired`; label = the returned
      entry's label. `CountEntry` doc comment gains the one-rule-two-consumers note.

## 2. Verify

- [x] 2.1 `TsSyncTests`: new test — a desired-only write-back group's review line has no count pair and
      carries the desired change; an acquired+desired group keeps its count pair (both through the shared
      rule). Pin the no-baseline review: `RemoteChangedSinceBaseline` false with a probe but no baseline.
- [x] 2.2 Build + full test run (slnx-only per VERIFICATION.md) — the existing suite is the
      behavior-preservation lock.
- [x] 2.3 CHANGELOG entry + ROADMAP digest line, same commit as the code. (No ARCHITECTURE change — the
      contract is unchanged; the spec delta records the invariant.)
