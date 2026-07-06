# Tasks: cadence-safe-ts-edits

Two-repo change: groups 1–3 edit `..\Library` (session needs `--add-dir ..\Library`; library commits are
separate from TSM commits, each with its own doc updates in the same commit as the code).

## 1. Library — schema metadata (`TsEditableSchema.cs`)

- [ ] 1.1 Add `TsCadenceClear` enum (`None`, `Target`, `Project`) with consumer-neutral doc comments pinning the TS source references (`ToggleExposurePlan`, `SaveProject`, `FilterCadenceFactory.Generate`)
- [ ] 1.2 Replace `TsField.CadenceSafe : bool` with `Clears : TsCadenceClear = None` (breaking, no shim); set `exposureplan.enabled` → `Target`, `project.filterswitchfrequency` → `Project`; update both fields' `Notes`
- [ ] 1.3 Re-express `IsCadenceBreaking` as `Clears != None`; fix all library-side references/doc comments that mention `CadenceSafe`
- [ ] 1.4 Update `TsEditableSchemaTests` for the new shape (scope values per field; `IsCadenceBreaking` equivalence)

## 2. Library — editor behavior (`TargetSchedulerEditor.cs`)

- [ ] 2.1 Add unchanged-value fast path to `UpdateField`: normalized old == new → return success (found, verified) with no UPDATE
- [ ] 2.2 Implement transactional clear in `UpdateField` when `Clears != None` and value changed: UPDATE + scoped `DELETE FROM filtercadenceitem` (scope `Target`: resolve the plan's `TargetId`; scope `Project`: `targetid IN (SELECT Id FROM target WHERE projectid = …)`) in one transaction, read-back verify unchanged
- [ ] 2.3 Add `RefusalReason.HasOverrideOrder`; in `TrySetField`, refuse scope-`Target` edits when the target has `overrideexposureorderitem` rows (guard order preserved, new check last); scope-`Project` unaffected
- [ ] 2.4 Editor tests: plan-disable clears only its target's cadence rows; fsf change clears all project targets' rows and spares other projects; unchanged value is a no-op (rows survive); OEO refusal (target scope) vs OEO pass-through (project scope); failed commit leaves both update and rows intact
- [ ] 2.5 Build library + run `Astronomy.Catalog.Tests`; update Library `CLAUDE.md`/`ROADMAP.md` in the same commit

## 3. TSM — gate and app plumbing

- [ ] 3.1 Rebuild TSM against the changed library; fix the `CadenceSafe` → `Clears` fallout (`TsEditGate`, any references); map the new refusal in `EditOutcome` handling
- [ ] 3.2 Extend `TsEditGate` seam tests to cover the `HasOverrideOrder` refusal outcome

## 4. TSM — per-filter enabled UI

- [ ] 4.1 Surface the plan `enabled` state + TS plan key on filter rows (`ReconciliationRow`/loader); no checkbox for disk-only rows
- [ ] 4.2 Add the checkbox to the filter-row template (target-`active` checkbox pattern; DOMAIN.md "add a UI element" checklist)
- [ ] 4.3 Implement confirm-first flow: `ContentDialog` driven by `TsEditableSchema.IsCadenceBreaking` (cadence-reset wording; LIVE adds the actively-imaging warning); cancel reverts with no write
- [ ] 4.4 Route confirmed toggles through `TsEditGate.ApplyAsync`; in-place row update on success; revert + surfaced message on refusal/failure (OEO wording points at the TS editor)
- [ ] 4.5 Build + unit tests; visual/behavioral verification is the user's (checkbox states, dialog wording, cancel/confirm paths, scroll preservation)

## 5. Verify live + docs

- [ ] 5.1 User-run end-to-end check against a LOCAL copy first, then LIVE: disable/enable a filter, confirm `filtercadenceitem` rows cleared and TS regenerates on next planning pass
- [ ] 5.2 Update TSM `ARCHITECTURE.md` (cadence invariant + accepted runtime-sync constraint), `ROADMAP.md` (per-filter enabled shipped), `CLAUDE.md` router if needed — same commit as the TSM code
