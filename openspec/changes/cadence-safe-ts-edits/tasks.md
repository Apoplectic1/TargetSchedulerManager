# Tasks: cadence-safe-ts-edits

Two-repo change (revised 2026-07-06 for the sync model + fsf UI scope): groups 1–2 edit `..\Library`
(separate commits, docs in-commit). Build green per group.

## 1. Library — schema metadata (`TsEditableSchema.cs`)

- [x] 1.1 Add `TsCadenceClear` enum (`None`, `Target`, `Project`) with consumer-neutral doc comments pinning the TS source references (`ToggleExposurePlan`, `SaveProject`, `FilterCadenceFactory.Generate`)
- [x] 1.2 Replace `TsField.CadenceSafe : bool` with `Clears : TsCadenceClear = None` (breaking, no shim); set `exposureplan.enabled` → `Target`, `project.filterswitchfrequency` → `Project`; update both fields' `Notes`; `IsCadenceBreaking` ≡ `Clears != None`
- [x] 1.3 Update `TsEditableSchemaTests` for the new shape (scope values per field; `IsCadenceBreaking` equivalence; the all-cadence-safe template pin becomes all-`None`)

## 2. Library — editor behavior (`TargetSchedulerEditor.cs`)

- [x] 2.1 Unchanged-value fast path in `UpdateField`: normalized old == new → success (found, verified), no UPDATE, no clear
- [x] 2.2 Transactional clear when `Clears != None` and the value changed: UPDATE + scoped `DELETE FROM filtercadenceitem` (scope `Target`: the plan's target id, one SELECT; scope `Project`: `targetid IN (SELECT Id FROM target WHERE projectid = …)`) in one transaction; read-back verify unchanged; exact TS table/column names verified against the TS source EF configs
- [x] 2.3 `RefusalReason.HasOverrideOrder`: refuse scope-`Target` edits when the target has `overrideexposureorderitem` rows (guard order preserved, new check last); scope-`Project` unaffected
- [x] 2.4 Editor tests over real temp dbs: plan-disable clears only its target's rows; fsf change clears all project targets' rows and spares other projects; unchanged value no-op (rows survive); OEO refusal (target scope) vs pass-through (project scope); failed commit leaves both intact
- [x] 2.5 Build + full `Astronomy.Catalog.Tests`; Library ROADMAP digest in the same commit

## 3. TSM — gate, replay, plumbing

- [x] 3.1 Rebuild against the changed library; fix `CadenceSafe` → `Clears` fallout; map `HasOverrideOrder` in `RefusalText` (wording points at the TS editor)
- [x] 3.2 Seam tests: gate passes the new refusal through (no journal entry); push replay routes a journaled cadence-breaking field through the same `TrySetField` (stub asserts the call; composition is the library's)

## 4. TSM — per-filter enabled UI + flyout inclusion

- [x] 4.1 Surface plan `enabled` on filter rows end-to-end (reader SELECTs it if absent → `TsPlanData` → resolver → projection cell → `ReconciliationRow`); no checkbox for disk-only rows
- [x] 4.2 Checkbox in the filter-row template (target-`active` pattern; DOMAIN checklist), direct commit (confirm removed 2026-07-07, user decision), in-place update on success, refusal reverts + surfaced
- [x] 4.3 `TsFieldsEditor` stops skipping cadence-breaking fields; they commit directly (confirm removed 2026-07-07; was: scope-aware wording — fsf names the whole-project fan-out); project flyout thereby ships fsf, plan flyout ships enabled (checkbox setter shared so the grid mirrors)
- [x] 4.4 Tests: confirm-gating predicate paths; enabled flows into rows (BuildRows); flyout inclusion (cadence field rendered)

## 5. Verify + docs

- [ ] 5.1 Build + both suites ✅ (170 lib + 131 app); user-run pass PENDING: toggle a filter locally → local `filtercadenceitem` cleared + journal entry → push → BIRDWATCHER cleared + TS regenerates on next pass; fsf via project flyout with fan-out confirm; OEO refusal if any target has one; cancel paths
- [x] 5.2 Docs same-commit: TSM ARCHITECTURE (cadence invariant + replay composition), DOMAIN (confirm-first convention + checkbox), ROADMAP (digest + Parts queue), memory update
