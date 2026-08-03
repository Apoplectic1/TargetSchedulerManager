# Tasks: assign-template-adoption

## 1. Planner reshape (Services/AdoptionPlanner.cs)

- [x] 1.1 Add `ListCandidates(row, ts, profileId)` — strict scope (same filter, same square bin), per-candidate `WouldPair` (purpose + expressed-and-equal gain/offset; `-1` sentinel never pairs), preselect index (pair → same purpose → order); non-square-bin cells yield an empty scope
- [x] 1.2 Reshape `Build` to take the chosen template (no matching, no `pending`, no `desiredOverride`); keep target payload (centroid, RA hours, sky-rotation seed) and plan payload (born-complete counts, exposure sentinel-vs-explicit) unchanged
- [x] 1.3 Delete `MatchTemplate`, `BuildCreateOffer`, `AdoptionHold`, `TemplateCreateOffer`, `PendingTemplate`, `TemplateFormResult`

## 2. VM funnel (ViewModels/MainViewModel.Edits.cs, .Sync.cs)

- [x] 2.1 Replace the three hooks (`AdoptHoldPrompt`, `AdoptTemplateFormPrompt`, target/project dialog hook) with one `AdoptPrompt: Func<AdoptionFacts, Task<AdoptionChoice?>>`; define `AdoptionFacts` (target name/coords, locked-project xor project list, candidates + preselect, disk cell values) and `AdoptionChoice` (project guid, template id)
- [x] 2.2 Rewrite `AdoptRowAsync`: busy refusal → facts → prompt → null = cancel (log info) → `Build` → `ApplyInsertAsync` → status → `LoadAsync(PullPolicy.Never)`; delete `BuildViaTemplateFormAsync`

## 3. Dialog (MainWindow.Dialogs.cs, .Flyouts.cs)

- [x] 3.1 Implement `ShowAdoptDialogAsync(facts, anchor)`: project ComboBox (disabled+preselected when locked), template ComboBox with capture-value display, disk-vs-template facts panel refreshed on selection, non-pairing caution TextBlock, empty-scope message with Accept disabled; movable + seeded near the row via `ShowDialogAsync`
- [x] 3.2 Delete `ShowAdoptTemplateFormAsync`, `ShowAdoptTargetDialogAsync`, `ShowAdoptHoldDialogAsync`; rewire the context-menu path and window hook to `AdoptPrompt`
- [x] 3.3 Build clean (0 errors)
- [x] 3.4 All dialogs open centered — anchor seeding retired (user call 2026-08-03 after obs 3eba: seeding the ContentDialog overlay races layout and can land the box off-screen — an invisible modal reading as a UI hang); `ShowDialogAsync` loses its anchor parameter, `ShowEditDialogAsync`/`ShowMosaicDialogAsync` lose theirs, flyouts spec requirement renamed/modified (delta added)
- [x] 3.5 Cell-keying edits re-reconcile on editor close (obs 4798, option 1): `IsPairingKey` (plan exposure; template gain/offset/bin/defaultexposure/filtername/name; target rotation) tracked per editor session, close triggers `LoadAsync(PullPolicy.Never)`; flyouts mirror requirement MODIFIED (delta)

## 4. Tests

- [x] 4.1 Reshape `AdoptionPlannerTests`: strict-scope listing (bin exclusion, non-square empty), preselect ranking, `WouldPair` matrix (sentinel, purpose, gain/offset), `Build`-with-chosen-template payloads (locked-project target reuse, exposure sentinel, born-complete)
- [x] 4.2 Reshape adoption-path tests in `TsInsertSyncTests`/VM tests to the single-hook flow; drop creation-form paths (generic template-insert replay tests in the library and `TsSync` stay untouched)
- [x] 4.3 Full test run green (app + Catalog suites)

## 5. Docs + specs (same commit as code)

- [x] 5.1 Update SUBSYSTEMS.md (adoption bullet → assignment dialog), DOMAIN.md (dialog inventory), ROADMAP.md status, CHANGELOG.md entry
- [ ] 5.2 At archive/sync time: apply the delta to `openspec/specs/disk-row-adoption/spec.md` and edit its Purpose line directly ("rendering `Both` when the assigned template pairs")
