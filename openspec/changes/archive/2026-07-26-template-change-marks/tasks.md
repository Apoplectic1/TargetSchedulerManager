# template-change-marks — Tasks

## 1. SyncMarks template resolution (outbound + inbound onto rows)

- [x] 1.1 `SyncMarks.Build` takes the retained `CatalogGraph?` (replacing the plans param); derive
      planKey→templateKey, templateKey→name, targetId→distinct-templateKeys, targetKey→name, and
      projectKey→name maps (+ the existing target→plan-key map)
- [x] 1.2 `ForPlan`: union the plan's template entries (both directions) into glyph + attributed tooltip
      lines (`→ unpushed — template '<name>': <col> <old> → <new>`; name falls back to raw key)
- [x] 1.3 `ForKeys`: count each pending (template, field) once per header via the distinct-template union
      (graph map + row-carried plan keys → template keys)
- [x] 1.4 `ForKeys` tooltips (D7): attributed old→new lines for own-scope target/project fields; counts
      only for rolled-up plan/template fields
- [x] 1.5 `ForTemplate(tsKey)`: single-template (glyph, tooltip) for the picker
- [x] 1.6 `RefreshAllMarks` passes `_lastLoad?.Graph`

## 2. Inbound pull diff covers exposuretemplate

- [x] 2.1 `TsInboundDiff.FieldSet` += `(TsTable.ExposureTemplate, "Id", <derived from
      TsEditableSchema.For(ExposureTemplate)>)` — derived column list, no literal copy

## 3. Templates… picker marks

- [x] 3.1 VM surface for per-template marks (fresh `SyncMarks` build at picker open)
- [x] 3.2 `Templates_Click` items prefix the glyph and attach the old→new tooltip when marked

## 4. Tests

- [x] 4.1 SyncMarks: template outbound lights all users' rows; zero-use template lights none; plan+template
      union → `⇄`; attribution line grammar; header counts a shared template field once; folded-plan
      template rolls up; `ForTemplate` glyph/tooltip; header own-scope attribution (project/target lines,
      name fallback to raw key) with rolled-up counts beside them
- [x] 4.2 Inbound diff: exposuretemplate field change recorded old→new keyed by Id; untouched template
      records nothing; absent column (schema drift) skipped
- [x] 4.3 Marks sweep integration: `RefreshAllMarks` with a template journal entry marks rows + header in
      place

## 5. Docs + verification

- [x] 5.1 Update `ARCHITECTURE.md` (marks resolution mentions template key space) and `CHANGELOG.md`;
      spec deltas synced at archive time
- [x] 5.2 Build + full test run green; user verifies arrows visually (template edit → row arrows +
      picker mark) — behavior-changing, so archive waits for the user's verify word
