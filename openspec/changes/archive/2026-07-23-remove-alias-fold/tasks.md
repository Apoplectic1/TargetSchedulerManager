# Tasks: remove-alias-fold

## 1. Library (repo: ..\Library — commit there, docs same commit)

- [x] 1.1 `TargetResolver`: collapse multi-claim classification to duplicate-always (delete the
      `IsAliasName` gate + the `aliases` list + `IsAliasName` itself + doc mentions)
- [x] 1.2 `CatalogBuildReport`: delete `AliasTsTarget`, `AliasTsTargets` param, `AliasMemberCount` +
      cache, `TargetMatchIssues.Alias` (keep remaining flag values; fix `DuplicateTsTarget` doc text)
- [x] 1.3 `WriteBackPlanner`: delete the alias-exemption branch (ex-alias cells fall to
      `ManualGroup(DuplicateFold)`)
- [x] 1.4 Library tests: alias tests become duplicate/manual expectations (`TargetResolverTests`,
      `CatalogBuildReportTests`, `WriteBackPlannerTests` incl. `Report(...)` builders)
- [x] 1.5 Library `ROADMAP.md`: resolve the "Open: alias-vs-duplicate handling" line; library builds
      green + full Catalog test suite passes; commit in `..\Library`

## 2. TSM app

- [x] 2.1 `ReconciliationLoader`: delete `isAlias`, the `alias` badge, and the `!isAlias` multi-plan
      suppression; drop `aliases=` from the load log line
- [x] 2.2 `AmbiguityReport`: delete alias info-lines + the same-key alias exemption; reword the
      planned-only-twin consolidation instruction (no "intentional alias" escape); fix doc comments
- [x] 2.3 `MainViewModel.GetDiagnosticsContext`: drop `aliases=`; `ReconciliationRow` doc comments drop
      alias mentions
- [x] 2.4 TSM tests: `AmbiguityReportTests` alias tests become action-item expectations; `Report(...)`
      builders lose the alias param (`WriteBackStepTests`, `MainViewModelAmbiguityTests`,
      `MainViewModelTemplateTests`)

## 3. Specs + docs

- [x] 3.1 Amend the active `ts-ambiguity-report` change's delta in place: same-key requirement loses the
      alias exemption + its scenario; the "Adjudicated-shape folds are information" requirement is
      deleted (this change's own delta mirrors the result)
- [x] 3.2 `DOMAIN.md`: badge list drops `alias`; convention becomes "one TS row per position, no
      exceptions"; `ARCHITECTURE.md` drops "aliases /" from the reported list
- [x] 3.3 `ROADMAP.md` shipped entry; `NOTEBOOK.md` correction entry gets a "done" pointer

## 4. Verify + commit

- [x] 4.1 `dotnet test TargetSchedulerManager.slnx` green (slnx only — stale-binary trap); library suite
      green; state the human-verification boundary (grid badges + report rendering on real data need the
      user's next load)
- [x] 4.2 Commit TSM (code + docs + spec amendments in one commit)
