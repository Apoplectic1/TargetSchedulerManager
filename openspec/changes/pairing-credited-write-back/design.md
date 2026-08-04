# Design — pairing-credited-write-back

## Context

See `proposal.md` → Why for motivation. Current state that shapes the approach:

- `WriteBackPlanner.Plan` (Library) buckets disk actuals by `(target, filter, purpose, seconds)` over
  `InventoryFilter` rows, filtering per-frame framing via `FramingCluster.ServesPlanRotation` before
  summing (`WriteBackPlanner.cs:47-54`). Since `capture-config-keys`, `InventoryFilter` is already
  config-bucketed and carries `TypicalGain` / `TypicalOffset` / `TypicalBinningX/Y` — the planner's inputs
  need **no** schema or scanner change.
- The capture-config pairing comparison ("expressed-and-equal; sentinel compared as the value it is")
  exists today in at least two places: the library reconciler's cell-key merge and the app's
  `AdoptionPlanner.MismatchReason` (which documents itself as mirroring the reconciler). The observed bug
  is precisely these rules drifting from write-back's coarser key.
- `AdoptionPlanner.Build` / `BuildBulk` (app) seed born-complete counts unconditionally; `WouldPair` is
  already computed per candidate for the dialog caution.
- Row-scoped badges (`camera`, `cam≠`) and the deepest-visible-line rule exist in the app's
  projection/render layer; `Badges.Canonical` maps decorated text to canonical tokens.

## Goals / Non-Goals

**Goals**
- One capture-config pairing predicate in the Library, consumed by write-back crediting and referenced by
  every surface that states a pairing verdict (grid merge, adoption caution) — drift becomes impossible,
  not just fixed.
- Surgical path (`SingleTargetPlanner`) adopts the same crediting in the same change, even though no app
  UI invokes it — it is a shipped library capability and must not preserve the stale rule.

**Non-Goals**
- No change to `desired` semantics (ratchet-up stays; never lowered — user decision (a)).
- No change to the framing crediting rule, the manual/held classification, `UnplannedFrames` gating
  (`Both` targets only), or the journal/push mechanics.
- No auto-correction of sentinels, ever; no new editing surface.
- No batching/suppression of the first-run decrease flood in the push review — it is the one-time truth.

## Decisions

**D1 — Credit inside the bucket, per plan group, not by widening the tuple key.** The 4-tuple grouping
stays (it also drives same-key-multiplicity manual detection and `UnplannedFrames`). Within a bucket, an
inventory row credits a plan group's sum only if it passes the shared pairing predicate against the
group's template. Alternative — adding gain/offset/bin to the dictionary key — was rejected: pairing is
expressed-and-equal with sentinel asymmetry, not tuple equality, so a widened key either mis-models the
rule or needs a custom comparer that hides it. Auto-writable groups hold exactly one plan (multiplicity is
manual), so "the group's template" is well-defined; a manual multi-plan group needs no credit sum.

**D2 — The pairing predicate is lifted to one Library helper.** A small static predicate (sited beside the
existing merge comparison the reconciler uses; exact home chosen at implementation against the current
code) takes the disk cell's expressed config and a template's `gain`/`offset`/`bin` and answers
pairs/doesn't with a reason. `WriteBackPlanner` and `SingleTargetPlanner` call it; `AdoptionPlanner`'s
`MismatchReason` is re-based onto it (its wording layer stays app-side). Semantics, fixed here once:
a dimension separates when both planes express it and values differ; an unexpressed disk dimension never
separates; a template camera-default sentinel (`-1`) **never pairs** (user convention: sentinels are
errors, and an unspecified value can never be asserted to agree — already adoption's rule).

**D3 — Adoption seeding keys off the pairing verdict.** The single plan-payload builder (`PlanInsert`,
which both `Build` and `BuildBulk` funnel through) derives the verdict from the same pure predicate the
dialog's caution used, on the same inputs — pairs → born-complete, else 0/0/0 — so the promise and the
payload cannot disagree (the established dialog-promise ≡ grid-render principle). *(Adjusted at
implementation from "thread the candidate's `WouldPair` through the accept path": threading would have
churned the dialog-choice records for no added safety — the verdict function is pure and the inputs are
identical, so recomputation is the same fact.)*

**D4 — `sentinel` badge is computed in the projection layer from current template state.** Same shape as
`camera`: row-scoped on plan rows whose template has `gain`/`offset`/`readoutmode` = −1, union-rolled into
headers, warning severity via the canonical severity map. Recomputation is free — the projection already
rebuilds on load/pull/editor-close re-reconcile. `readoutmode` participates in the badge only, not in
pairing (it is not a reconciliation key and the disk plane does not express it).

**D5 — First-run settling is deliberately unmitigated.** The ~245-bucket historical backlog stamps as
ordinary journaled decreases, reviewed decreases-first at push like any write-back. No dry-run, no cap, no
phased rollout — disk is truth, chosen knowingly; the review dialog is the safety.

## Risks / Trade-offs

- [Historical progress zeroes en masse; re-enabled targets could reschedule mass re-shoots] → Known and
  accepted by the user ("disk is truth, 100%; I manage desired/enable by hand"). The push review shows
  every decrease before BIRDWATCHER sees it.
- [A scanner regression (e.g. gain not read) would now zero counts instead of merely splitting grid rows]
  → The same review-decreases-first tripwire that already guards empty-bucket zeroing; decreases are loud.
- [Three surfaces claim to share one rule after D2; a partial re-base leaves silent drift] → Task explicitly
  re-bases the reconciler-side comparison and `AdoptionPlanner` onto the helper (or asserts equivalence via
  a test that runs the same cases through all consumers).
- [`UnplannedFrames` note volume grows ~245] → Accepted; the user iterates report → grid fix → re-read and
  wants completeness.

## Migration Plan

None (rule #15): pure behavior change, no schema, no persisted-state migration. Library ships first
(cross-repo release ordering) if a release is cut. Operational heads-up to the user at ship: the first
load stamps the historical backlog; the first push carries hundreds of decreases — review once, push once.

## Open Questions

None.
