# Design: rotation-framing-key

## Context

See `proposal.md` → Why. All grouping/keying logic lives in `..\Library\Astronomy.Catalog`
(`ImageLibraryScanner` builds per-unit aggregates keyed by capture configuration since
`capture-config-keys`); the app owns the pairing predicate and the grid. `Astronomy.XISF` already exposes
everything needed per frame: `RotatorSkyAngleDeg` (OBJCTROT), `RotatorPosAngleDeg` (POSANGLE),
`RaDegrees`/`DecDegrees`. The TS plane expresses rotation **per target** (one value), not per plan — unlike
every key shipped so far, the disk side varies per frame while the TS side is a single target-level
comparand.

Everything below is calibrated by the 2026-07-29 measurement spike over the live library (18,650 frames,
104 units): 71.9% of frames carry a sky angle, 28.1% mechanical-only, 3 frames neither; real framings sit
≥ 9° apart fold-180 while within-framing jitter is ≤ 0.2° (NINA snaps the rotator); every true flip pair's
centroids coincide within 0.12°; the sky−mech zero point is stable mod-180 within a session block but
drifts 19–35° across remounts in 5 of 30 (unit, camera) groups.

## Goals / Non-Goals

**Goals:**
- Assign every frame a framing cluster before the capture-configuration grouping, so framing becomes one
  more bucket key with the established column/pairing mechanics.
- Keep the clustering deterministic, tolerance-insensitive over the measured library, and testable with
  synthetic fixtures.
- Answer `capture-config-keys`' open vocabulary question: a tolerance-clustered dimension reuses the
  `mixed` rollup marker; no new vocabulary.

**Non-Goals:**
- Overlap-percentage computation or display (needs image pixel dimensions `XisfHeader` does not expose;
  deferred — the design must simply not preclude adding it as a column later).
- Any mechanical→sky conversion, per-session zero-point calibration, or WBPP-side anything.
- Changing unit formation (targets/panels), matching/anchoring, or any total.

## Decisions

### Framing clusters are assigned per unit, before the aggregate GroupBy
A pre-pass over a unit's frame readings assigns each reading a framing cluster; the cluster then joins the
existing aggregate grouping tuple exactly like gain/offset/binning. Alternative — clustering per
(filter, exposure) bucket after grouping — rejected: a framing is a property of the unit's pointing
history, not of one filter's frames, and per-bucket clustering would let the same physical framing get
different cluster identities across filters.

### Cluster identity = (expression, fold-180 angle group, spatial group)
Frames partition by rotation expression first — **sky** (OBJCTROT present), **mech** (POSANGLE only),
**unknown** (neither) — because the three are mutually incomparable (unknown zero point; nothing at all).
Within sky and mech partitions separately: single-linkage gap clustering on the angle folded mod 180°
(tolerance **5°**), then, within each angle group, single-linkage spatial clustering on plate-solved
coordinates (haversine link distance **0.5°**). Unknown frames join the unit's dominant cluster only if it
is unambiguous (a single cluster exists); otherwise they form their own `unknown` cluster — never silently
attributed.

Why single-linkage both times: a campaign drifts (dither, pointing scatter — measured up to 0.54° span in
a legitimate single framing), and chaining tolerates drift while a genuine stray (M97's frame 1.3°+ from
its nearest sibling) still splits. Why 5°/0.5°: the nearest genuinely distinct framing pair measures 9°
(mech, Eastern Veil) / 10° (sky, Barnard 202) and jitter is ≤ 0.2°, so any rotation tolerance in 1–5°
yields identical clusters on the real library; 5° sits centred and matches the spike. Both are named
constants beside the match tolerance, not settings.

### Flip folding is built into the metric, guarded by the spatial step
Clustering operates on angles mod 180 from the start, so 0°/180° frames are indistinguishable *by angle*
and merge — unless their centers differ, in which case the spatial step splits them anyway. This is the
"RA/DEC centroid guard" from the deferral, landing as a consequence of ordering (angle fold first, spatial
split second) rather than as a special flip rule. Raw (unfolded) angles are retained on the aggregate for
display/debugging.

### Pairing compares the cluster's circular-mean sky angle to the TS target rotation, fold-180, same 5°
In the library's cell join (`ReconciliationProjection` — where the capture-config pairing already lives; the
app only renders the cells): a disk bucket is rotation-eligible iff its cluster expresses sky rotation
and the anchored TS target carries a rotation; then eligible iff fold-180 delta ≤ 5°. All other cases
(mech, unknown, TS rotation null) skip the rotation term entirely — the camera precedent, verbatim. The
TS comparand is the *target's* rotation applied to every one of its plan buckets, since TS cannot express
per-plan rotation. A plan whose rotation no captured framing serves keys its own plan-only cell; when a
plan's rotation cannot be compared at all, it attaches to the largest otherwise-agreeing cluster so
rotation never prevents pairing.

### Display: one `Rot` column + `framing` badge; rollups reuse value-or-`mixed`
One new column in the capture-config group: sky clusters show the cluster's fold-180 circular mean
(`50°`); mech clusters show the mechanical angle visibly marked (e.g. `228°m`); unknown shows the em dash.
TS rows show the target rotation fold-180 so agreeing rows read identically. Rollups reuse the
value-or-`mixed` pill mechanics unchanged. Disk rows failing the rotation pairing term carry a
warning-severity row-scoped `framing` badge (the `cam≠` mechanics). Excluded from sort, like the rest of
the capture-config group.

### Write-back credits by the same serving rule (in-flight addition, user decision 2026-07-29)
Discovered during verification (Tulip: TS `acquired`=32 while zero frames serve the re-framed 160° plan —
the user read that as a contradiction, and at the telescope it makes TS under-schedule the re-shoot).
The rotation-participation predicate now lives ONCE as `FramingCluster.ServesPlanRotation` with three
consumers — the projection's pairing/disagree cue, `WriteBackPlanner`'s disk sum, and
`SingleTargetPlanner`'s cell routing — so the badge and the stamped counts can never tell different
stories. Bulk path: non-serving inventory rows simply don't sum (the grid's badged rows explain; the push
review shows the decrease). Surgical path: a withheld cell emits a `FramingMismatch` note naming frames,
framing, and the rotation they fail — a count that visibly did not move deserves its stated reason.
Alternative — leaving write-back coarse and hand-adjusting `desired` on re-framed targets — rejected:
write-back's purpose is "TS's acquired mirrors disk truth," and disk truth is framing-aware now.

### Rotation edits interact correctly with the key
Target `rotation` is already flyout-editable. After an edit, the next load re-pairs: clusters that
matched may separate and vice versa. This is correct — re-framing the plan means old frames no longer
serve it — but it is the first edit that changes row *identity* rather than a value. No special handling;
noted in docs so the behavior reads as designed, not as a glitch.

## Risks / Trade-offs

- **Single-linkage chaining could bridge two framings via intermediate frames.** → Measured framings are
  ≥ 9° apart with ≤ 0.2° jitter; a bridge would need a dense smear the rotator-snapping acquisition style
  cannot produce. Accepted; fixtures pin the behavior at the boundaries.
- **A one-frame cluster produces a one-frame row (M100).** → Intended — the n=1 stray *is* the
  reference-frame hazard this exists to surface. The badge + count price it; no minimum cluster size.
- **Mech-only units (23% of units) gain rows that can never pair on rotation.** → They pair on the other
  keys exactly as today; their internal framing splits (M94, M106, Iris, Eastern Veil, Crescent) are real
  and were previously invisible. No regression, strictly more information.
- **Grid width: one more column in an already-tight ruler.** → Same single-ruler tuning as
  capture-config; the elastic Target column absorbs it. Accepted.
- **TS-side rotation is target-level, so one bad rotation value separates every bucket of that target.**
  → That is the honest reading (the whole target was re-framed); the `framing` badge makes the cause
  visible, and rotation is flyout-editable to fix authoring errors.

## Migration Plan

None — no persisted state changes hands (fresh in-memory scan each load; `Catalog.db` derived and no app
reads it). Order of work: library first (scanner pre-pass, aggregate/report surface, tests), then app
(pairing predicate, ruler + column, badge, rollups, tests). The library change is inert until the app
reads the new fields, so one commit or two both work with no broken intermediate state.

## Open Questions

- Should framing splits also be listed in the dated Ambiguities report (alongside the `cam≠` candidate
  from `capture-config-keys`)? Safe to decide when touching the report next; the badge filter already
  makes them enumerable in-grid.
- Exact column placement within the capture-config group and the mech display marking (`228°m` vs a
  glyph) — settle at the ruler edit with the grid in front of us.
