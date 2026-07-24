# sentinel-cell — tasks

## 1. Extract

- [x] 1.1 `TsFieldsEditor`: private sealed nested `SentinelCell` (design D1/D2) — captures → fields,
      lambdas → `OnUseDefaultChecked`/`OnUseDefaultUnchecked`/`OnValueConfirmed`, bodies verbatim;
      `BuildSentinelNumber` delegates.

## 2. Verify + docs

- [x] 2.1 Build + full test run (regression floor; the cell itself has no tests — by nature).
- [x] 2.2 CHANGELOG + ROADMAP digest line, same commit.
- [ ] 2.3 Human verification pass (user-run, GATES archive — no auto-archive despite pure refactor: no
      test net): plan flyout → Exposure — check "use default" (default shows, box disables), uncheck
      (box arms with default, nothing written until confirm), type an override (Seconds mirrors), clear
      the box (restores), re-check (sentinel commits, default re-resolves).
