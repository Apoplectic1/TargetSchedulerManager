# push-decomposition — tasks

## 1. Decompose (bodies verbatim)

- [x] 1.1 `PushReplayState` private sealed class: `FailedSeqs`, `Failures`, `Fail(entries, detail)`
      (today's local function verbatim, incl. per-entry dedup + `Log.Error`). `RefusedStructurally`
      becomes a private static helper.
- [x] 1.2 `ProbePushPreconditions()` → `PushResult?`: the probe + unreachable + remote-sidecar refusals.
- [x] 1.3 `ReplayWriteBackLeg(collapsed, state)` → `PushResult?`: filters `Kind == WriteBack`; applier
      construction + structural refusals (non-null return); planned writes + verify-failure recording
      (null return).
- [x] 1.4 `ReplayFieldLeg(collapsed, state)` → void: filters `Kind == Manual`; per-field guarded replay
      in seq order; whole-db-refusal abort cascade via `state`.
- [x] 1.5 `CommitAndClose(collapsed, state, pullProgress, pullCancel)` → `PushResult`: seq-aware
      retention, partial-failure return, mid-push-edits return, contained closing pull, Success. `Push`
      shrinks to the D1 orchestrator.

## 2. Verify + docs

- [x] 2.1 New `TsSyncTests` abort-cascade test: `RefuseAll = SchemaIncompatible` over two manual edits ⇒
      `PartialFailure`, 2 failures (second "not attempted"), both retained, no second write attempt.
- [x] 2.2 Build + full test run (slnx-only per VERIFICATION.md) — existing push suite green = the
      verbatim-move proof.
- [x] 2.3 CHANGELOG entry + ROADMAP digest line, same commit.
