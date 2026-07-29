# Tasks: rotation-framing-key

## 1. Library — scanner framing pre-pass (`..\Library\Astronomy.Catalog`)

- [x] 1.1 Extend the scanner's frame reading with sky angle, mechanical angle, and per-frame RA/Dec
      (all already exposed by `XisfHeader`; carried nullable, never defaulted)
- [x] 1.2 Implement the framing pre-pass per unit: partition by expression (sky / mech / unknown),
      single-linkage fold-180 gap clustering at 5°, then single-linkage spatial clustering at 0.5°
      haversine; unknown frames join a sole existing cluster or form their own (design → Cluster identity)
- [x] 1.3 Add the framing cluster to the aggregate grouping tuple and surface framing on the aggregate:
      expression, fold-180 circular-mean angle, cluster centroid (consumer-neutral naming; the raw angle
      set was dropped — the fold mean + expression carry everything a consumer reads)
- [x] 1.4 Publish per-cluster centroids on the unit report beside the existing consensus centroid
      (`TargetReport.Framings`)
- [x] 1.5 Scanner unit tests on synthetic fixtures: two-rotation split; 0°/180° flip merges (coincident
      centroids); 180°-apart distinct centers split; translated stray at unchanged rotation splits;
      n=1 stray forms its own cluster; mech-only unit clusters internally; neither-angle frame reported
      unknown; uniform unit unchanged (14 clusterer tests + 6 projection pairing tests)

## 2. App — pairing + grid

- [x] 2.1 Extend the pairing predicate: rotation term participates only when the disk bucket expresses
      sky rotation AND the anchored TS target carries a rotation; fold-180 delta ≤ 5° pairs (target-level
      comparand applied to every plan bucket). NOTE: landed in the library's `ReconciliationProjection`
      (where the capture-config pairing already lives), not app-side — design.md amended to match
- [x] 2.2 Add the `Rot` column to the capture-config group in the ruler; disk rows show cluster fold-180
      mean (mech visibly marked `°m`, unknown em dash), TS rows show target rotation fold-180; excluded
      from sort like the rest of the group
- [x] 2.3 Rollups: reuse the value-or-`mixed` pill mechanics for framing
- [x] 2.4 Add the warning-severity row-scoped `framing` badge on disk rows failing the rotation pairing
      term (`cam≠` mechanics); wired into `Badges.IsWarning` so the badge filter and flag color agree
- [x] 2.5 App pairing/rendering tests: plan-matching minority cluster pairs while larger cluster
      separates (Barnard 202 shape); flipped plan pairs (Bear Claw shape); plan without rotation pairs on
      remaining keys; mech-only cluster never fails pairing on rotation; badge presence/absence

## 3. Verify against the live library

- [x] 3.1 Build + full test pass (library then app): Catalog 219, Contracts 61, App 311 — all green
- [x] 3.2 Run the app and confirm the known cases: Barnard 202 → `Both` @50° (28 frames) + Disk @60°
      (451, badged); Sh2-101 Tulip → `Both` @160° + Disk @20° (199, badged); M100 → n=1 Disk row @135°
      badged; M81 → four framing rows, 65° pairing; Bear Claw/Wizard/M81 flips do NOT split; IC 443 both
      clusters badged (TS plans a third framing); M97 translation stray splits.
      **Headless pipeline run against the live library confirms all of these at the cell level**
      (M97's stray splits as its own n=1 cell; Barnard/Wizard non-pairing is pre-existing gain/seconds
      separation from capture-config, with the framing split correct inside each config bucket) —
      the in-app visual pass is the user's call, per the run-on-request rule
- [x] 3.3 Confirm target-level Hours/Remaining figures unchanged by the re-keying (structural: cell
      splits preserve Disk×seconds sums; `RowAggregates` sums components; Bear Claw 65.6/65.6 exact)
- [x] 3.4 Confirm mech-only targets (M94, M106, Leo Triplet) show internal framing clusters, pair on
      remaining keys, and carry no `framing` badge (headless run; Iris/Eastern Veil/Crescent same shape)

## 4. Docs (same commit as the code, per repo rule)

- [x] 4.1 Update `ARCHITECTURE.md` key facts (cell key + new framing invariant). `CONVENTIONS.md`
      unchanged — its one-plausible-home map already routes scanning/reconciliation to the library;
      `CLAUDE.md` mirror unchanged per the capture-config precedent (the condensed list omits this tier)
- [x] 4.2 Update `DOMAIN.md`: framing in the capture-config column block + `Rot` cell semantics; the
      rotation-edit-re-keys-rows behavior noted as designed; the WBPP boundary (TSM detects, WBPP
      enforces, XFM neither) as a standing truth
- [x] 4.3 `ROADMAP.md`: rotation + RA/DEC deferral closed as one unit (telescope remains deferred);
      overlap-% column recorded as the explicit follow-up
- [x] 4.4 Standing truths (shift-left rule): flip-fold geometry + mech-conversion rejection →
      `DOMAIN.md` "What TSM is for"; the measured framing landscape → `NOTEBOOK.md` (2026-07-29 entry)

## 5. Write-back credits only serving frames (in-flight addition, user decision 2026-07-29)

- [x] 5.1 `FramingCluster.ServesPlanRotation` — the shared rotation-participation rule (pairing cue,
      bulk crediting, surgical routing all consume it)
- [x] 5.2 `WriteBackPlanner`: non-serving inventory rows excluded from the disk sum (re-framed plans stamp
      their true progress, possibly 0); `SingleTargetPlanner`: non-serving cells withheld with a
      `FramingMismatch` note
- [x] 5.3 `ReconciliationProjection.FramingDisagrees` refactored onto the shared rule (same semantics)
- [x] 5.4 Tests: Barnard credit shape (only the 50° cluster sums; all-non-serving stamps 0), mechanical +
      flip still credit, surgical withhold + note (Catalog 222 green)
- [x] 5.5 `write-back` delta spec + proposal/design updated; ARCHITECTURE + DOMAIN refreshed for the
      serving rule
