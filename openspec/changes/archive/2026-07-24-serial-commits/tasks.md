# serial-commits — tasks

## 1. The chain

- [x] 1.1 `Shared/CommitChain.cs` per design D1 (UI-thread task chain; guarded `await prev`; caller
      awaits its own task).
- [x] 1.2 `CommitChainTests`: no-overlap (second starts only after first completes, TCS-gated); results
      resolve in submission order under a slow first commit; a throwing commit faults only itself and
      the chain continues.

## 2. Wire the surfaces

- [x] 2.1 `TsFieldsEditor`: one `CommitChain` field; route all six commit sites
      (toggle / number / sentinel-checkbox / sentinel-box / combo / text) through
      `_chain.Run(() => _commit(...))`.
- [x] 2.2 `MainWindow.Desired_Committed`: window-level chain around `SetPlanDesiredAsync`.

## 3. Verify + docs

- [x] 3.1 Build + full test run (slnx-only).
- [x] 3.2 CHANGELOG entry + ROADMAP digest line, same commit.
- [x] 3.3 Human sanity pass (user-run — bugfix, no auto-archive): rapid flyout edits (Enter, retype,
      Enter) and rapid inline Desired edits feel unchanged; values land as typed; then archive.
