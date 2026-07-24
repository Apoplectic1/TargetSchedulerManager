# serial-commits — design

## Context

`TsFieldsEditor`'s six handler kinds and `MainWindow.Desired_Committed` all run
`await commit(...)` with their surface still interactive. Overlapping same-field commits race three
ways: db write order (separate connections), read-back verify (first write can observe the second's
value → `Failed` → spurious revert), and last-known bookkeeping (updated per-completion, so the control
can end up disagreeing with both db and journal). The busy exclusion (2026-07-24) deliberately does not
serialize edits against each other — different-field concurrency is safe; *same-surface rapid
re-confirmation* is the hole.

## Goals / Non-Goals

**Goals:** commits from one surface apply strictly in confirmation order with no overlap; zero UX
change; zero locking (UI-thread-confined state).

**Non-Goals:** cross-surface serialization (two different flyouts / a flyout + the grid — different
fields by construction, already safe); any change to commit semantics, refusal handling, or the
in-place mirrors; the M7 clamp/router dedup (separate change, same file — keep the diffs apart).

## Decisions

### D1 — `CommitChain`: a UI-thread task chain, not a lock or a queue structure

```csharp
internal sealed class CommitChain
{
    private Task _tail = Task.CompletedTask;

    public Task<bool> Run(Func<Task<bool>> commit)
    {
        Task<bool> next = RunAfter(_tail, commit);
        _tail = next;
        return next;
    }

    private static async Task<bool> RunAfter(Task prev, Func<Task<bool>> commit)
    {
        try { await prev; } catch { /* an earlier commit's fault must not poison later ones */ }
        return await commit();
    }
}
```

`Run` is called only from UI-thread event handlers, so the read-swap of `_tail` is single-threaded by
construction — no `Interlocked`, no `SemaphoreSlim` (the async-locking idiom the codebase avoids).
Each caller awaits **its own** task, so per-site success/revert handling is untouched; continuations
resume on the dispatcher in chain order, so `_lastKnown` updates land in confirmation order. A faulted
commit fails only itself (guarded `await prev`); the gate's commits return `false` rather than throw,
so the guard is belt-and-suspenders.

### D2 — One chain per `TsFieldsEditor` instance; one per window for inline Desired

Per-editor scope serializes cross-field commits within one flyout too — over-serialization of a few
milliseconds, and it keeps the rule simple ("one flyout, one commit at a time"). The window-level chain
for `Desired_Committed` likewise serializes across rows; same reasoning, same cost. Not per-control:
that would fix only the same-field case while leaving `_lastKnown` bookkeeping racing across fields
sharing one dictionary.

### D3 — Rejected alternatives

- **Disable the form while committing** — visible flicker, and disabling moves focus, which fires
  `TextBox.LostFocus` commits re-entrantly mid-fix: the cure invokes the disease.
- **Refuse mid-flight confirms** (busy-gate style `_committing` flag + revert) — bounces a *valid*
  second value back at the user; the busy gate's refuse-loudly posture fits bulk-vs-edit conflicts, not
  two keystrokes of the same user's intent.
- **Serialize inside the VM funnel / `TsEditGate`** — would also serialize independent different-field
  edits app-wide and change `ApplyAsync`'s contract for every caller; the race is a per-surface
  bookkeeping problem, so the fix belongs at the surface.

### D4 — Tests pin the chain; the wiring is build + hands-on

`CommitChainTests` (plain unit tests, no WinUI): second commit does not start until the first completes
(TaskCompletionSource-gated); results resolve in submission order even when the first is slow; a
throwing commit faults only its own task and the next commit still runs. The editor/window wiring is a
mechanical one-line change per site (compile-checked); rapid-edit feel is the user pass.

## Risks / Trade-offs

- [A hung commit stalls the whole surface's chain] → a commit is a local-db write with a busy-timeout;
  the same hang would today wedge that control's revert anyway. No new failure mode, just honest
  ordering.
- [Chain tail grows unbounded under pathological input] → each completed task is dropped when the next
  replaces `_tail`; memory is O(pending), and pending is human-typing-rate.

## Migration Plan

None. Clean rebuild.

## Open Questions

None.
