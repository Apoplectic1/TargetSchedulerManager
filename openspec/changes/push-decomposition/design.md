# push-decomposition — design

## Context

`TsSync.Push` (post `truthful-outcome`) runs: collapse/empty-check → probe refusals → split into
write-back/field entry lists → write-back leg (structural refusals return whole-push refusal; per-plan
verify failures recorded) → field leg (per-field refusal/failure; whole-db-refusal abort cascade) →
`Journal.CommitPush` retention → partial-failure return / mid-push-edits return / contained closing pull
→ `Success`. Two local functions (`Fail`, `RefusedStructurally`) close over `failedSeqs`/`failures`.

## Goals / Non-Goals

**Goals:** each leg individually nameable (in a diff, a test failure, an LLM context window); replay
state explicit; zero behavior change.

**Non-Goals:** any semantic change to refusals, retention, ordering, or outcome shapes; the M4
`MainViewModel` split (separate concern); further `PushResult` changes.

## Decisions

### D1 — Bodies move verbatim; the orchestrator reads as the sequence

```csharp
public PushResult Push(IProgress<int>? pullProgress = null, CancellationToken pullCancel = default)
{
    List<TsJournalEntry> collapsed = Journal.Collapse();
    if (collapsed.Count == 0)
        return new PushResult(PushOutcome.NothingToPush, 0, [], PulledFresh: false);

    if (ProbePushPreconditions() is { } refused)
        return refused;

    PushReplayState state = new();
    if (ReplayWriteBackLeg(collapsed, state) is { } structuralRefusal)
        return structuralRefusal;
    ReplayFieldLeg(collapsed, state);

    return CommitAndClose(collapsed, state, pullProgress, pullCancel);
}
```

`PushReplayState` is a private sealed class holding `FailedSeqs`/`Failures` with the dedup-and-log
`Fail` method (today's local function, verbatim — including the `Log.Error` per entry).
`RefusedStructurally` becomes a private static helper the write-back leg calls. Each leg filters its own
entries from `collapsed` (`Kind == WriteBack` / `Manual`) — one list parameter, no pre-split threading.

### D2 — `PushResult?`-returning legs encode "whole-db refusal" vs "completed with failures"

The write-back leg's three structural refusals (columns missing, read-only, open sidecar) already mean
"nothing was written — refuse the whole push"; returning them as a non-null `PushResult` keeps that
one-way exit visible in the orchestrator. Per-plan verify failures stay in `state` and the leg returns
null. The field leg never whole-push-refuses (its whole-db refusal is the in-state abort cascade), so it
returns void.

### D3 — Codify (not change) leg order + abort cascade in the spec

Both behaviors exist and matter — write-back-then-fields is why an explicit later desired edit outranks
the writer's ratchet; the cascade is why one dead remote db doesn't hammer N field writes — but neither
is in `ts-sync-model`. The delta ADDs them as a requirement, and the cascade (untested today) gets a
test: `RecordingEditor.RefuseAll = SchemaIncompatible` over two manual edits ⇒ `PartialFailure`, both
entries failed (second as not-attempted), both retained, exactly one editor write attempt.

## Risks / Trade-offs

- [Verbatim-move drift (a guard subtly dropped in transit)] → the truthful-outcome + push suites cover
  every return path: NothingToPush, Unreachable, RefusedBusy (probe + applier), Refused (structural),
  PartialFailure (row-gone, write-back-row-gone), Success (+pulled fresh / closing-pull-cancelled /
  closing-pull-failed / mid-push-edits via CommitPush's seq rule), plus the new cascade test.

## Migration Plan

None. Pure refactor; clean rebuild.

## Open Questions

None.
