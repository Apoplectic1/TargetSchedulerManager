# Tasks — add-inbox-v2-emission

## 1. Contract alignment

- [x] 1.1 Cross-repo checkpoint: implement against ISM's revised
      `catalog-inbox-contract.md` (v2) only after its task 1.1 lands; verify the sentinel
      table against AL `TsIntentImporter` (the single source), not the doc from memory.

## 2. Emitter

- [x] 2.1 Envelope constant → `v: 2`.
- [x] 2.2 `project-upsert` record widens: settings block + `is_mosaic`, full committed values
      from the local working copy; sentinel→null translations per contract v2 table.
- [x] 2.3 `exposure-template-upsert` mirror widens: moon-relax triplet.
- [x] 2.4 Observed emission extends to project rows: pull-diff correlates project-table rows
      (by TS guid) and emits full-value v2 `project-upsert`s for observed field changes on
      existing rows; remotely-added projects and plan/template rows stay silent.
- [x] 2.5 Verify the authored-by-construction premise for project columns: neither TS's
      runtime nor TSM's write-back writes the project table (grep TS reference clone + TSM
      write-back paths); record the result in the change's design notes.

## 3. Tests

- [x] 3.1 Fixture emission: pushed settings edit emits a project-upsert carrying the full v2
      block; sentinels emit as JSON null (`minimumaltitude 0.0`, `maximumAltitude 0.0`).
- [x] 3.2 Template mirror carries the relax triplet on every plan-upsert ride-along and on a
      pushed template edit.
- [x] 3.3 Existing v1 emission suite re-pinned at v2 (envelope + widened ops); no sent-tracking
      state introduced.
- [x] 3.4 Observed project emission: an inbound project-settings diff emits one full-value
      `project-upsert`; a remotely-added project emits nothing; the closing pull never echoes
      the push's own project edits; plan/template rows stay silent.

## 4. Verification + docs (same commit as code)

- [x] 4.1 Full TSM suite green, zero warnings;
      `openspec validate add-inbox-v2-emission --strict`.
- [x] 4.2 Live ceremony (paired with ISM — ISM task 5.2): drain inbox → install pair → real
      push with a settings edit → verify arrival in Catalog.db.
- [x] 4.3 Docs: ROADMAP one-liner (the duty rider shipped; TSM back to bed), CHANGELOG entry,
      SUBSYSTEMS export-duty section field list if it enumerates ops.
