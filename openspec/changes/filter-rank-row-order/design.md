# Design: filter-rank-row-order

## Context

See `proposal.md` — Why. Two independent ordering mechanisms are touched (established in the
2026-08-05 explore session, obs c73e):

- The **global comparator** (`ReconciliationLoader.BuildRows` final `rows.Sort`) orders sibling rows;
  its filter step uses `NaturalComparer` and its last tie-break is `RowPlane` enum order (`Ts, Disk,
  Both` — plan-first today, by accident of declaration order).
- The **rollup `detail` list** is built separately in `fp.OrderBy(c => c.Seconds)` and spliced in on
  expand by `VisibleRowTree`; it never passes through the comparator, so plane position on a seconds
  tie is stable-sort emit-order luck.

The two changes do not compete — one lives in each mechanism.

## Goals / Non-Goals

**Goals**
- Passband-rank filter order, one user-editable edit point.
- Deterministic expanded-rollup order: disk evidence first, bare plan commitments last.
- One universal reading — "commitments sit under evidence" — at both the sibling and detail level.

**Non-Goals**
- No change to matching, reconciliation keys, write-back, search, or the toolbar group-sort modes
  (they reorder groups; within-group order inherits the comparator automatically).
- No configuration UI for the rank; future filters are a one-line constant edit (user decision:
  "if I add additional filters, I will specify a new ranked order at that time").

## Decisions

1. **Rank home: `Models/Format.cs`, a `FilterRank` string array** beside the camera alias — both are
   "display conventions for codes." Rejected: `FilterBrushes` (that file is color, and the palette's
   switch order is incidental); the Library (presentation is consumer-specific — shared-lib
   discipline).
2. **Rank semantics: index-of with unranked → after-ranked, natural among themselves.** Comparison
   stays inside the existing comparator (rank index replaces `NaturalComparer` for the filter step);
   ties fall through to the later keys unchanged.
3. **Detail order: partition then order, not a comparator.** The detail list is built in one place;
   partitioning into disk-backed (Disk + merged Both lines) then plan-only blocks, seconds ascending
   within each, is clearer than threading a second comparator through. Merged lines stay in the disk
   block (user decision — they are evidence of actuals).
4. **Flip the global plane tie-break to Disk-before-TS** rather than reordering the `RowPlane` enum:
   the enum order is public reading surface elsewhere (source dropdown semantics live off `RowSource`,
   but `Plane.CompareTo` is only used in this one tie-break) — invert at the comparison site with a
   comment, keeping the enum declaration stable.

## Risks / Trade-offs

- [Tests pin alphabetical order] → resync loader-order tests to the rank; add coverage for unranked
  codes and the plane tie-break.
- [A future filter code silently sorts last] → intended behavior (after-ranked rule); the user
  re-specifies the rank when adding filters.

## Migration Plan

Display-only; no data, schema, or push-path impact. Ship + visual verify (row order is
user-verifiable — change waits for field verification before archive).
