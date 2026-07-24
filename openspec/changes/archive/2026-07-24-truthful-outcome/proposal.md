# truthful-outcome — sync outcomes report what actually happened

## Why

The 2026-07-24 review's blind cross-check found two places the sync flow reports the opposite of what
happened, both violating the project's report-outcomes-faithfully doctrine:

1. **The push's closing pull can throw *after* the journal is cleared.** `TsSync.Push` rewrites the
   journal (`TsSync.cs:493`) *before* the closing pull (`:517`), whose catch handles only
   `OperationCanceledException`. A `SqliteException` (SMB drop mid-backup) or `IOException` (swap
   failure) propagates into `PushAsync`'s catch, which logs *"push threw — journal intact, re-push after
   fixing the cause"* and shows **"PUSH FAILED … edits stay journaled"** — both false: the remote writes
   all landed and the journal is empty. The code comment asserts the backwards claim explicitly. The same
   throw via the open-with-dirty push lands in `LoadAsync`'s catch as "load failed". Data converges (the
   changed remote mtime forces a pull next open), but the user is told to re-push nothing and may
   re-enter "lost" edits by hand.
2. **Discard + cancelled pull leaves the discarded values presented as truth.** The open-with-dirty
   Discard clears the journal and baseline *first*, then pulls; a cancelled pull proceeds on the old
   local copy — which still physically holds every "discarded" value, now journal-less: no dirty badge,
   no → marks, nothing to push, for the rest of the session. Converges only at the next open's forced
   pull.

## What Changes

- **Contain the closing pull inside `Push`**: any closing-pull failure (not just cancellation) is caught
  there and reported as what it is — *push succeeded, closing pull failed, next open pulls fresh* — never
  as a push failure. `PushResult` carries the fact so the status line can say it; the backwards comment in
  `PushAsync`'s catch becomes true again (every remaining throw source really does precede the journal
  rewrite).
- **Discard becomes pull-first**: the discarding pull runs *before* anything is cleared; the journal is
  cleared only when that pull lands (the swap has physically replaced the discarded values). A cancelled
  or failed discard-pull changes nothing — journal, baseline, badge, and marks stay intact and truthful,
  and the user simply still has their unpushed edits.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `ts-sync-model`: (a) the push-replay requirement gains truthful-outcome language — a fully-applied push
  SHALL be reported as pushed even when its closing pull fails; the journal SHALL be cleared exactly when
  the writes were applied+verified, never contradicted by the report. (b) the unpushed-state requirement's
  Discard changes from "clear the journal and pull fresh" to "pull fresh, then clear the journal only when
  the pull lands" — a cancelled discard-pull SHALL leave the dirty state intact.

## Impact

- **App only**: `Shared/TsSync.cs` (`Push` closing-pull containment, `PushResult` shape, `Discard`
  semantics), `ViewModels/MainViewModel.cs` (`PushAsync` catch comment/status, `PrepareTsForLoadAsync`
  Discard case, `DescribePush`), tests in `TsSyncTests` + a `MainViewModelTests` addition.
- **Docs**: `ARCHITECTURE.md` sync-model section (Discard ordering + push outcome contract), CHANGELOG/
  ROADMAP digest, same commit.
- **UX change (visible)**: a push whose closing pull fails now reports "pushed N · closing pull failed —
  next open pulls fresh" instead of "PUSH FAILED"; a cancelled discard-pull now reports "discard not
  completed — unpushed edits kept" and the grid keeps its dirty badge/marks.
