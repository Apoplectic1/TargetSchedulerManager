# push-rule-dedup — one definition each for the push path's two shared rules

## Why

Review finding M6 (2026-07-24, `docs/2026-07-24-app-code-review.md`, both halves verified): two
invariant-bearing rules in `TsSync` are spelled twice and can drift independently.

1. **Count-entry selection.** `PreparePush` inlines `acquired ?? accepted` while the replay's
   `CountEntry` helper implements `acquired ?? accepted ?? First()`. They agree today only because
   `PreparePush` independently special-cases the desired-only group; the next count column added to
   write-back must be fixed in two places, or the review shows something the replay doesn't do — in the
   push path, where a wrong review is the dangerous kind of wrong.
2. **Baseline equality.** `ShouldPull` and `PreparePush`'s `RemoteChangedSinceBaseline` each spell out
   `RemoteLength != probe.Length || RemoteLastWriteUtc != probe.LastWriteUtc`. Two spellings of "the
   baseline still matches", one skip rule and one staleness warning consuming them.

## What Changes

- `CountEntry(plan)` becomes the **single** selection rule: `PreparePush` calls it and derives
  "desired-only group" from the returned entry's column instead of re-querying — one preference chain,
  two consumers (review + replay).
- A private `BaselineMatches(TsDbStat probe)` becomes the **single** baseline-equality definition:
  `ShouldPull` reads it straight (`sidecar || !matches`), the staleness warning negates it — with the
  existing "no baseline ⇒ no warning" behavior preserved exactly (a naive `!BaselineMatches` would
  newly warn on unbaselined pushes, which would be a wrong claim).
- Behavior-preserving throughout — existing `TsSyncTests` green is the verification; one new test pins
  the desired-only review line through the shared rule.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `ts-sync-model`: gains the requirement this dedup protects — the push review SHALL present the same
  count-entry selection the replay executes, and the staleness warning SHALL negate the same
  baseline-match definition the pull skip rule reads (one definition each, so the review can never show
  something the replay doesn't do).

## Impact

- **App only, one file of logic**: `Shared/TsSync.cs` (`PreparePush`, `ShouldPull`, new
  `BaselineMatches`, `CountEntry` doc). Tests: `TsSyncTests` (existing suite = behavior lock; one
  addition). Docs: CHANGELOG/ROADMAP digest line, same commit. No UI, no schema, no persisted state.
