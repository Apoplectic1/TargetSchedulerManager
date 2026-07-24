# Proposal: remove-alias-fold

## Why

The alias-fold mechanism exists to let a "benign" multi-claim (≥2 TS names that each exactly match a disk
identity facet) auto-resolve unflagged. The hand-edit doctrine (2026-07-08) abolished that category, and the
fold demonstrably masked a real defect for weeks: the M27/Dumbell twin was never intentional, but the fold
presented it as benign — summing a *disabled* twin's goals into Desired while NINA showed something else
("explained ≠ approved"). Strict-equality alias naming also silently folds *identical accidental twins*.
Removal was agreed in explore 2026-07-08 (NOTEBOOK correction entry has the footprint); after the pending
Dumbell consolidation the machinery covers zero rows, so this is dead-code deletion.

## What Changes

- **Library (`..\Library\Astronomy.Catalog`, separate repo) — BREAKING (API surface):**
  - `TargetResolver`: multi-claim classification collapses to **duplicate-always-flagged** — the
    `IsAliasName`-gated alias branch and `IsAliasName` itself are deleted.
  - `CatalogBuildReport`: `AliasTsTargets` parameter, `AliasTsTarget` record, `AliasMemberCount`, and the
    `TargetMatchIssues.Alias` flag are deleted.
  - `WriteBackPlanner`: the alias exemption branch (one plan per member → auto-write to every member) is
    deleted; ex-alias multi-plan cells route to `ManualGroup` with `DuplicateFold` — held for hand fix,
    never auto-written.
- **TSM app:**
  - Grid: the `alias` badge dies; the multi-plan badge loses its `!isAlias` suppression (>1 plans on a
    cell is always badged).
  - Ambiguity report: alias info-lines and the same-key alias exemption die — any same-key plan group
    with >1 plan is an action item; consolidation wording drops the "intentional alias" escape.
  - Diagnostics/log lines drop their `aliases=` counters.
- **Specs/docs:** the active `ts-ambiguity-report` change's delta spec is amended in place (its
  "alias folds are informational" requirement and alias-exemption scenario die — premise dead; this
  change's own delta mirrors the amendment so archive order doesn't matter). `DOMAIN.md`'s convention
  drops its alias escape clause: **one TS row per position, no exceptions**. `ARCHITECTURE.md` /
  Library `ROADMAP.md` references updated.
- **Not changed:** single-target naming freedom — a lone TS `Dumbell` still matches dir `M27 - Dumbell`
  via ordinary name validation (`IsAliasName`'s only call site was the multi-claim fold).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ts-ambiguity-report`: the "adjudicated folds are information" requirement is removed and the same-key
  detection requirement loses its alias exemption. NOTE: this capability is not yet in main specs — it
  lives in the active `ts-ambiguity-report` change, whose delta is amended in place by this change; the
  delta here mirrors the post-amendment text so a later sync is a no-op in either archive order.

No other main-spec capability mentions aliases (`write-back`'s main spec never specced the planner
exemption), so grid-badge and planner behavior changes are implementation-level.

## Impact

- **Cross-repo:** library edit (resolver/report/planner + tests) commits in `..\Library`; TSM edit
  (loader/report/VM/tests + docs) commits here. Both repos green independently.
- **Behavioral:** until the user consolidates the M27/Dumbell twin on BIRDWATCHER, it now surfaces as a
  flagged **duplicate** (grid badge, manual write-back group, report action item) instead of a silent
  fold — exactly the surface-for-decision doctrine. No `Catalog.db` schema impact (report-plane only).
- **No back-compat:** `CatalogBuildReport`'s constructor shape changes; the sole consumer (TSM) updates
  in lockstep. No migration, no obsolete shims (house rule 15).
