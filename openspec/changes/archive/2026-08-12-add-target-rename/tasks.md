## 1. Library — the schema row (`..\Library`, separate repo)

- [x] 1.1 Add `target.name` to `TsEditableSchema` (Text, `Guarded: true`, note on NINA file-naming
      follow + coordinate-primary matching); narrow the "identity columns" exclusion comment to
      RA/Dec/epoch
- [x] 1.2 Update Library tests that pin the target field set / schema counts; add coverage that the
      editor whitelist now admits `target.name` and that its edit clears no cadence rows

## 2. App — rename surface and re-shape

- [x] 2.1 Verify the generated target form renders Name guarded (arm-to-edit) with no app change
      (schema-driven); add the blank/whitespace-name refusal so an empty rename never reaches the
      gate (control reverts, matching numeric clamp behavior)
- [x] 2.2 Wire the in-place mirror while the dialog is open (group/panel header name text) and make
      a committed `target.name` edit trigger the close-time no-pull re-reconcile (the `reshaped`
      path — name is group identity, not a pairing key; extend the trigger accordingly)
- [x] 2.3 App tests: rename journals + replays like any intent edit (journal label, push review
      line); blank-name refusal; close-time re-reconcile fires on a name commit and not on
      non-keying edits

## 3. App — observed-emission at pull (design D3–D5)

- [x] 3.1 Add `CatalogInboxExporter.ExportObservedTargets(localDbPath, targetGuids, observedAt,
      inboxDir)` — reuse `ReadRows`/serialization/`WriteInbox`; full-value `target-upsert` per guid;
      `at` = pull completion time
- [x] 3.2 Add the same-second collision rule to `WriteInbox` (advance the stamp to the next free
      second, `CreateNew` stays the guard, never overwrite)
- [x] 3.3 Hook the pull path: after each pull's inbound diff (open pull, Pull-now, closing pull),
      filter Target-table changes on existing rows (exclude `(new)` entries), and emit; no
      target-table changes → no file; consume the single pull's diff, not the accumulated store
- [x] 3.4 Failure posture: emission fault after a committed pull aborts loudly (log naming inbox
      path + operation, user-visible error), never rolls back or skips silently (rule #16)
- [x] 3.5 App tests (file-level fixtures, never `Catalog.db`): BIRDWATCHER rename → one full-value
      `target-upsert`; closing pull never echoes the push's own changes; remotely-added target
      silent; inbound plan/project/template changes silent; quiet pull writes no file; same-second
      push+pull emissions land in distinct files

## 4. Verify + docs (same commit as code)

- [x] 4.1 Build both repos, run Library + App test suites green
- [x] 4.2 Update reference docs: ROADMAP (Phase 5 export-duty entry — observed-emission shipped,
      P9 residual closed; drop the manual-flush note), SUBSYSTEMS.md → TS sync model (second
      emission point), UI.md → Editing (guarded rename, close-time re-reconcile), CHANGELOG entry
- [x] 4.3 Field verification (user): rename a target in TSM end-to-end (arm → commit → push → TS +
      inbox record); confirm the pending `Cygnus Loop P9` BIRDWATCHER rename auto-flushes at the
      next session's open pull and ISM ingests it
