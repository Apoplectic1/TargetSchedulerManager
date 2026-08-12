# Proposal: add-catalog-export-duty

> **UNBLOCKED 2026-08-12 (same day):** the schema landed (ISM `docs\design\catalog-db-schema.md`)
> and decided the write protocol — **inbox file** (decision D1). TSM implements against
> **`..\IntervalSchedulerManager\docs\design\catalog-inbox-contract.md` only** (JSONL, TS-guid
> identity, three full-value-upsert ops, rule-#16 abort semantics) — TSM never opens `Catalog.db`
> and needs no schema knowledge, which shrinks this change below what the proposal sketched.
> Specs/design/tasks: write when picked up. Origin: the who-plans decision,
> `..\IntervalScheduler\docs\2026-08-12-who-plans-decision.md` (consequences table → TSM row).
> **Open v1 question flagged by IS-session review — resolve at spec time:** TSM's adoption verb
> *creates* TS exposure plans (and can create projects/targets) whose guids are unknown to import
> provenance; the contract's `desired-set` op cannot create an exposure plan (no template linkage
> in its payload). Either the contract gains an `exposure-plan-upsert` op (v1 revision, ISM-side)
> or this duty's scope excludes adoption-created rows — decide with ISM before implementing.

## Why

The 2026-08-12 who-plans decision made ISM the planning app, planning from `Catalog.db` (the
authored intent store, ISM-owned, local). During the TS→IS coexistence window the user keeps
authoring intent through TSM (targets, desired counts, enable state — the TS-editing surface that
already exists). Without an export, that intent is stranded in TS's schema and ISM's target pool
starves. TSM is the only component that sees every TS write, so the duty lands here: **update
Catalog.db whenever TSM writes TS.**

## What Changes

- New Services-layer step in TSM's write path: after a successful TS write (push-as-replay commit,
  in-grid edit write-back, adoption insert), project the affected intent into `Catalog.db` —
  targets, desired counts, membership/enable state, at the capture-configuration-cell grain TSM
  already keys on.
- One-way only: TSM → Catalog.db. TSM never reads Catalog.db for display and never plans; ISM is
  Catalog.db's owner — this duty is a *feed*, and the single-writer story needs deciding at design
  time (candidates: TSM appends to an inbox ISM ingests, or TSM writes via an AL Catalog-store API
  under a file lock — decide with the schema).
- No UI. No new verbs. TSM's charter is unchanged (TS-database manager until TS retires); this is
  its one and only ISM-era duty.

## Capabilities

### New Capabilities

- `catalog-export`: intent written to TS through TSM is reflected into the authored intent store
  (Catalog.db) so ISM's planner sees it — coverage, grain, and failure behavior (fail-fast, no
  silent skip: rule #16) to be specified when the Catalog.db schema exists.

### Modified Capabilities

*(none expected — existing TS-write capabilities keep their contracts; the export is additive. Recheck
at spec time.)*

## Impact

- TSM Services layer (write path hooks); new AL dependency on the Catalog.db access module when it
  exists (currently `Astronomy.Catalog` is scan/reconcile-shaped — the intent-store API is ISM
  stage-0 work).
- Cross-repo coordination: schema and write-protocol owned by ISM's Catalog.db design; this change
  implements TSM's side only.
- Lifetime: dies with TSM at TS retirement — the feed exists only for the coexistence window.
