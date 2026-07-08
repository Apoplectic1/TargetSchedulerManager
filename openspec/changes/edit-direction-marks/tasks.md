# Tasks — edit-direction-marks

## 1. Inbound differ in TsSync (D1–D3)

- [x] 1.1 Author the diffable field set (design D2) in a new `Shared\TsInboundDiff.cs`: per-table column
      lists (`exposureplan`: desired/acquired/accepted/exposure/exposureTemplateId/enabled; `target`:
      active/priority/rotation/name + ra/dec column names verified against `TargetSchedulerReader`;
      `project`: the `TsEditableSchema` project set) + a snapshot reader (one SELECT per table over an
      existing local db → `(table, key) → column → value` display strings; keys: plan Id / target guid /
      project Id, case-insensitive).
- [x] 1.2 Session inbound store on `TsSync`: union-in per pull (latest observation wins per field),
      row-added entries ("new from BIRDWATCHER"), deletions ignored, columns missing from either snapshot
      skipped; exposed read-only for the marks resolver.
- [x] 1.3 Hook `TsSync.Pull`: snapshot before backup (skip when no local file), snapshot after, diff,
      union. Verify all four pull paths flow through (open / force / discard / closing pull).
- [x] 1.4 Mask in `TsSync.RecordWriteBack`: remove that plan key's `acquired`/`accepted` inbound entries
      (never `desired`).
- [x] 1.5 Tests (temp SQLite dbs, spec scenarios): field change diffs with old/new; first-pull-no-local
      records nothing; identical snapshot records nothing; union across two pulls; latest-wins per field;
      added-row entry; deleted-row silence; missing-column skip; write-back mask (acquired/accepted
      removed, desired kept); push's closing pull leaves earlier inbound intact.

## 2. Marks resolver (D4)

- [x] 2.1 `Services\SyncMarks.cs`: built from `Journal.Collapse()` + the inbound store + graph maps
      (`TargetId → plan TS keys`, `TargetId → project key`); resolves per-key direction and tooltip.
      Leaf rule: plan key only (disk-plane leaves structurally blank). Header rule: union over target key
      + project key (group header / mosaic parent only) + all graph plan keys. Glyphs `←`/`→`/`⇄`/blank.
- [x] 2.2 Tooltip text: leaf = one line per field per direction with old → new (inbound "BIRDWATCHER",
      outbound "unpushed"); header = direction counts; null when blank.
- [x] 2.3 Tests: each mark state; folded multi-plan key rolls up to header via graph map; project entry
      marks group header / mosaic parent, never panels; template journal entries mark nothing; tooltip
      content for leaf and header.

## 3. Row plumbing + view-model lifecycle (D5)

- [x] 3.1 Mutable `MarkGlyph`/`MarkTooltip` (PropertyChanged) on `ReconciliationRow`,
      `TargetGroupRow`, `PanelGroupRow` (shared via `AggregateHeaderRow` where sensible).
- [x] 3.2 `MainViewModel.RefreshAllMarks()`: sweep `_groups` → panels → children → detail rows in place
      (raise only on change; never replace the `Rows` collection). Call at end of `ApplyFilters`, after
      every applied edit (`ApplyOutcome`), after push without reload (partial / mid-push-edit paths),
      after Discard.
- [x] 3.3 VM tests (stub gate seam, spec scenarios): commit → row + owning header mark in place; push
      full success → applied `→` clears, `⇄` collapses to `←`, masked-actuals-only rows go blank;
      partial push → retained rows keep `→`; discard → `→` gone; offline/Continue-local session → no `←`;
      restart with journal sidecar → `→` restored.

## 4. Grid column (D6)

- [x] 4.1 Insert `<ColumnDefinition Width="24"/>` at position 0 in all four duplicated width blocks
      (group / filter / panel templates + sticky header row) and bump every `Grid.Column` index by one;
      header row column 0 stays empty.
- [x] 4.2 Mark `TextBlock` centered at column 0 in the three row templates: `{x:Bind MarkGlyph,
      Mode=OneWay}` + `ToolTipService.ToolTip` bound to `MarkTooltip` (no tooltip when blank).
- [ ] 4.3 Run the app (user verifies visually per the run/screenshot rule): alignment across all row
      kinds, mark centering, glyph rendering (U+2190/U+2192/U+21C4), tooltips, live update on edit,
      collapse behavior after push.

## 5. Docs + wrap-up

- [x] 5.1 `ARCHITECTURE.md`: inbound differ (pull choke point, session store, mask) + marks flow;
      `DOMAIN.md`: mark column meaning/glyph convention + add-a-UI-element checklist pass;
      `ROADMAP.md`: recently-shipped entry. Same commit as the code.
- [x] 5.2 Full build + all tests green per `VERIFICATION.md`; state what needs human visual verification.
