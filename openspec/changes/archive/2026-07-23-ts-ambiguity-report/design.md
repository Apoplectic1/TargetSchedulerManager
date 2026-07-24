# ts-ambiguity-report — design

## Context

Every load already computes the detections (`TargetResolver` → `CatalogBuildReport`; `WriteBackStep` →
`WriteBackPlanner.Plan`), and `MainViewModel._lastLoad` retains the `LoadResult` (rows + report + graph). The
resolver rejection (2026-07-08) fixed the workflow: TSM detects, the user repairs by hand in NINA's TS UI.
What's missing is one printable artifact that rolls everything up with fix instructions, plus three TS-internal
checks nothing surfaces today. Constraints: read-only (no TS writes, no AL changes needed — all consumed types
are public: `CatalogGraph`, `CatalogBuildReport`, `WriteBackPlanner`/`WriteBackPlan`, `EffectiveExposure`).

## Goals / Non-Goals

**Goals:**
- One toolbar action → dated Markdown report → auto-open in the default `.md` handler; file persists.
- Every item: **what** (the entity, by name + TS Id where known), **why** (the rule it trips), **fix** (the
  exact NINA TS-UI edit). Actionable at the rig without TSM present.
- Three new checks over the loaded graph: same-key plans per target (all sources, not just Both), planned-only
  twins (same name OR within match tolerance of each other, no disk anchor), duplicate template names per profile.
- Ambiguity count in the status line after each load (tripwire visible without opening the report).

**Non-Goals:**
- No editing/resolution verbs, no adjudication store, no persistence beyond the report file itself.
- No new detection in AL — the three new checks are app-side pure functions (they're TSM's *reporting* concern;
  promote to AL only if a second consumer ever wants them).
- No printing UI — the default editor prints.

## Decisions

1. **Pure builder, own service:** `Services/AmbiguityReport.cs` — static
   `Build(CatalogGraph, CatalogBuildReport, WriteBackPlan, DateTimeOffset, header info) → (string markdown, int itemCount)`.
   Pure over inputs = unit-testable without I/O; the command wrapper does file write + launch.
   *Alternative rejected:* extending `WriteBackStep` (it's the auto-stamp step; report is a different concern).
2. **Re-plan, don't plumb:** the builder calls `WriteBackPlanner.Plan(...)` itself from the retained graph +
   report rather than threading the plan out of `WriteBackStep.Run`. It's pure and cheap (~ms); keeps
   `WriteBackStepResult` untouched. *Alternative rejected:* caching the plan on `LoadResult` — wider surface
   for no measurable gain.
3. **Report sections mirror the fix location, not the detector:** "Rename/target identity" (name-mismatch,
   ambiguous, unanchored, invalid), "Duplicates & twins" (duplicate folds, planned-only twins, identical-name
   alias folds noted as *info* when adjudicated-shaped), "Plans" (same-key multi-plan, held write-back cells
   with per-plan candidates incl. TS plan Ids/desired/acquired), "Templates" (duplicate names), "Notes"
   (UnplannedFrames — informational, no fix required). Each section prints its clean state explicitly
   ("✓ none") so the empty report is affirmative, not blank.
4. **Same-key check spans all TS-sourced targets** (Planned + Both): group `graph.Plans` by
   `(TargetId, template.FilterName upper, purpose via FilterPurposeClassifier on template name,
   EffectiveExposure.Seconds)`; >1 → item. This intentionally overlaps `WriteBackPlan.Manual` for Both targets —
   the report de-dupes by (target, filter, seconds) key, preferring the manual-cell item (it carries disk count
   + per-plan detail).
5. **Planned-only twins:** among `Source == Planned` top-level targets — same normalized name ⇒ twin; else
   pairwise haversine < the load's match tolerance ⇒ near-twin (reported with separation). O(n²) on ~25
   planned-only rows — negligible.
6. **File + launch:** write to `%APPDATA%\TargetSchedulerManager\Reports\ambiguities-yyyyMMdd-HHmm.md`
   (`Log`-style local time), then `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })` —
   the WinUI-desktop-safe way to open the default handler. Failure to launch is non-fatal (report path shown
   in status line; the file exists either way).
7. **Status line:** `MainViewModel` computes the count during `LoadAsync` (builder runs headless there — cheap,
   pure) and appends `· N ambiguities` when N > 0; the toolbar button (`Ambiguities…`, enabled when a load
   exists) writes + opens the file on demand. Count and file always agree because both come from one builder.
8. **TS Ids in fix text:** items carry `ImportedFromTsGuid` (the TS integer Id string) where present —
   *"delete exposure plan Id 1040"* beats *"the second H900 plan"* at the rig.

## Risks / Trade-offs

- [Alias folds are policy, not defects] → identical-name folds and adjudicated aliases print under an *info*
  heading, clearly separated from action items, so the report doesn't nag about M27/Dumbell (or does — until
  the user consolidates it, which is their stated intent; info-not-action keeps the count honest at 0 actions).
- [Duplicate template names: reader may not scope by profile] → group by (ProfileId, Name); with one profile in
  practice this is exact; harmless if a second profile appears.
- [Report drift vs conventions] → the header cites DOMAIN.md's two conventions verbatim, so the printed page
  teaches the rule that was tripped.

## Open Questions

*(none — shapes confirmed with the user 2026-07-08: file + auto-open chosen over dialog; report-only, no edits)*
