# NOTEBOOK.md — TargetSchedulerManager

**Charter:** running lab notebook — short, dated empirical findings from *doing the work*
(runtime/WinUI/grid behaviors observed, measurements, surprises) too small for a standalone
`docs/` record. Newest on top. Read it for "did we already learn X by doing it." **Split:** a
small finding-from-doing-the-work → here; a substantial standalone record (decision / review /
design) → `docs/YYYY-MM-DD-<slug>.md`.

**2026-07-29 — the measured framing landscape (rotation spike, 18,650 frames; fed `rotation-framing-key`).**
Production-faithful header dump + circular-clustering sweep over the live library, before designing rotation
as a key. What it settled: **(1)** the tolerance question dissolved — genuinely distinct framings sit ≥ 9°
apart fold-180 (nearest pairs: Eastern Veil 8.9° mech, Barnard 202 10° sky) while within-framing jitter is
≤ 0.2° (NINA snaps the rotator; raw values are literally 50.0/60.0/15.0), so tolerances 1–5° give identical
clusters; the earlier "5° puts M33/M51 exactly on the boundary" note did not reproduce under fold-180
(measured 0.56°/0.10°) — the only boundary resident in the whole library is Sh2-174 (TS 90° vs disk 94.8°,
inside the 5° tolerance → pairs). **(2)** Every true flip pair's centroids coincide ≤ 0.12° (M81 0.032°,
Bear Claw 0.028°, Wizard 0.116°) — fold-180 + centroid guard is correct AND optically right (a rectangle
rotated 180° about its center covers the identical footprint, so flips never cost integration overlap).
**(3)** Mech→sky conversion is dead: the zero point is mod-180 stable in 25/30 (unit, camera) groups but
drifts 19–35° across remounts in exactly the units that matter (Abell 21/78, IC 443). **(4)** The real
re-framing census: 6 sky-comparable multi-framing targets (Barnard 202 — only 28/479 frames at the planned
50°; Tulip — the *majority* 199/285 is the old 20° framing; M100 — ONE stray 135° frame among 104, the
reference-frame-hazard shape; M81 — four framings; Pacman; IC 443 — TS plans a *third* framing neither
cluster matches), plus ~5 mech-only (M94, M106, Iris, Eastern Veil, Crescent) and one pure translation
stray (M97 — same 125° angle, one frame 1.45° off-center; only the centroid catches it — the fact that
welded RA/DEC to rotation as ONE framing concept). "14 targets with different framings" from the
capture-config pass was inflated by counting flips. Spike artifacts were scratchpad-only (read-only against
library + local TS db).

**2026-07-24 — one-off test flake, name not captured.** During the presentation-conventions pass, one
run reported 1/230 failed with the name scrolled out of the tail; four immediate re-runs were fully
green. Suspects if it recurs: the timing-gated tests (`CommitChainTests`' 50 ms no-overlap window, the
busy-gate blocking-editor waits) under build-time disk load. If seen again: capture the test name
(`dotnet test` without `-v:q`) before re-running.

**2026-07-23 — alias removal DONE (`remove-alias-fold`; lib `306f6fd`).** The removal described in the
2026-07-08 correction below shipped ahead of the BIRDWATCHER pass (user's call) — so until the Dumbell
consolidation, the M27/Dumbell twin surfaces every load as a duplicate badge + held write-back cell + report
action items. That's the intended surface-for-decision behavior, not a regression.
**Closed 2026-07-23** — the Dumbell consolidation landed by hand in that same BIRDWATCHER pass (Swan · Rosette ·
Dumbell; CHANGELOG ▶ DECIDED 2026-07-24), so the tray and the ambiguity report are now zero and no duplicate
badge is live. The doctrine it illustrates stands: `ARCHITECTURE.md` (one TS row per position) + `DOMAIN.md`
(no alias escape).

**2026-07-08 (late) — CORRECTION: the M27/Dumbell twin was never intentional; alias mechanism to be removed.**
The morning entry below recorded "user: leave it" — that was tolerance of a state the alias fold presented
as benign, not approval of the twin. User (same night, after the ambiguity report surfaced it as *"If
unintended, consolidate"*): **"this was not intentional and should be brought to my attention."** Standing
lesson: **explained ≠ approved** — a structurally weird state must surface for the user's decision even when
the numbers reconcile. *(Graduated 2026-07-26 to `DOMAIN.md` → TS authoring conventions as a forward
constraint on any ambiguity-resolving mechanism; this entry keeps the incident that produced it.)*
Consequences: (1) Dumbell consolidation joins tomorrow's BIRDWATCHER hand-edit list
(delete the disabled `Dumbell` target in "Nebulae - Above 45"; M27 keeps desired 169; counts restamp next
load); (2) the **alias fold mechanism gets removed in full** (agreed in explore, 2026-07-08 late): it exists
to let a "benign" multi-claim auto-resolve unflagged, the hand-edit doctrine abolishes that category, and it
demonstrably masked this defect for weeks (strict-equality naming also folds *identical accidental twins*
silently). After consolidation the machinery covers zero rows → removal is pure dead-code deletion: resolver
alias/duplicate classification collapses to duplicate-always-flagged, `AliasTsTarget`/`Alias`
flag/`AliasMemberCount`/`IsAliasName` + planner alias branch die (lib), grid `alias` badge + `!isAlias`
multi-plan suppression + report info-lines/exemption die (TSM), DOMAIN's convention drops its alias escape
clause (one TS row per position, NO exceptions). Single-target naming freedom is unaffected (a lone TS
`Dumbell` still matches dir `M27 - Dumbell` via ordinary validation — `IsAliasName`'s only call site is the
multi-claim fold). Sequence: BIRDWATCHER pass → clean re-run → tick 4.2 → archive `ts-ambiguity-report` →
paired lib+app `remove-alias-fold` change (its delta also MODIFIES/REMOVES the ts-ambiguity-report spec's
"adjudicated folds are information" requirement — premise dead).

**2026-07-08 — "Desired ≠ NINA" on M27 - Dumbell is the alias fold, not drift (user: leave it — SUPERSEDED, see correction above).**
Visual-pass observation (USER_OBS f818): TSM showed H/O/S Desired 298 where NINA's editor shows 169.
Both are right — the row folds the catalog's one alias pair (`aliases=1` every load): TS target **M27**
(Galaxies, enabled, desired 169×3 + Stars 33) + TS target **Dumbell** (Nebulae - Above 45, **disabled**,
desired 129×3 + Stars 32) sum to 298/65; the TS column's 16 = 8+8 likewise. NINA's editor shows one
target at a time, so the two views can never agree on a folded row. The `×2` Plans cell + amber `alias`
badge mark exactly this. Note the fold sums a *disabled* twin's goals into Desired/Hours — user decided
2026-07-08: **leave as-is** (the badge explains it; retiring the Dumbell twin in NINA remains the
no-code cleanup if it ever grates).

**2026-07-07 — exposure 0 is literal; TSM's two `> 0` filters were the leftovers.** The Library
adjudicated exposure-0 against the TS source (`d26b75e` in `..\Library`: the planner defers only on
`!= -1`, so 0 = a literal zero-second exposure). TSM had two filters still encoding "non-positive =
unknown" — `TsEditGate.ReadPlanEffectiveSecondsAsync` (`value > 0`) and the flyout exposure commit
(`v > 0`) — whose combined effect: committing 0 left the Seconds cell stale until reload (pre-`d26b75e`
it mirrored the *template default*, actively wrong vs the reload). Both flipped to `>= 0`
(openspec `exposure-zero-literal`). Deeper conflation found and deliberately left: `ReconciliationRow`
uses `PlanSeconds == 0` as its own no-seconds marker, so the TS-only plane renders 0 as "—" — mirror
and reload agree on every plane (the real invariant); only plan+disk rows literally display "0".

_Substantial findings live as dated `docs/` records; spent ones (the 2026-06-10 code reviews, the
2026-06-26 guarded-TS-write plan, the 2026-07-24 app review + re-check) moved to `docs/archive/` on
2026-07-26, each with a status banner. The WinUI landmines are captured in `DOMAIN.md` § "WinUI gotchas"._
