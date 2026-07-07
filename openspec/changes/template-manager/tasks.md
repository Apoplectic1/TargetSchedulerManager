# Tasks: template-manager

Two repos: library schema rows first (its own commit in `..\Library`), then the TSM surface. Build green per group.

## 1. Library (`..\Library`, separate commit)

- [ ] 1.1 `TsEditableSchema`: 11 exposuretemplate rows per design D1 (bounds/units/notes; all cadence-safe; UI order twilight → moon suite → dither → humidity) + `TwilightLevel` enum map + `EnumValues` entry
- [ ] 1.2 `TsEditableSchemaTests`: new rows present/typed/bounded, enum map codes/labels, exposuretemplate count pins; full library suite green

## 2. TSM surface

- [ ] 2.1 VM: `ListTemplates()` + `TryGetTemplateForPlan(planTsKey)` over the retained graph (record: TsKey · Name · Filter · UsedByPlans; no-load → empty + status note path) + internal graph seam for tests
- [ ] 2.2 Toolbar "Templates…" button + picker flyout (name · filter · used-by, natural order; click → editor flyout titled "Template '<name>' — used by N plan(s)")
- [ ] 2.3 Row menu: "Edit template…" on plan rows when the template resolves
- [ ] 2.4 Tests: template list + plan→template resolution over an injected graph (write-back test builders); journal seam for a template field commit (ExposureTemplate table + label)

## 3. Verify + docs

- [ ] 3.1 Build + full App.Tests + library tests; user-run pass: picker list sanity (counts, zero-use template), row item, moon-suite + twilight edits, sentinel checkboxes still right on gain/offset, push → verify in NINA's TS editor, blast-radius title in push review
- [ ] 3.2 Docs same-commit: DOMAIN (Templates… picker + blast-radius convention + filtername re-keying caution), ROADMAP (digest + Parts queue), library-side doc note per its conventions; memory update
