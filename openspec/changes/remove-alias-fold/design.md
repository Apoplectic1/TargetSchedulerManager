# Design: remove-alias-fold

## Context

The alias fold lives in three library seams and four app seams. Library: `TargetResolver` classifies a
multi-claim (one disk unit claimed by ≥2 TS targets) as an `AliasTsTarget` when every claimant's name
exactly equals a disk identity facet (`IsAliasName`), else a `DuplicateTsTarget`; `CatalogBuildReport`
carries `AliasTsTargets` + `AliasMemberCount` + the `TargetMatchIssues.Alias` flag (excluded from
`IsIdentityFlagged`); `WriteBackPlanner` exempts an alias-fold cell whose plan count equals the member
count and auto-writes the disk count to every member. App: the grid's `alias` badge and `!isAlias`
multi-plan-badge suppression (`ReconciliationLoader`), the ambiguity report's alias info-lines + same-key
alias exemption + "intentional alias" consolidation wording (`AmbiguityReport`), and two `aliases=`
diagnostic counters. The sole real instance (M27/Dumbell) was adjudicated unintentional; consolidation is
on the user's BIRDWATCHER hand-fix list.

## Goals / Non-Goals

**Goals:**
- Delete the mechanism end to end: a multi-claim is always a flagged duplicate, held for hand fix.
- Keep both repos green independently; docs and specs updated in the same commits.

**Non-Goals:**
- No change to single-target naming freedom: a lone TS `Dumbell` still matches dir `M27 - Dumbell` via
  ordinary name validation — `IsAliasName`'s only call site is the multi-claim classification.
- No `Catalog.db` schema change (the fold lives in the report plane, not the schema).
- No obsolete shims / no staged deprecation — the sole consumer (TSM) updates in lockstep (house rule).

## Decisions

### D1 — Resolver: classification collapses to duplicate-always-flagged
`w.AssignedTs.Count > 1` → `DuplicateTsTarget`, unconditionally; delete `IsAliasName` and the `aliases`
list. Ex-alias rows pick up the `Duplicate` issue flag, so the grid badges them and write-back holds them
— surfacing is the point.

### D2 — Report type: delete the alias surface, keep flag values stable
`AliasTsTarget` record, `AliasTsTargets` ctor param, `AliasMemberCount`, `_aliasMembersByDirectory`, and
`TargetMatchIssues.Alias` (= 1) all die. Remaining flag values keep their current bit positions (gap at 1)
— renumbering buys nothing and the enum is not persisted. `IsIdentityFlagged` needs no change: it never
included `Alias` or `Duplicate`; duplicates route to manual via the planner's fold check, unchanged.

### D3 — Planner: ex-alias cells become `ManualGroup(DuplicateFold)`
Delete the alias-exemption branch. A multi-plan cell on a duplicate-fold target now takes the existing
`else` path: `ManualReason.DuplicateFold` (flag present) — held, never auto-written. This is the doctrine:
a fold is a defect to consolidate by hand, not a shape to write through.

### D4 — App: badge + report simplifications
`isAlias` dies in `ReconciliationLoader`: the `alias` badge is gone and the multi-plan badge condition
becomes `plans > 1` with no suppression (an ex-alias row shows `duplicate` + the multi-plan badge — both
true). `AmbiguityReport`: the alias info-lines loop dies; the same-key check drops the
`AliasMemberCount != count` exemption (any same-key group with >1 plan is an action item); the
planned-only-twin consolidation instruction drops its "or make the names an intentional alias" clause.
Diagnostic counters (`aliases=` in the load log and Ctrl+N context) die.

### D5 — Spec handling: amend the active change's delta in place, mirror here
The only spec text mentioning aliases is the active `ts-ambiguity-report` change's delta (not yet in main
specs). That file is edited in place to the post-removal text; this change's own delta mirrors it exactly,
so either archive order converges (see the note in this change's spec file). Main specs mention aliases
nowhere else — grid-badge and planner changes are implementation-level.

### D6 — Docs
`DOMAIN.md`: badge list drops `alias`; the authoring convention's alias escape clause is replaced by
**one TS row per position, no exceptions**. TSM `ARCHITECTURE.md` drops "aliases /" from the
reported-never-dropped list. Library `ROADMAP.md`: the "Open: alias-vs-duplicate handling" line is
resolved (mechanism removed); shipped-history narrative stays as history.

## Risks / Trade-offs

- [Until the user consolidates M27/Dumbell on BIRDWATCHER, every load shows a duplicate badge, a manual
  write-back group, and a report action item] → Intended — that is the surface-for-decision doctrine; the
  hand fix is already on the user's list.
- [Ex-alias write-back cells stop auto-writing counts to both twins] → Correct: writing a disk count into
  an unintentional twin's plans was the masking behavior being removed.
- [Library API break (`CatalogBuildReport` ctor)] → Sole consumer is TSM; both repos change together, no
  compat shims (house rule 15).

## Open Questions

None — footprint and doctrine were agreed 2026-07-08 (NOTEBOOK correction entry).
