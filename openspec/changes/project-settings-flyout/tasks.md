# Tasks: project-settings-flyout

Small TSM-only change on fully generic plumbing. Order: trigger → warn → verify. Build stays green per group.

## 1. Trigger

- [ ] 1.1 `Row_RightTapped`: add "Edit project…" gated by the row shape's project key (`TargetGroupRow.ProjectTsKey`, `PanelGroupRow.Children[0].ProjectTsKey`, `ReconciliationRow.ProjectTsKey`); mosaic parent keeps "Edit mosaic project…" as its item; flyout via `ShowEditFlyoutAsync(TsTable.Project, key, "{Project} — project", null, null)`

## 2. Cross-field warn

- [ ] 2.1 Warn evaluation on `minimumtime`/`meridianwindow` commits: pair from seed + in-flyout committed values; invalid ⇒ persistent caution line in the flyout ("Min time > 2 × Meridian window — TS will never select this project") + status note; clears when a commit makes the pair valid; the write always proceeds
- [ ] 2.2 Unit test the pair rule (pure predicate) + a gate seam test that a project-field commit journals with the project key (`TsTable.Project`, kind Manual)

## 3. Verify + docs

- [ ] 3.1 Build + full App.Tests; user-run pass: right-click each row shape (group / panel / plan / disk-only-negative), edit a knob per type (enum, real, bool, whole), state Active↔Inactive plain-write check in the journal/push review, the warn appear/clear pair, push → verify in NINA's TS Database Manager
- [ ] 3.2 Docs same-commit: DOMAIN.md (Editing: project trigger is right-click-only; the warn convention), ARCHITECTURE.md (retire the stale state-stamping caution if referenced), ROADMAP.md (digest + Status Parts queue), memory note on the retired gotcha
