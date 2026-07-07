# NOTEBOOK.md — TargetSchedulerManager

**Charter:** running lab notebook — short, dated empirical findings from *doing the work*
(runtime/WinUI/grid behaviors observed, measurements, surprises) too small for a standalone
`docs/` record. Newest on top. Read it for "did we already learn X by doing it." **Split:** a
small finding-from-doing-the-work → here; a substantial standalone record (decision / review /
design) → `docs/YYYY-MM-DD-<slug>.md`.

**2026-07-07 — exposure 0 is literal; TSM's two `> 0` filters were the leftovers.** The Library
adjudicated exposure-0 against the TS source (`d26b75e` in `..\Library`: the planner defers only on
`!= -1`, so 0 = a literal zero-second exposure). TSM had two filters still encoding "non-positive =
unknown" — `TsEditGate.ReadPlanEffectiveSecondsAsync` (`value > 0`) and the flyout exposure commit
(`v > 0`) — whose combined effect: committing 0 left the Seconds cell stale until reload (pre-`d26b75e`
it mirrored the *template default*, actively wrong vs the reload). Both flipped to `>= 0`
(openspec `exposure-zero-literal`). Deeper conflation found and deliberately left: `ReconciliationRow`
uses `PlanSeconds == 0` as its own no-seconds marker, so the TS-only plane renders 0 as "—" — mirror
and reload agree on every plane (the real invariant); only plan+disk rows literally display "0".

_Substantial findings to date live as dated `docs/` records (the 2026-06-10 code reviews, the
2026-06-26 guarded-TS-write plan) and the WinUI landmines are captured in `DOMAIN.md` § "WinUI
gotchas"._
