# truthful-outcome — tasks

## 1. Contain the closing pull (TsSync)

- [x] 1.1 `TsSync.Push`: widen the closing pull's catch — `OperationCanceledException` keeps its quiet
      log; a new `catch (Exception)` logs `"PUSH applied but the closing pull failed — next open will
      pull fresh"` (with the exception) and falls through to the `Success` return. `PushResult` gains
      `ClosingPullFailed` (bool, default false), set only in that new catch.
- [x] 1.2 `TsSyncTests`: closing pull throws (stub/fault the pull after a clean replay) ⇒ outcome
      `Success` + `ClosingPullFailed` + journal empty + remote writes applied; closing pull cancelled ⇒
      `Success` without the flag (pin existing behavior); a replay-leg throw ⇒ journal intact (pins the
      D2 premise).

## 2. Truthful reporting (MainViewModel)

- [x] 2.1 `PushAsync`: rewrite the catch comment to state the now-true guarantee (every throw escaping
      `Push` precedes the journal rewrite — the closing pull is contained); `DescribePush` appends
      `" · closing pull failed — next open pulls fresh (see tsm.log)"` when `ClosingPullFailed`.
- [x] 2.2 `MainViewModelTests`: a push result with `ClosingPullFailed` produces the honest status string
      (no "PUSH FAILED", no "edits stay journaled").

## 3. Discard pull-first

- [x] 3.1 `TsSync.Discard`: shrink to journal-clearing only (baseline stays — the discarding pull just
      recorded it); rewrite its doc comment to the new contract (call only after the discarding pull
      landed; pull-first removed the crash window the baseline-drop guarded).
- [x] 3.2 `PrepareTsForLoadAsync` Discard case: `TryPullAsync(probe)` FIRST; landed ⇒
      `await Task.Run(Sync.Discard)` ⇒ `"edits discarded · pulled fresh"`; cancelled ⇒
      `"discard not completed — unpushed edits kept"` with journal/baseline/badge/marks untouched
      (drop the `CancelledPullNote("edits discarded · ")` path).
- [x] 3.3 Tests: Discard + cancelled pull ⇒ journal still dirty, badge shows unpushed, honest status;
      Discard + landed pull ⇒ journal empty. Drive at the `TsSync` level if the VM pull path resists
      stubbing, with the VM strings asserted separately.

## 4. Verify + docs

- [x] 4.1 Build + full test run (slnx-only per VERIFICATION.md).
- [x] 4.2 `ARCHITECTURE.md` sync-model section: push outcome contract (journal cleared ⇔ writes applied;
      closing pull contained) + Discard pull-first ordering. ROADMAP digest + CHANGELOG entry. Same
      commit as the code.
- [x] 4.3 Human verification pass (user-run): push with BIRDWATCHER unplugged mid-closing-pull is hard to
      stage — the meaningful checks are the Discard flows: cancel a discard-pull (badge/marks stay, honest
      status), complete a discard-pull (clean grid, journal empty).
