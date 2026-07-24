# ts-ambiguity-report — tasks

## 1. Report builder (pure)

- [x] 1.1 `Services/AmbiguityReport.cs`: static `Build(CatalogGraph, CatalogBuildReport, WriteBackPlan,
      DateTimeOffset generatedAtLocal, string tsDbPath, string libraryRoot) → AmbiguityReportResult(string
      Markdown, int ActionCount)`. Header (generated-at local time, db + library paths, the two DOMAIN.md
      conventions verbatim); sections per design decision 3, each with `✓ none` clean marker; info section for
      alias folds excluded from ActionCount.
- [x] 1.2 Existing-detection items: report issues (NameMismatches with separation + rename-to-token fix;
      AmbiguousMatches with candidate list; DuplicateTsTargets; UnanchoredTsTargets; InvalidTsTargets),
      `WriteBackPlan.Manual` cells (reason, disk count, per-plan TS Id/desired/acquired/accepted, fix text per
      reason), `NeedsReconciliation` notes with UnplannedFrames under the no-action notes section.
- [x] 1.3 New checks (pure helpers in the same file): same-key plans across ALL TS-sourced targets
      (group by TargetId + template FilterName + purpose via `FilterPurposeClassifier` on template name +
      `EffectiveExposure.Seconds`; de-dupe vs Manual items by (target, filter, seconds), preferring the manual
      cell); planned-only twins (same normalized name, else pairwise haversine < tolerance, top-level
      `Source == Planned` only; reuse/lift the resolver's haversine); duplicate template names per
      (ProfileId, Name).

## 2. Command + surfacing

- [x] 2.1 `MainViewModel`: run the builder at the end of a successful `LoadAsync` (re-plan via
      `WriteBackPlanner.Plan` — pure, ms); store `AmbiguityCount` + report inputs; append `· N ambiguities`
      to StatusText when N > 0; expose `CanShowAmbiguities` (load exists).
- [x] 2.2 Report command: write `%APPDATA%\TargetSchedulerManager\Reports\ambiguities-yyyyMMdd-HHmm.md`
      (create dir), launch via `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`;
      launch failure non-fatal → status line shows the path; log the write (`Log.Info`).
- [x] 2.3 `MainWindow` toolbar: `Ambiguities…` button after Pull now (DOMAIN.md add-a-UI-element checklist:
      enabled binding, tooltip, spacing).

## 3. Tests (App.Tests)

- [x] 3.1 Builder tests over synthetic graph/report/plan: each existing-detection kind produces its item +
      section; clean inputs → ActionCount 0 + every `✓ none`; alias-fold info excluded from count.
- [x] 3.2 New-check tests: Swan-shaped same-key pair (Both target, de-duped vs Manual, TS Ids in text);
      same-key on a Planned-only target caught; identical-name planned-only twins caught; near-coordinate
      planned-only pair caught with separation; duplicate template names caught; singletons/distinct-seconds
      not flagged.
- [x] 3.3 VM test: successful load sets `AmbiguityCount` and StatusText carries `ambiguities` only when > 0.

## 4. Verify + docs

- [x] 4.1 Full build + all tests green per VERIFICATION.md.
- [x] 4.2 Run against real data (user cue permitting): report lists exactly the known items (FishHead 6-cell
      identity block as one target item + held cells; Swan Id 299/1040 pair; M27/Dumbell as info) — state
      what needs the user's visual/print check. DONE 2026-07-23: first real run (07-08) listed exactly the
      known items and caught a fresh one (Rosette P4 / `Panel Center`); after the user's BIRDWATCHER
      hand-fix pass (FishHead rename · Swan stray plan · Dumbell consolidation) the re-run is fully clean —
      dups=0, zero held cells, report shows 0 action items (tsm.log 2026-07-23 21:07). Note: M27/Dumbell
      finished as a flagged duplicate rather than "info" — the alias premise died mid-change
      (`remove-alias-fold` amended this change's delta in place).
- [x] 4.3 Docs in the same commit: DOMAIN.md chrome list + checklist pass for the new button; ROADMAP
      recently-shipped + Status next-step update; ARCHITECTURE.md one-liner under the write-back section
      (report = the tripwire surface).

## 5. Real-data feedback round (2026-07-08 evening run)

- [x] 5.1 Rig vocabulary, no raw ids: target guids dropped everywhere; plans render as template name +
      desired/acq/acc (how the TS UI tells same-key plans apart); targets prefix their project
      (`project › target`); twins discriminate by project.
- [x] 5.2 Alias-fold exemption in the same-key check (mirrors the planner: plans == members → explained) —
      fixes the 6-item M27/Dumbell flood; regression test.
- [x] 5.3 Panel-path name-mismatch describes the token disagreement instead of prescribing a bogus
      catalog-token rename (Rosette P4 → "Rename to `Mosaic`"); regression test.
- [x] 5.4 Unplanned-frames info compresses to one line per target (141 info lines on real data).
- [x] 5.5 Spec updated to the corrected contract; 176 tests green.
- [x] 5.6 TS-UI-shaped layout: target headline + one indented row per plan/bucket (held cells, same-key
      items, unplanned-frames info) instead of joined single lines.
