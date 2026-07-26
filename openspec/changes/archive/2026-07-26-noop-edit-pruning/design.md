# noop-edit-pruning — Design

## Context

The journal (`TsJournal`) is append-only per verified write; `Collapse()` reduces to first-Old → last-Value
per field but never asks whether they became equal. The editor already short-circuits same-value db writes
(`TargetSchedulerEditor.NormalizedEquals` → verified, no write) — the journal is the only layer without
no-op awareness. All mark/count/review/push surfaces read the journal (directly or via `Collapse`), and the
badge uses an incrementally-maintained distinct-field set (`_fieldKeys`, review N2).

## Goals / Non-Goals

**Goals:** a field whose value returns to its baseline (since last push) reads clean **everywhere**; no
no-op writes replay to BIRDWATCHER; single same-value re-commits journal nothing.

**Non-Goals:** no change to inbound facts (a pre-edit `←` must survive the revert — the user's gotcha);
no change to write-back masking; no journal-file migration (rule 15).

## Decisions

**D1 — Prune at `Append`, not filter at `Collapse`.** When a write nets a field to baseline, `Append`
removes the field's entries (crash-safe rewrite, same mechanism as push retention) and returns null;
first-touch same-value commits return null without touching the file. Invariant: *the journal never holds
a net-no-op field* — so marks, badge, review, push replay, and the dirty-open prompt all heal with zero
per-surface code (the user's "anywhere syncmarks are used", by construction). *Alternative rejected:*
filtering in `Collapse()` — entries would linger, keeping the dirty-open prompt and push retention aware
of fields no surface shows; three consumers to patch instead of one producer.

**D2 — Baseline = the first journaled Old since the last push, remembered per field.** Maintained as a
dictionary beside `_fieldKeys` (same lock, same rebuild sites: `Load`, `ReplaceAllLocked`); for a field's
first entry the incoming `old` is the baseline. Push retention naturally resets baselines — after a push,
the pushed value becomes the next edit's Old. This is the user-identified state to remember; the *initial
sync state* needs no remembering because inbound is a separate store.

**D3 — Equality via the one invariant text rule.** `TsValueText.From(Canonicalize(value))` vs the stored
baseline string (ordinal; null==null). The baseline was produced by the editor's `ToText` — the same
invariant `Convert.ToString` rule — and the editor's own no-op short-circuit already trusts this
comparison, so the journal is not inventing a new equality. A residual mismatch fails safe: the field just
stays marked (today's behavior).

**D4 — `Append` returns `TsJournalEntry?`.** Callers (`RecordEdit`, `RecordWriteBack`, gate) ignore the
return; tests that use it assert non-null. Write-back masking stays keyed to the `RecordWriteBack` call,
not the entry — `WriteBackStep` skips already-stamped values upstream, so a pruned stamp is not a real
path.

**D5 — No load-time pruning.** The invariant holds for any journal this version wrote; older sidecars
(rule 15: no compat code) at worst show today's behavior until pushed/discarded.

## Risks / Trade-offs

- [A revert triggers a full crash-safe file rewrite] → Rare (user round-trips a field), same cost as the
  push-retention rewrite; appends stay cheap.
- [Cadence side effects already happened locally on the intermediate writes] → Remote is untouched and
  consistent (net value unchanged); the local cadence clear is overwritten by the next pull, and TS never
  runs against the local copy.
- [Baseline string vs typed value formatting drift] → D3: both sides are the same invariant rule; fails
  safe to "stays marked".

## Migration Plan

None (D5).

## Open Questions

None — option 1 chosen by the user 2026-07-26, uniform across all mark surfaces.
