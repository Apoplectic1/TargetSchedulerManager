# push-rule-dedup — design

## Context

Two rules in `TsSync.cs` exist as two spellings each (review M6, verified):

- Count-entry preference: `PreparePush` (`acquired ?? accepted`, null ⇒ desired-only display) vs
  `CountEntry` (`acquired ?? accepted ?? First()` — the desired-only fallback carries the disk count,
  because the write-back ratchet only ever raises desired TO the count).
- Baseline equality: `ShouldPull` (`baseline is not { } b || b.RemoteLength != probe.Length || …`) vs
  `RemoteChangedSinceBaseline` (`baseline is { } b && (b.RemoteLength != probe.Length || …)`).

## Goals / Non-Goals

**Goals:** one definition per rule; identical observable behavior; the next write-back count column or
baseline field lands in exactly one place.

**Non-Goals:** the M1 `Push` decomposition (separate change); any review-dialog content change.

## Decisions

### D1 — `CountEntry` is the rule; `PreparePush` derives display intent from its result

`PreparePush` calls `CountEntry(plan)` and maps: returned entry's column is `desired`
(case-insensitive) ⇒ the group is desired-only ⇒ no count pair displayed (`count = null`); otherwise the
entry *is* the count. Equivalent today because write-back journals only acquired/accepted/desired, so a
group without acquired/accepted falls through to `First()` = its desired entry; the label expression
(`count ?? desired ?? First()`) collapses to the returned entry's label in every case. `CountEntry`'s
doc comment gains the "one rule, two consumers (review + replay)" note.

### D2 — `BaselineMatches(TsDbStat probe)` with the null-baseline asymmetry kept explicit

```csharp
private bool BaselineMatches(TsDbStat probe) =>
    _state.Baseline is { } b && b.RemoteLength == probe.Length && b.RemoteLastWriteUtc == probe.LastWriteUtc;
```

`ShouldPull` ⇒ `probe.HasSidecar || !BaselineMatches(probe)` (unbaselined ⇒ no match ⇒ pull — same as
today). The staleness warning ⇒ `probe is not null && _state.Baseline is not null && !BaselineMatches(probe)`
— the explicit `Baseline is not null` guard preserves today's "no baseline ⇒ no warning" (an unbaselined
push must not claim "remote changed since the baseline" when there is no baseline). Note: the review's own
fix snippet dropped that guard — the naive negation would have introduced a false warning; the asymmetry
(skip rule treats null as mismatch, warning treats null as silence) is why one bare helper can't serve
both unguarded.

## Risks / Trade-offs

- [Silent behavior drift during the rewrite] → the whole existing `TsSyncTests` push/pull suite is the
  lock; plus one new test pinning the desired-only review line (no phantom count pair, ratcheted desired
  shown) — the review-side half `CountEntry`'s replay tests don't cover.

## Migration Plan

None. Pure refactor; clean rebuild.

## Open Questions

None.
