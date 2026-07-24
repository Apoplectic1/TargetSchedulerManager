# review-polish — design

## Context

Ten independent small items from the 2026-07-24 review (Cowork + blind cross-check), each verified
against current code. No cross-dependencies except N2 feeding `SyncBadgeText`.

## Goals / Non-Goals

**Goals:** land every remaining accepted review item in one low-risk sweep; zero behavior change except
N3's strictly-more-logging.

**Non-Goals:** M4 (its own change, next); journal-first durability redesign (M2 stays a doc fix); the
deliberately-skipped list in the proposal.

## Decisions

- **D1 (M2):** docs tell the truth instead of the code chasing an unreachable guarantee. The honest
  contract: append is flushed-to-OS before the entry is visible (survives process death); OS/power loss
  can drop the tail line, and the db-commit→journal-append pair is not atomic, so `Flush(true)` could
  narrow but never close the window — the failure mode (local db holds an unreplayable write) is stated
  where the torn-line policy already is.
- **D2 (N2):** `CollapsedCount` = count of distinct field keys, maintained as a `HashSet<string>` under
  the existing `_lock` (`Append` adds; `ReplaceAllLocked` rebuilds). The badge reads one int property
  (lock held only for the read) instead of building dictionaries on the UI thread.
- **D3 (M7 router):** `TryCommitMirroredField(group, row, column, value)` returns `Task<bool>?` —
  null = not a mirrored column, caller falls through to the generic `SetTsFieldAsync` path (which keeps
  the pair-warn bookkeeping in the lambda where its captures live). The switch preserves the original
  guard order/semantics (row-context vs group-context columns).
- **D4 (N3):** `FireAndLog(Func<Task>, string what)` — awaits and catches-with-log. The VM methods catch
  their own domain failures already; this catches what escapes them (resource lookup faults, dialog
  bugs) that today die as unobserved task exceptions, invisible in tsm.log.
- **D5 (N4):** `TsValueText.From(object?) => string?` (invariant `Convert.ToString`, null → null) in
  `Shared\`; `TsSync` wraps with `?? "null"` (a review line must show *something*), `SyncMarks` takes it
  raw (a tooltip line's null is "no old value"). One conversion rule, two display contracts — the
  difference is intentional and now visible at the call sites.
- **D6 (N5):** `MaxBusyRetries = BusyTimeoutMs / RetrySleepMs` named constants;
  `cancel.WaitHandle.WaitOne(RetrySleepMs)` replaces `Thread.Sleep` so a cancel interrupts the nap (the
  loop-top `ThrowIfCancellationRequested` then fires).

## Risks / Trade-offs

- [Wide but shallow diff] → every item is independently revertible; the full suite plus new
  `CollapsedCount` tests lock behavior.

## Migration Plan

None. Clean rebuild; auto-archive per standing rule.

## Open Questions

None.
