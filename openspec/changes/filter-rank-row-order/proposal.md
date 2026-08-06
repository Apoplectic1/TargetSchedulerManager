# Proposal: filter-rank-row-order

## Why

Within an expanded target, rows order alphabetically by filter code (B, G, H, L, O, R, S) — an order
with no physical meaning — and the source lines under an expanded Both rollup fall wherever
seconds-ascending emit order drops them, so the TS plan line can land anywhere among the disk lines.
The user reads a target's story passband-first and wants the bare plan commitment to sit under the
disk evidence (2026-08-05, obs c73e).

## What Changes

- Filter ordering switches from alphabetical to a **fixed display rank: H, S, O, L, R, G, B**.
  Codes outside the rank sort after B in natural order. The rank is one named app-side constant
  (the app's first canonical filter-order home); adding a filter later means editing that one list.
- An expanded Both rollup presents its source lines in **two blocks: disk-backed lines first
  (Disk lines and nested Both lines), plan-only TS lines last** — seconds ascending within each block.
- The global plane tie-breaker flips to **Disk before TS**, so sibling rows tying on every other key
  read the same way ("commitments sit under evidence") instead of contradicting the rollup rule.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `reconciliation-grid`: the "Row order keeps one filter's rows contiguous" requirement changes
  (filter compares by display rank, not natural order; plane tie-break becomes Disk-before-TS), and a
  new requirement pins the expanded-rollup detail-line order (disk-backed block then plan-only block,
  seconds ascending within each).

## Impact

- `TargetSchedulerManager.App/Services/ReconciliationLoader.cs` — the global sort's `byFilter` step
  and plane tie-break; the rollup `detail` list construction.
- `TargetSchedulerManager.App/Models/Format.cs` — new `FilterRank` constant beside the camera alias.
- App tests pinning alphabetical filter order or detail-line order resync.
- `UI.md` sort-precedence text updates in the same commit (rule: docs ride the code).
- No Library change; no schema/data change; display-only (no reconciliation key is affected).
